using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键页共用内容（分页版，人类拍板 2026-09-26）：分层=分页——对局/复盘/暂停/Ctrl/Shift/全局
/// 六页，页间用 ←/→（宿主层拦截）或圆圈数字页码点击切换。每页只渲染该层按键：键帽尺寸统一、
/// 上部 ≤3 字注记（KeymapEntry.Short）、下部键名；完整说明统一在页面底部（仅当前层，两列）。
/// 由 F1 帮助浮层（KeymapScreen）与引导屏（GuideScreen）共用；数据全部由 Core KeymapTable 派生。
/// </summary>
public sealed partial class KeymapContent : VBoxContainer
{
    private static readonly KeycapScope[] PageOrder =
    [
        KeycapScope.Game, KeycapScope.Replay, KeycapScope.PauseMenu,
        KeycapScope.Ctrl, KeycapScope.Shift, KeycapScope.Global,
    ];

    private const float Unit = 34f;        // 键帽单元（含间隙）
    private const float KeyHeight = 30f;
    private const float KeyGap = 4f;
    private const float KeyboardPad = 5f;
    private const int DescColumns = 2;

    private int _page;
    private Control _keyboardHost = null!;
    private GridContainer _descHost = null!;
    private HBoxContainer _indicatorRow = null!;

    public KeymapContent()
    {
        AddThemeConstantOverride("separation", 6);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _indicatorRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _indicatorRow.AddThemeConstantOverride("separation", 6);
        AddChild(_indicatorRow);

        _keyboardHost = new Control { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        AddChild(_keyboardHost);

        _descHost = new GridContainer { Columns = DescColumns };
        _descHost.AddThemeConstantOverride("h_separation", 16);
        _descHost.AddThemeConstantOverride("v_separation", 3);
        AddChild(_descHost);

        RebuildPage();
    }

    /// <summary>翻页（宿主在 ←/→ 时调用；±1 循环）。</summary>
    public void PageStep(int delta) => SetPage((_page + delta + PageOrder.Length) % PageOrder.Length);

    public void SetPage(int index)
    {
        _page = Mathf.Clamp(index, 0, PageOrder.Length - 1);
        RebuildPage();
    }

    private void RebuildPage()
    {
        RebuildIndicators();
        RebuildKeyboard(PageOrder[_page]);
        RebuildDescriptions(PageOrder[_page]);
    }

    /// <summary>当前页的登记行（声明序；浮层专属行不入页）。</summary>
    private static List<KeymapEntry> PageEntries(KeycapScope scope) =>
        KeymapTable.Entries.Where(e => !e.OverlayOnly && KeycapScopeMap.ScopeOf(e) == scope).ToList();

    // ---- 页码指示（圆圈数字，可点击）----

    private void RebuildIndicators()
    {
        foreach (var child in _indicatorRow.GetChildren())
        {
            child.QueueFree();
        }
        for (int i = 0; i < PageOrder.Length; i++)
        {
            var button = new Button
            {
                Text = (i + 1).ToString(),
                CustomMinimumSize = new Vector2(22, 22),
                FocusMode = FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 11);
            bool selected = i == _page;
            button.AddThemeStyleboxOverride("normal", CircleBox(selected));
            button.AddThemeStyleboxOverride("hover", CircleBox(true));
            button.AddThemeStyleboxOverride("pressed", CircleBox(true));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.AddThemeColorOverride("font_color", selected ? Colors.White : Palette.Active.TextDim);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.AddThemeColorOverride("font_pressed_color", Colors.White);
            var page = i;
            button.Pressed += () => SetPage(page);
            _indicatorRow.AddChild(button);
        }
        var name = new Label { Text = KeycapScopeMap.DisplayName(PageOrder[_page]) + "层" };
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeColorOverride("font_color", Palette.Active.Accent);
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _indicatorRow.AddChild(name);
    }

    /// <summary>圆圈样式：选中/激活=强调色实心，未选=描边空心。</summary>
    private static StyleBoxFlat CircleBox(bool filled)
    {
        var box = new StyleBoxFlat
        {
            CornerRadiusTopLeft = 11,
            CornerRadiusTopRight = 11,
            CornerRadiusBottomLeft = 11,
            CornerRadiusBottomRight = 11,
        };
        if (filled)
        {
            box.BgColor = Palette.Active.Accent;
        }
        else
        {
            box.BgColor = new Color(0, 0, 0, 0);
            box.BorderColor = Palette.Active.PanelBorder;
            box.BorderWidthBottom = 1;
            box.BorderWidthTop = 1;
            box.BorderWidthLeft = 1;
            box.BorderWidthRight = 1;
        }
        return box;
    }

    // ---- 键盘模拟区：仅当前层的键，尺寸统一，注记在上 ----

    private void RebuildKeyboard(KeycapScope scope)
    {
        foreach (var child in _keyboardHost.GetChildren())
        {
            child.QueueFree();
        }
        // 宿主自身定尺寸：普通 Control 不传播子节点最小尺寸，塌缩会让说明网格叠上键盘
        var (minOffset, maxEnd) = KeyboardLayout.Extents;
        _keyboardHost.CustomMinimumSize = new Vector2(
            (float)((maxEnd - minOffset) * Unit) + KeyboardPad * 2,
            KeyboardLayout.TotalRows * Unit + KeyboardPad * 2);

        // 物理键 → 注记：声明序首个登记（同页同键去重）
        var keys = new Dictionary<string, string>();
        foreach (var entry in PageEntries(scope))
        {
            foreach (var group in KeycapParser.Parse(entry.Keys))
            {
                foreach (var cap in group.Caps)
                {
                    if (KeyboardLayout.Find(cap) != null)
                    {
                        keys.TryAdd(cap, entry.Short ?? "");
                    }
                }
            }
        }

        void Place(KeyboardLayout.PhysicalKey key, int row)
        {
            if (!keys.TryGetValue(key.Name, out var note))
            {
                return; // 非本层的键不渲染
            }
            var cap = new KeyCap(key.Name, scope, mini: false, sub: note);
            cap.Position = new Vector2((float)((key.OffsetUnits - minOffset) * Unit) + KeyboardPad, row * Unit + KeyboardPad);
            cap.Size = new Vector2((float)(key.WidthUnits * Unit) - KeyGap, KeyHeight);
            _keyboardHost.AddChild(cap);
        }

        for (int row = 0; row < KeyboardLayout.LetterRows.Count; row++)
        {
            foreach (var key in KeyboardLayout.LetterRows[row])
            {
                Place(key, row);
            }
        }
        Place(KeyboardLayout.Space, 3);
        Place(KeyboardLayout.ArrowUp, 2);
        foreach (var key in KeyboardLayout.ArrowRow)
        {
            Place(key, 3);
        }
    }

    // ---- 底部说明：当前层的完整说明（统一两列）----

    private void RebuildDescriptions(KeycapScope scope)
    {
        foreach (var child in _descHost.GetChildren())
        {
            child.QueueFree();
        }
        var entries = PageEntries(scope);
        int rows = Mathf.CeilToInt(entries.Count / (float)DescColumns);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < DescColumns; col++)
            {
                int index = col * rows + row; // 列主序：同类相邻竖排
                if (index < entries.Count)
                {
                    _descHost.AddChild(BuildDescItem(entries[index], KeycapScopeMap.ScopeOf(entries[index])));
                }
                else
                {
                    _descHost.AddChild(new Control());
                }
            }
        }
    }

    private static Control BuildDescItem(KeymapEntry entry, KeycapScope scope)
    {
        var item = new HBoxContainer();
        item.AddThemeConstantOverride("separation", 4);
        item.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var dot = MakeSwatch(scope);
        dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        item.AddChild(dot);

        foreach (var group in KeycapParser.Parse(entry.Keys))
        {
            if (group.Caps.Length <= 3)
            {
                foreach (var cap in group.Caps)
                {
                    item.AddChild(new KeyCap(cap, scope, mini: true));
                }
            }
            else
            {
                // 大组合（Shift+WASD / Shift+方向键）合并键帽，避免撑破列宽
                item.AddChild(new KeyCap(group.Modifier, scope, mini: true));
                item.AddChild(new KeyCap(string.Join("", group.Keys), scope, mini: true));
            }
        }

        var desc = new Label
        {
            Text = entry.Description,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        desc.AddThemeFontSizeOverride("font_size", 10);
        desc.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        item.AddChild(desc);
        return item;
    }

    /// <summary>作用域色板（图例/说明行小点共用）。</summary>
    private static Control MakeSwatch(KeycapScope scope)
    {
        var swatch = new Panel { CustomMinimumSize = new Vector2(9, 9) };
        var face = KeyCap.FaceOf(scope);
        var box = new StyleBoxFlat
        {
            BgColor = face,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        };
        if (KeyCap.LuminanceOf(face) > 0.6f) // 浅色帽体描边定边（对局层与页面同色，无描边则图例不可见）
        {
            box.BorderColor = KeyCap.TextOnOf(face);
            box.BorderWidthBottom = 1;
            box.BorderWidthTop = 1;
            box.BorderWidthLeft = 1;
            box.BorderWidthRight = 1;
        }
        swatch.AddThemeStyleboxOverride("panel", box);
        return swatch;
    }

    /// <summary>作用域图例行（宿主屏标题行右对齐嵌入）。</summary>
    public static Control BuildLegend()
    {
        var legend = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        legend.AddThemeConstantOverride("separation", 8);
        foreach (var scope in PageOrder)
        {
            var item = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            item.AddThemeConstantOverride("separation", 3);
            item.AddChild(MakeSwatch(scope));
            var label = new Label { Text = KeycapScopeMap.DisplayName(scope) };
            label.AddThemeFontSizeOverride("font_size", 9);
            label.AddThemeColorOverride("font_color", Palette.Active.TextDim);
            item.AddChild(label);
            legend.AddChild(item);
        }
        return legend;
    }
}
