using Godot;
using Nmoku.Core;
using Nmoku.Core.Camera;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Views;

/// <summary>
/// 3D 左视图（migration-plan §3.2，B8 朝向：层=世界 XY 竖直面、沿 Z 排布）。
/// 棋子六桶（人类反馈 2026-09-19）：焦点层实体 / 被辅助线标记的跨层子强化 / 其余非焦点大幅减弱，
/// 黑白分色透明度走 Palette（浅色主题可反转）。网格逐层显隐：焦点全亮、相邻 0.35、远层 0（断崖）。
/// 悬停预选球（3D 蓝点跟随 2D 悬停，R4-12）+ 辅助线渲染（B9 首子即停载荷，R4-11）。
/// 棋子全量重建（≤1014，成本可忽略，不做增量）。
/// </summary>
public sealed partial class Board3DView : Node3D
{
    /// <summary>语义调色板（色弱切换时由 Main 推送 ApplyPalette）。</summary>
    public Palette Theme { get; private set; } = Palette.Default;

    private MultiMeshInstance3D _focusBlack = null!;
    private MultiMeshInstance3D _focusWhite = null!;
    private MultiMeshInstance3D _markedBlack = null!;
    private MultiMeshInstance3D _markedWhite = null!;
    private MultiMeshInstance3D _fadedBlack = null!;
    private MultiMeshInstance3D _fadedWhite = null!;
    private readonly List<MultiMeshInstance3D> _gridLayers = new(); // 下标 = 层号
    private readonly List<StandardMaterial3D> _gridLayerMats = new();
    private MultiMeshInstance3D _connectors = null!;
    private MeshInstance3D _hover = null!;
    private MeshInstance3D _lastMoveRing = null!;
    private Node3D _auxRoot = null!;

    // 材质引用（ApplyPalette 原地换色，避免整棵重建）
    private StandardMaterial3D _focusBlackMat = null!;
    private StandardMaterial3D _focusWhiteMat = null!;
    private StandardMaterial3D _markedBlackMat = null!;
    private StandardMaterial3D _markedWhiteMat = null!;
    private StandardMaterial3D _fadedBlackMat = null!;
    private StandardMaterial3D _fadedWhiteMat = null!;
    private StandardMaterial3D _connectorMat = null!;
    private StandardMaterial3D _hoverMat = null!;
    private StandardMaterial3D _lastMoveRingMat = null!;

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
        _gridLayers.Clear();
        _gridLayerMats.Clear();

        (_focusBlack, _focusBlackMat) = BuildStoneBucket("focus_black", Theme.StoneBlack, 1f, transparent: false);
        (_focusWhite, _focusWhiteMat) = BuildStoneBucket("focus_white", Theme.StoneWhite, 1f, transparent: false);
        (_markedBlack, _markedBlackMat) = BuildStoneBucket("marked_black", Theme.StoneBlack, Theme.GhostMarkedBlack, transparent: true);
        (_markedWhite, _markedWhiteMat) = BuildStoneBucket("marked_white", Theme.StoneWhite, Theme.GhostMarkedWhite, transparent: true);
        (_fadedBlack, _fadedBlackMat) = BuildStoneBucket("faded_black", Theme.StoneBlack, Theme.GhostBlack, transparent: true);
        (_fadedWhite, _fadedWhiteMat) = BuildStoneBucket("faded_white", Theme.StoneWhite, Theme.GhostWhite, transparent: true);

        BuildGrid(boardSize, spacing);

        _hover = BuildHover();
        AddChild(_hover);
        _lastMoveRing = BuildLastMoveRing();
        AddChild(_lastMoveRing);
        _auxRoot = new Node3D { Name = "aux" };
        AddChild(_auxRoot);
    }

    /// <summary>色弱/主题切换：原地更新全部材质颜色与透明度（不重建结构）。</summary>
    public void ApplyPalette(Palette theme)
    {
        Theme = theme;
        _focusBlackMat.AlbedoColor = theme.StoneBlack;
        _focusWhiteMat.AlbedoColor = theme.StoneWhite;
        _markedBlackMat.AlbedoColor = WithAlpha(theme.StoneBlack, theme.GhostMarkedBlack);
        _markedWhiteMat.AlbedoColor = WithAlpha(theme.StoneWhite, theme.GhostMarkedWhite);
        _fadedBlackMat.AlbedoColor = WithAlpha(theme.StoneBlack, theme.GhostBlack);
        _fadedWhiteMat.AlbedoColor = WithAlpha(theme.StoneWhite, theme.GhostWhite);
        _connectorMat.AlbedoColor = theme.ConnectorLine;
        _hoverMat.AlbedoColor = theme.HoverHighlight;
        _lastMoveRingMat.AlbedoColor = theme.LastMoveRing;
    }

    private static Color WithAlpha(Color c, float alpha) => new(c.R, c.G, c.B, alpha);

    /// <summary>
    /// 按棋盘现状全量重建棋子实例。桶归属：焦点层=实体；非焦点被辅助线标记=强化；
    /// 其余非焦点=大幅减弱（人类反馈 2026-09-19）。
    /// </summary>
    public void Rebuild(Board board, int focusZ, AuxScanResult aux)
    {
        var marked = new HashSet<Coordinate3D>();
        foreach (var m in aux.PlanarMarkers)
        {
            marked.Add(m.Position);
        }
        foreach (var m in aux.SpaceMarkers)
        {
            marked.Add(m.Position);
        }

        var buckets = new[]
        {
            _focusBlack, _focusWhite, _markedBlack, _markedWhite, _fadedBlack, _fadedWhite,
        };
        var counts = new int[buckets.Length];
        var cursors = new int[buckets.Length];

        void BucketBy(int x, int y, int z, out int bucket)
        {
            int color = board.Get(x, y, z) == CellState.Black ? 0 : 1;
            if (z == focusZ)
            {
                bucket = color;                    // 0/1 焦点实体
            }
            else if (marked.Contains(new Coordinate3D(x, y, z)))
            {
                bucket = 2 + color;                // 2/3 标记强化
            }
            else
            {
                bucket = 4 + color;                // 4/5 大幅减弱
            }
        }

        board.ForEach((x, y, z, state) =>
        {
            if (state != CellState.Empty)
            {
                BucketBy(x, y, z, out int bucket);
                counts[bucket]++;
            }
        });

        for (int i = 0; i < buckets.Length; i++)
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
            var world = CoordMapper.LogicalToWorld(x, y, z, _spacing, _boardSize, _layers);
            buckets[bucket].Multimesh!.SetInstanceTransform(
                cursors[bucket]++,
                Transform3D.Identity.Translated(new Vector3((float)world.X, (float)world.Y, (float)world.Z)));
        });
    }

    private bool _gridAllFull;    // O 键开关：所有格线强制全亮
    private int _lastFocusZ;

    /// <summary>网格逐层显隐（人类反馈 2026-09-19）：反比例雾化 alpha=1/(1+d)——焦点 1.0、相邻 0.5、
    /// 远层渐近 0 但永远留一点（像雾）；O 键切换全亮 1.0。</summary>
    public void SetGridFocus(int focusZ)
    {
        _lastFocusZ = focusZ;
        ApplyGridAlphas();
    }

    /// <summary>O 键：所有格线全亮 1.0 ↔ 反比例雾化。</summary>
    public void ToggleGridAll()
    {
        _gridAllFull = !_gridAllFull;
        ApplyGridAlphas();
    }

    private void ApplyGridAlphas()
    {
        for (int z = 0; z < _gridLayerMats.Count; z++)
        {
            float alpha;
            if (_gridAllFull)
            {
                alpha = 1f;
            }
            else
            {
                int d = Math.Abs(z - _lastFocusZ);
                alpha = 1f / (1f + d); // 反比例雾化
            }
            var c = Theme.GridLine;
            _gridLayerMats[z].AlbedoColor = WithAlpha(c, alpha);
        }
    }

    /// <summary>悬停预选球（R4-12：3D 蓝点跟随 2D 悬停），落格心，null 隐藏。</summary>
    public void SetHover(Coordinate3D? cell)
    {
        if (cell is { } c)
        {
            var world = CoordMapper.LogicalToWorld(c.X, c.Y, c.Z, _spacing, _boardSize, _layers);
            _hover.Position = new Vector3((float)world.X, (float)world.Y, (float)world.Z);
            _hover.Visible = true;
        }
        else
        {
            _hover.Visible = false;
        }
    }

    /// <summary>
    /// 最后手标记（源 LeftPanel lastMoveRing：红环 0.45–0.55、色弱换黄、压顶渲染）：
    /// 公告板圆环始终面朝摄像机、套在棋子中心，与蓝色悬停球/辅助线小点在形状上区分。
    /// </summary>
    public void SetLastMove(Coordinate3D? cell)
    {
        if (cell is { } c)
        {
            var world = CoordMapper.LogicalToWorld(c.X, c.Y, c.Z, _spacing, _boardSize, _layers);
            _lastMoveRing.Position = new Vector3((float)world.X, (float)world.Y, (float)world.Z);
            _lastMoveRing.Visible = true;
        }
        else
        {
            _lastMoveRing.Visible = false;
        }
    }

    /// <summary>径向渐变圆环纹理（白色环带，颜色由 AlbedoColor 着色）。</summary>
    private static GradientTexture2D BuildRingTexture()
    {
        var gradient = new Gradient
        {
            Colors = new[]
            {
                new Color(1, 1, 1, 0), new Color(1, 1, 1, 0),
                new Color(1, 1, 1, 1), new Color(1, 1, 1, 0),
            },
            Offsets = new[] { 0f, 0.78f, 0.88f, 1f },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 128,
            Height = 128,
        };
    }

    /// <summary>最后手环：公告板四边形 + 圆环纹理，无深度测试压顶（源 renderOrder 998 语义）。</summary>
    private MeshInstance3D BuildLastMoveRing()
    {
        _lastMoveRingMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Theme.LastMoveRing,
            AlbedoTexture = BuildRingTexture(),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        return new MeshInstance3D
        {
            Name = "last_move_ring",
            Mesh = new QuadMesh { Size = new Vector2(1.15f, 1.15f), Material = _lastMoveRingMat },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>
    /// 辅助线渲染（R4-11 + B9 载荷）：3D = SpaceLines + PlanarMarkers + SpaceMarkers；
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
                var s = CoordMapper.LogicalToWorld(line.Start.X, line.Start.Y, line.Start.Z, _spacing, _boardSize, _layers);
                var e = CoordMapper.LogicalToWorld(line.End.X, line.End.Y, line.End.Z, _spacing, _boardSize, _layers);
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
            var world = CoordMapper.LogicalToWorld(marker.Position.X, marker.Position.Y, marker.Position.Z, _spacing, _boardSize, _layers);
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
    /// 网格线（B8 朝向：层=世界 XY 竖直面、沿 Z 排布）——**每层独立材质**供显隐分级；
    /// 另含层连接立柱（四角+中心=天元/地元锚点，由 boardSize 推导：(size-1)/2 与三界点，禁止硬编码）。
    /// 细盒代替 1px 线；配合 MSAA 4× 抗锯齿。
    /// </summary>
    private void BuildGrid(int boardSize, double spacing)
    {
        const float thickness = 0.05f;
        float length = boardSize - 1;
        float mid = length / 2;

        for (int z = 0; z < _layers; z++)
        {
            var layerMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Theme.GridLine,
                // 必须开 Alpha：不透明材质会忽略 AlbedoColor 的 alpha 通道，逐层显隐失效（人类反馈 2026-09-19）
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            var grid = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh { Size = new Vector3(1, 1, 1), Material = layerMat },
                InstanceCount = 2 * boardSize,
            };

            // B10：与 CoordMapper 同步旋转——层 z 的格面须与该层棋子同深度，否则显隐错位
            float depth = (float)((_layers - 1 - z) * spacing);
            int i = 0;
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

            _gridLayers.Add(new MultiMeshInstance3D
            {
                Name = $"grid_{z}",
                Multimesh = grid,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _gridLayerMats.Add(layerMat);
            AddChild(_gridLayers[z]);
        }

        _connectorMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Theme.ConnectorLine,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var connectorMultimesh = new MultiMesh
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
            connectorMultimesh.SetInstanceTransform(r, new Transform3D(zAxis, new Vector3(rx, ry, maxDepth / 2)));
        }

        _connectors = new MultiMeshInstance3D
        {
            Name = "connectors",
            Multimesh = connectorMultimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_connectors);
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
