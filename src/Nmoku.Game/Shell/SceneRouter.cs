using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>路由层级：L0 基座（互斥）→ L1 模态 → L2 浮层/HUD 附件。</summary>
public enum RouteLayer
{
    Base,
    Modal,
    Overlay,
}

/// <summary>背板策略：随路由挂载于同层内容之下；Dim/Blur 兼具输入隔离（MouseFilter Stop）。</summary>
public enum BackdropKind
{
    None,
    Dim,
    Blur,
    Opaque,
}

/// <summary>KeepAlive 基座屏原地刷新协议：切主题/切语言时不销毁重建，保留当前交互态。</summary>
public interface IKeepAliveRefreshable
{
    void RefreshTheme();
    void RefreshLocale();
}

/// <summary>
/// 路由定义（声明式，类 vue-router routes + keep-alive）：改表即改行为，无需动逻辑。
/// KeepAlive 仅用于 L0 基座（game=保留现场为人类拍板 2026-09-19）；模态/浮层一律销毁重建。
/// </summary>
public sealed class RouteDef
{
    public required string Id { get; init; }
    public required RouteLayer Layer { get; init; }
    public required Func<Control> Factory { get; init; }
    public bool KeepAlive { get; init; }
    public BackdropKind Backdrop { get; init; } = BackdropKind.None;
    /// <summary>Dim/Blur 背板的压暗强度。</summary>
    public float DimAlpha { get; init; } = 0.55f;
}

/// <summary>
/// 三层场景路由器（状态机的视图投影，01-core §3.4 不改）：基座栈 + 浮层栈差分挂载/卸载；
/// KeepAlive 路由常驻树内仅切可见性；背板随路由同层挂载。GameState→路由栈映射见 <see cref="StackFor"/>。
/// </summary>
public sealed partial class SceneRouter : Node
{
    private readonly Dictionary<string, RouteDef> _defs;
    private readonly Dictionary<string, Control> _content = []; // 全部已挂载内容（含 KeepAlive；卸载据此释放）
    private readonly Dictionary<string, Control> _backdrops = [];
    private readonly List<string> _inTree = [];
    private readonly List<string> _overlays = []; // 浮层栈（底→顶，独立于状态机）
    private string[] _baseStack = [];
    private CanvasLayer _layerBase = null!;
    private CanvasLayer _layerModal = null!;
    private CanvasLayer _layerOverlay = null!;
    private static Shader? _blurShader;

    public SceneRouter(IEnumerable<RouteDef> defs) =>
        _defs = defs.ToDictionary(d => d.Id);

    public override void _Ready()
    {
        _layerBase = new CanvasLayer { Layer = 0 };
        _layerModal = new CanvasLayer { Layer = 10 };
        _layerOverlay = new CanvasLayer { Layer = 20 };
        AddChild(_layerBase);
        AddChild(_layerModal);
        AddChild(_layerOverlay);
    }

    /// <summary>状态机 → 路由栈投影（唯一映射点，取代散落的可见性硬编码）。</summary>
    public static string[] StackFor(GameState state) => state switch
    {
        GameState.Title => ["title"],
        GameState.Playing => ["game"],
        GameState.Paused => ["game", "pause"],
        GameState.GameOver => ["game", "game-over"],
        GameState.GuideFromTitle => ["title", "guide"],
        GameState.GuideFromGame => ["game", "guide"],
        GameState.Replay => ["game", "replay-hud"],
        _ => ["title"],
    };

    /// <summary>状态驱动：重置基座栈并收起全部浮层。</summary>
    public void ShowState(GameState state)
    {
        _baseStack = StackFor(state);
        _overlays.Clear();
        Apply();
    }

    public void PushOverlay(string id)
    {
        if (!_overlays.Contains(id))
        {
            _overlays.Add(id);
        }
        Apply();
    }

    /// <summary>关闭指定浮层（关闭后背板与内容一并卸载）。</summary>
    public bool PopOverlay(string id)
    {
        if (!_overlays.Remove(id))
        {
            return false;
        }
        Apply();
        return true;
    }

    public bool IsOverlayOpen(string id) => _overlays.Contains(id);

    public bool HasOverlay => _overlays.Count > 0;

    /// <summary>最顶浮层（Esc 逐层冒泡的落点）。</summary>
    public string? TopOverlay => _overlays.Count > 0 ? _overlays[^1] : null;

    /// <summary>当前在树内的路由（含隐藏的 KeepAlive 基座）。</summary>
    public IReadOnlyList<string> Mounted => _inTree;

    /// <summary>重建路由实例（工厂重跑、事件重接线）：主题切换等需要壳屏整体换肤时用；
    /// KeepAlive 路由优先走原地刷新（<see cref="IKeepAliveRefreshable"/>），禁止销毁 "game" 基座。</summary>
    public void Rebuild(string id, bool localeChanged = false)
    {
        if (!_inTree.Contains(id) || !_content.TryGetValue(id, out var content))
        {
            return;
        }
        if (id == "game")
        {
            return; // 保护对局基座现场，永不销毁
        }
        if (_defs.TryGetValue(id, out var def) && def.KeepAlive && content is IKeepAliveRefreshable refreshable)
        {
            // 背板色若为 Opaque 同步更新
            if (_backdrops.TryGetValue(id, out var backdrop) && def.Backdrop == BackdropKind.Opaque && backdrop is ColorRect rect)
            {
                rect.Color = Palette.Active.Background;
            }
            if (localeChanged)
            {
                refreshable.RefreshLocale();
            }
            else
            {
                refreshable.RefreshTheme();
            }
            return;
        }
        bool wasVisible = content.Visible;
        Unmount(id);
        Mount(id);
        SetRouteVisible(id, wasVisible);
    }

    private bool _primed;

    private void Apply()
    {
        var target = new List<string>(_baseStack);
        foreach (string id in _overlays)
        {
            if (!target.Contains(id))
            {
                target.Add(id);
            }
        }

        // KeepAlive 基座首次全部入树常驻（保留现场），之后仅切可见性、不参与卸载
        if (!_primed)
        {
            _primed = true;
            foreach (var def in _defs.Values.Where(d => d.KeepAlive))
            {
                if (!_inTree.Contains(def.Id))
                {
                    Mount(def.Id);
                }
            }
        }

        foreach (string id in target.Where(id => !_inTree.Contains(id)))
        {
            Mount(id);
        }
        foreach (string id in _inTree.Where(id => !target.Contains(id)).ToList())
        {
            if (!_defs[id].KeepAlive)
            {
                Unmount(id);
            }
        }
        foreach (string id in _inTree)
        {
            SetRouteVisible(id, target.Contains(id));
        }
    }

    private void Mount(string id)
    {
        var def = _defs[id];
        var layer = LayerFor(def.Layer);

        Control content;
        if (def.KeepAlive && _content.TryGetValue(id, out var cached))
        {
            content = cached;
        }
        else
        {
            content = def.Factory();
            if (def.Backdrop != BackdropKind.None)
            {
                var backdrop = CreateBackdrop(def);
                _backdrops[id] = backdrop;
                layer.AddChild(backdrop);
            }
            layer.AddChild(content);
        }
        _content[id] = content;
        _inTree.Add(id);
        SetRouteVisible(id, true);
    }

    private void Unmount(string id)
    {
        // 背板与内容一律释放（销毁型路由；KeepAlive 基座不走进此路径，仅切可见性）。
        // 内容此前只在 KeepAlive 字典登记导致销毁型路由残留可见、继续吃点击（"存档菜单取消无响应"根因）。
        if (_backdrops.Remove(id, out var backdrop))
        {
            backdrop.QueueFree();
        }
        if (_content.Remove(id, out var content))
        {
            content.QueueFree();
        }
        _inTree.Remove(id);
    }

    private void SetRouteVisible(string id, bool visible)
    {
        if (_content.TryGetValue(id, out var content))
        {
            content.Visible = visible;
        }
        if (_backdrops.TryGetValue(id, out var backdrop))
        {
            backdrop.Visible = visible;
        }
    }

    private CanvasLayer LayerFor(RouteLayer layer) => layer switch
    {
        RouteLayer.Modal => _layerModal,
        RouteLayer.Overlay => _layerOverlay,
        _ => _layerBase,
    };

    private Control CreateBackdrop(RouteDef def)
    {
        Control backdrop = def.Backdrop switch
        {
            BackdropKind.Dim => new ColorRect { Color = new Color(0f, 0f, 0f, def.DimAlpha) },
            BackdropKind.Opaque => new ColorRect { Color = Palette.Active.Background },
            BackdropKind.Blur => MakeBlurBackdrop(def.DimAlpha),
            _ => new ColorRect(),
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop; // 输入隔离：模态层拦截基座鼠标
        return backdrop;
    }

    /// <summary>screen-texture 九点高斯模糊 + 压暗，单节点同时完成 blur 与 dim。</summary>
    private static Control MakeBlurBackdrop(float dim)
    {
        _blurShader ??= new Shader
        {
            Code = """
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float blur_strength : hint_range(0.0, 6.0) = 1.6;
uniform float dim : hint_range(0.0, 1.0) = 0.45;
void fragment() {
    vec2 px = SCREEN_PIXEL_SIZE * blur_strength;
    vec3 c = texture(screen_tex, SCREEN_UV).rgb * 4.0;
    c += texture(screen_tex, SCREEN_UV + vec2(px.x, 0.0)).rgb * 2.0;
    c += texture(screen_tex, SCREEN_UV - vec2(px.x, 0.0)).rgb * 2.0;
    c += texture(screen_tex, SCREEN_UV + vec2(0.0, px.y)).rgb * 2.0;
    c += texture(screen_tex, SCREEN_UV - vec2(0.0, px.y)).rgb * 2.0;
    c += texture(screen_tex, SCREEN_UV + px).rgb;
    c += texture(screen_tex, SCREEN_UV - px).rgb;
    c += texture(screen_tex, SCREEN_UV + vec2(px.x, -px.y)).rgb;
    c += texture(screen_tex, SCREEN_UV - vec2(px.x, -px.y)).rgb;
    c /= 16.0;
    COLOR = vec4(c * (1.0 - dim), 1.0);
}
""",
        };
        var rect = new ColorRect();
        rect.Material = new ShaderMaterial { Shader = _blurShader };
        ((ShaderMaterial)rect.Material).SetShaderParameter("dim", dim);
        return rect;
    }
}
