using Godot;
using Nmoku.Core;
using Nmoku.Core.Camera;

namespace Nmoku.Game.Views;

/// <summary>
/// 3D 左视图（migration-plan §3.2）：MultiMesh 黑/白实体 + 每层 ghost（逐层透明度，§8.2/B7），
/// 网格线单 ArrayMesh（§8.3 合批），悬停高亮占位（R4：精确拾取联动 M3 做）。
/// 棋子全量重建（≤1014 实例，成本可忽略，不做增量）；ghost 按层分桶是计划 §3.2 认可的排序降级方案（R3）。
/// </summary>
public sealed partial class Board3DView : Node3D
{
    private MultiMeshInstance3D _blackOpaque = null!;
    private MultiMeshInstance3D _whiteOpaque = null!;
    private readonly List<MultiMeshInstance3D> _ghostBuckets = new(); // 下标 = z*2+颜色
    private MeshInstance3D _grid = null!;
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

        _blackOpaque = BuildStoneBucket("black_opaque", new Color(0.09f, 0.09f, 0.10f), 1f, transparent: false);
        _whiteOpaque = BuildStoneBucket("white_opaque", new Color(0.92f, 0.93f, 0.95f), 1f, transparent: false);
        for (int z = 0; z < layers; z++)
        {
            // 同层 worldY 相同 → 层内透明度一致，材质按层设置（§8.2 连续透明度的 MultiMesh 化）
            float alpha = (float)CameraMath.ComputeOpacity(z * spacing, layers, spacing);
            _ghostBuckets.Add(BuildStoneBucket($"black_ghost_{z}", new Color(0.09f, 0.09f, 0.10f), alpha, transparent: true));
            _ghostBuckets.Add(BuildStoneBucket($"white_ghost_{z}", new Color(0.92f, 0.93f, 0.95f), alpha, transparent: true));
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

    /// <summary>悬停高亮占位：蓝点落在 (x,y,z) 格心，null 隐藏。</summary>
    public void SetHover(Coordinate3D? cell)
    {
        if (cell is { } c)
        {
            var world = CoordMapper.LogicalToWorld(c.X, c.Y, c.Z, _spacing);
            _hover.Position = new Vector3((float)world.X, (float)world.Y + 0.45f, (float)world.Z);
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

    private MeshInstance3D BuildGrid(int boardSize, double spacing)
    {
        var verts = new List<Vector3>();
        for (int z = 0; z < _layers; z++)
        {
            float height = (float)(z * spacing);
            for (int y = 0; y < boardSize; y++)
            {
                verts.Add(new Vector3(0, y, height));
                verts.Add(new Vector3(boardSize - 1, y, height));
            }
            for (int x = 0; x < boardSize; x++)
            {
                verts.Add(new Vector3(x, 0, height));
                verts.Add(new Vector3(x, boardSize - 1, height));
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.62f, 0.66f, 0.70f),
        });

        return new MeshInstance3D { Name = "grid", Mesh = mesh };
    }

    private MeshInstance3D BuildHover()
    {
        var mesh = new PlaneMesh
        {
            Size = new Vector2(0.9f, 0.9f),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.30f, 0.55f, 1.00f, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        var instance = new MeshInstance3D { Name = "hover", Mesh = mesh, Visible = false };
        return instance;
    }
}
