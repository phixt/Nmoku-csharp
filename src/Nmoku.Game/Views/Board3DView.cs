using Godot;
using Nmoku.Core;
using Nmoku.Core.Camera;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Views;

/// <summary>
/// 3D 左视图（migration-plan §3.2，B8 朝向：层=世界 XY 竖直面、沿 Z 排布）：
/// MultiMesh 黑/白实体 + 每层 ghost（焦点层强制实体、其余层 §8.2 连续透明度——D2），
/// 网格/立柱 MultiMesh，悬停预选球（3D 蓝点跟随 2D 悬停，R4-12），
/// 辅助线/标记点（AuxScan 载荷，R4-11）。颜色全走 Palette；棋子全量重建（≤1014，不做增量）。
/// </summary>
public sealed partial class Board3DView : Node3D
{
    /// <summary>语义调色板（色弱切换时由 Main 推送 ApplyPalette）。</summary>
    public Palette Theme { get; private set; } = Palette.Default;

    private MultiMeshInstance3D _blackOpaque = null!;
    private MultiMeshInstance3D _whiteOpaque = null!;
    private readonly List<MultiMeshInstance3D> _ghostBuckets = new(); // 下标 = z*2+颜色
    private MultiMeshInstance3D _grid = null!;
    private MeshInstance3D _hover = null!;
    private Node3D _auxRoot = null!;

    // 材质引用（ApplyPalette 原地换色，避免整棵重建）
    private StandardMaterial3D _blackOpaqueMat = null!;
    private StandardMaterial3D _whiteOpaqueMat = null!;
    private readonly List<(StandardMaterial3D Mat, Color Base, float Alpha)> _ghostMats = new();
    private StandardMaterial3D _gridMat = null!;
    private StandardMaterial3D _connectorMat = null!;
    private StandardMaterial3D _hoverMat = null!;

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
        _ghostMats.Clear();

        (_blackOpaque, _blackOpaqueMat) = BuildStoneBucket("black_opaque", Theme.StoneBlack, 1f, transparent: false);
        (_whiteOpaque, _whiteOpaqueMat) = BuildStoneBucket("white_opaque", Theme.StoneWhite, 1f, transparent: false);
        for (int z = 0; z < layers; z++)
        {
            // 同层 worldZ 相同 → 层内透明度一致（§8.2 连续透明度的 MultiMesh 化）
            float alpha = (float)CameraMath.ComputeOpacity(z * spacing, layers, spacing);
            var (blackGhost, blackMat) = BuildStoneBucket($"black_ghost_{z}", Theme.StoneBlack, alpha, transparent: true);
            var (whiteGhost, whiteMat) = BuildStoneBucket($"white_ghost_{z}", Theme.StoneWhite, alpha, transparent: true);
            _ghostBuckets.Add(blackGhost);
            _ghostBuckets.Add(whiteGhost);
            _ghostMats.Add((blackMat, Theme.StoneBlack, alpha));
            _ghostMats.Add((whiteMat, Theme.StoneWhite, alpha));
        }

        (_grid, _gridMat) = BuildGrid(boardSize, spacing);
        AddChild(_grid);
        _hover = BuildHover();
        AddChild(_hover);
        _auxRoot = new Node3D { Name = "aux" };
        AddChild(_auxRoot);
    }

    /// <summary>色弱/主题切换：原地更新全部材质颜色（不重建结构）。</summary>
    public void ApplyPalette(Palette theme)
    {
        Theme = theme;
        _blackOpaqueMat.AlbedoColor = theme.StoneBlack;
        _whiteOpaqueMat.AlbedoColor = theme.StoneWhite;
        foreach (var (mat, @base, alpha) in _ghostMats)
        {
            mat.AlbedoColor = new Color(@base.R, @base.G, @base.B, alpha);
        }
        _gridMat.AlbedoColor = theme.GridLine;
        _connectorMat.AlbedoColor = theme.ConnectorLine;
        _hoverMat.AlbedoColor = theme.HoverHighlight;
        QueueRedraw3D();
    }

    private void QueueRedraw3D()
    {
        // 材质换色即时生效，无需重建；预留扩展点
    }

    /// <summary>按棋盘现状全量重建棋子实例；焦点层强制实体，其余层按连续透明度（D2）。</summary>
    public void Rebuild(Board board, int focusZ)
    {
        var buckets = new List<MultiMeshInstance3D> { _blackOpaque, _whiteOpaque };
        buckets.AddRange(_ghostBuckets);

        var counts = new int[buckets.Count];
        var cursors = new int[buckets.Count];

        void BucketBy(int x, int y, int z, out int bucket)
        {
            int color = board.Get(x, y, z) == CellState.Black ? 0 : 1;
            double opacity = z == focusZ
                ? 1.0
                : CameraMath.ComputeOpacity(z * _spacing, _layers, _spacing);
            bucket = opacity >= 1.0 - GameConfig.CameraEpsilon ? color : 2 + z * 2 + color;
        }

        board.ForEach((x, y, z, state) =>
        {
            if (state != CellState.Empty)
            {
                BucketBy(x, y, z, out int bucket);
                counts[bucket]++;
            }
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
            BucketBy(x, y, z, out int bucket);
            var world = CoordMapper.LogicalToWorld(x, y, z, _spacing);
            buckets[bucket].Multimesh!.SetInstanceTransform(
                cursors[bucket]++,
                Transform3D.Identity.Translated(new Vector3((float)world.X, (float)world.Y, (float)world.Z)));
        });
    }

    /// <summary>悬停预选球（R4-12：3D 蓝点跟随 2D 悬停），落格心，null 隐藏。</summary>
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

    /// <summary>
    /// 辅助线渲染（R4-11）：3D 载荷 = SpaceLines + PlanarMarkers + SpaceMarkers；
    /// 线为 1px LineMesh（顶点色 0.9），标记为小球 r=0.12（0.8）——与源形态一致。
    /// </summary>
    public void SetAuxData(AuxScanResult data)
    {
        foreach (Node child in _auxRoot.GetChildren())
        {
            _auxRoot.RemoveChild(child);
            child.Free();
        }

        if (data.SpaceLines.Count > 0)
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            foreach (var line in data.SpaceLines)
            {
                var s = CoordMapper.LogicalToWorld(line.Start.X, line.Start.Y, line.Start.Z, _spacing);
                var e = CoordMapper.LogicalToWorld(line.End.X, line.End.Y, line.End.Z, _spacing);
                verts.Add(ToVector3(s));
                verts.Add(ToVector3(e));
                var c = line.Role == AuxRole.Self ? Theme.LineSelf : Theme.LineEnemy;
                var vc = new Color(c.R, c.G, c.B, 0.9f);
                colors.Add(vc);
                colors.Add(vc);
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            mesh.SurfaceSetMaterial(0, new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            });
            _auxRoot.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        foreach (var marker in data.PlanarMarkers.Concat(data.SpaceMarkers))
        {
            var c = marker.Role == AuxRole.Self ? Theme.LineSelf : Theme.LineEnemy;
            var mesh = new SphereMesh
            {
                Radius = 0.12f,
                Height = 0.24f,
                RadialSegments = 8,
                Rings = 6,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(c.R, c.G, c.B, 0.8f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                },
            };
            var world = CoordMapper.LogicalToWorld(marker.Position.X, marker.Position.Y, marker.Position.Z, _spacing);
            _auxRoot.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                Position = ToVector3(world),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private static Vector3 ToVector3(WorldPoint p) => new((float)p.X, (float)p.Y, (float)p.Z);

    private (MultiMeshInstance3D Instance, StandardMaterial3D Material) BuildStoneBucket(
        string name, Color color, float alpha, bool transparent)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, alpha),
            Roughness = 0.35f,
            Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        };
        var mesh = new SphereMesh
        {
            Radius = 0.4f,
            Height = 0.8f,
            RadialSegments = 24,
            Rings = 12,
            Material = material,
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
        return (instance, material);
    }

    /// <summary>
    /// 网格线（B8 朝向：层=世界 XY 竖直面、沿 Z 排布）+ 层连接立柱（四角+中心=天元/地元锚点，
    /// 一律由 boardSize 推导：(size-1)/2 与三界点，禁止硬编码）。
    /// 细盒代替 1px 线（人类反馈：加粗+平滑）；配合 MSAA 4× 抗锯齿。
    /// </summary>
    private (MultiMeshInstance3D Instance, StandardMaterial3D Material) BuildGrid(int boardSize, double spacing)
    {
        const float thickness = 0.05f;
        float length = boardSize - 1;
        float mid = length / 2;

        _gridMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Theme.GridLine,
        };
        var grid = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(1, 1, 1), Material = _gridMat },
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

        _connectorMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Theme.ConnectorLine,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var connectors = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(1, 1, 1), Material = _connectorMat },
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
        return (gridNode, _gridMat);
    }

    /// <summary>悬停预选球：半径略小于棋子（0.32 vs 0.4），空位格上可见。</summary>
    private MeshInstance3D BuildHover()
    {
        _hoverMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Theme.HoverHighlight,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var mesh = new SphereMesh
        {
            Radius = 0.32f,
            Height = 0.64f,
            RadialSegments = 20,
            Rings = 10,
            Material = _hoverMat,
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
