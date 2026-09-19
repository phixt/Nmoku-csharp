using Godot;
using Nmoku.Core;
using Nmoku.Core.Camera;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Views;

/// <summary>
/// 3D 左视图（migration-plan §3.2）：MultiMesh 黑/白实体 + 每层 ghost（逐层透明度，§8.2/B7），
/// 网格线 + 层连接立柱 MultiMesh（B8 朝向），悬停预选球（R4：精确拾取联动 M3 做）。
/// 颜色一律取自 <see cref="Theme"/>（语义调色板），棋子全量重建（≤1014 实例，成本可忽略，不做增量）。
/// </summary>
public sealed partial class Board3DView : Node3D
{
    /// <summary>语义调色板（色弱/明暗/主题系统的唯一取色点）。</summary>
    public Palette Theme { get; set; } = Palette.Default;

    private MultiMeshInstance3D _blackOpaque = null!;
    private MultiMeshInstance3D _whiteOpaque = null!;
    private readonly List<MultiMeshInstance3D> _ghostBuckets = new(); // 下标 = z*2+颜色
    private MultiMeshInstance3D _grid = null!;
    private MeshInstance3D _hover = null!;
    private int _boardSize;
    private int _layers;
    private double _spacing;

    public int BoardSize => _boardSize;
    public int Layers => _layers;
    public double Spacing => _spacing;

    public void Configure(int boardSize, int layers, double spacing)
    {
        _boardSize = boardSize;
        _layers = layers;
        _spacing = spacing;

        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.Free();
        }
        _ghostBuckets.Clear();

        _blackOpaque = BuildStoneBucket("black_opaque", Theme.StoneBlack, 1f, transparent: false);
        _whiteOpaque = BuildStoneBucket("white_opaque", Theme.StoneWhite, 1f, transparent: false);
        for (int z = 0; z < layers; z++)
        {
            // 同层 worldZ 相同 → 层内透明度一致，材质按层设置（§8.2 连续透明度的 MultiMesh 化）
            float alpha = (float)CameraMath.ComputeOpacity(z * spacing, layers, spacing);
            _ghostBuckets.Add(BuildStoneBucket($"black_ghost_{z}", Theme.StoneBlack, alpha, transparent: true));
            _ghostBuckets.Add(BuildStoneBucket($"white_ghost_{z}", Theme.StoneWhite, alpha, transparent: true));
        }

        _grid = BuildGrid(boardSize, spacing);
        AddChild(_grid);
        _hover = BuildHover();
        AddChild(_hover);
    }

    /// <summary>按棋盘现状全量重建棋子实例（黑白 × 实体/逐层 ghost 分桶）。</summary>
    public void Rebuild(Board board)
    {
        int capacity = _boardSize * _boardSize * _layers;
        var buckets = new List<MultiMeshInstance3D> { _blackOpaque, _whiteOpaque };
        buckets.AddRange(_ghostBuckets);

        var counts = new int[buckets.Count];
        var cursors = new int[buckets.Count];

        board.ForEach((x, y, z, state) =>
        {
            if (state == CellState.Empty)
            {
                return;
            }
            int color = state == CellState.Black ? 0 : 1;
            double opacity = CameraMath.ComputeOpacity(z * _spacing, _layers, _spacing);
            int bucket = opacity >= 1.0 - GameConfig.CameraEpsilon ? color : 2 + z * 2 + color;
            counts[bucket]++;
        });

        for (int i = 0; i < buckets.Count; i++)
        {
            buckets[i].Multimesh!.InstanceCount = counts[i];
            buckets[i].Multimesh!.VisibleInstanceCount = counts[i]; // 0 时整桶不可见
        }

        board.ForEach((x, y, z, state) =>
        {
            if (state == CellState.Empty)
            {
                return;
            }
            int color = state == CellState.Black ? 0 : 1;
            double opacity = CameraMath.ComputeOpacity(z * _spacing, _layers, _spacing);
            int bucket = opacity >= 1.0 - GameConfig.CameraEpsilon ? color : 2 + z * 2 + color;
            var world = CoordMapper.LogicalToWorld(x, y, z, _spacing);
            buckets[bucket].Multimesh!.SetInstanceTransform(
                cursors[bucket]++,
                Transform3D.Identity.Translated(new Vector3((float)world.X, (float)world.Y, (float)world.Z)));
        });
    }

    /// <summary>悬停预选：比棋子略小的蓝色小球（源项目形态，人类反馈 2026-09-19），落格心（B8 朝向下与棋子同点）。</summary>
    public void SetHover(Coordinate3D? cell)
    {
        if (cell is { } c)
        {
            var world = CoordMapper.LogicalToWorld(c.X, c.Y, c.Z, _spacing);
            _hover.Position = new Vector3((float)world.X, (float)world.Y, (float)world.Z);
            _hover.Visible = true;
        }
        else
        {
            _hover.Visible = false;
        }
    }

    private MultiMeshInstance3D BuildStoneBucket(string name, Color color, float alpha, bool transparent)
    {
        var mesh = new SphereMesh
        {
            Radius = 0.4f,
            Height = 0.8f,
            RadialSegments = 24,
            Rings = 12,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(color.R, color.G, color.B, alpha),
                Roughness = 0.35f,
                Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            },
        };
        var instance = new MultiMeshInstance3D
        {
            Name = name,
            CastShadow = transparent
                ? GeometryInstance3D.ShadowCastingSetting.Off
                : GeometryInstance3D.ShadowCastingSetting.On,
        };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = 0,
        };
        instance.Multimesh = multimesh;
        AddChild(instance);
        return instance;
    }

    /// <summary>
    /// 网格线（B8 朝向：层=世界 XY 竖直面、沿 Z 排布）+ 层连接立柱（四角+中心=天元/地元锚点，
    /// 一律由 boardSize 推导：(size-1)/2 与三界点，禁止硬编码）。
    /// 细盒代替 1px 线（人类反馈：加粗+平滑）；配合 MSAA 4× 抗锯齿。
    /// </summary>
    private MultiMeshInstance3D BuildGrid(int boardSize, double spacing)
    {
        const float thickness = 0.05f;
        float length = boardSize - 1;
        float mid = length / 2;

        var grid = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh
            {
                Size = new Vector3(1, 1, 1),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = Theme.GridLine,
                },
            },
            InstanceCount = 2 * boardSize * _layers,
        };

        int i = 0;
        for (int z = 0; z < _layers; z++)
        {
            float depth = (float)(z * spacing);
            for (int y = 0; y < boardSize; y++)
            {
                // 沿 X 的横线
                var xAxis = new Basis(new Vector3(length, 0, 0), new Vector3(0, thickness, 0), new Vector3(0, 0, thickness));
                grid.SetInstanceTransform(i++, new Transform3D(xAxis, new Vector3(mid, y, depth)));
            }
            for (int x = 0; x < boardSize; x++)
            {
                // 沿 Y 的竖线（源 buildGridGeometry 第二族，行线竖直）
                var yAxis = new Basis(new Vector3(thickness, 0, 0), new Vector3(0, length, 0), new Vector3(0, 0, thickness));
                grid.SetInstanceTransform(i++, new Transform3D(yAxis, new Vector3(x, mid, depth)));
            }
        }

        var connectors = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh
            {
                Size = new Vector3(1, 1, 1),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = Theme.ConnectorLine,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                },
            },
            InstanceCount = 5,
        };

        // 天元/地元锚点：四角三界点 + 中心 (size-1)/2（偶数尺寸落在格间，与源 (bs-1)/2 一致）
        float maxDepth = (float)((_layers - 1) * spacing);
        float center = (boardSize - 1) / 2f;
        (float, float)[] railPoints = [(0f, 0f), (length, 0f), (0f, length), (length, length), (center, center)];
        for (int r = 0; r < railPoints.Length; r++)
        {
            var (rx, ry) = railPoints[r];
            var zAxis = new Basis(new Vector3(thickness, 0, 0), new Vector3(0, thickness, 0), new Vector3(0, 0, maxDepth));
            connectors.SetInstanceTransform(r, new Transform3D(zAxis, new Vector3(rx, ry, maxDepth / 2)));
        }

        var gridNode = new MultiMeshInstance3D { Name = "grid", Multimesh = grid, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var connectorNode = new MultiMeshInstance3D { Name = "connectors", Multimesh = connectors, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(connectorNode);
        return gridNode;
    }

    /// <summary>悬停预选球：半径略小于棋子（0.32 vs 0.4），空位格上可见。</summary>
    private MeshInstance3D BuildHover()
    {
        var mesh = new SphereMesh
        {
            Radius = 0.32f,
            Height = 0.64f,
            RadialSegments = 20,
            Rings = 10,
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Theme.HoverHighlight,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        var instance = new MeshInstance3D
        {
            Name = "hover",
            Mesh = mesh,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        return instance;
    }
}
