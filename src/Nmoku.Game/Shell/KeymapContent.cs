using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键页共用内容（对齐源 guide 视觉，紧凑单屏版）：①键盘模拟区（坐标由 Core KeyboardLayout
/// 正式排布数据生成，只渲染有功能的键）②分类说明（三列网格，键帽小样化，无滚动条——
/// 人类要求 2026-09-25：滚动条不该出现，整页一屏放下）。
/// 由 F1 帮助浮层（KeymapScreen）与引导屏（GuideScreen，标题「快捷键说明」/暂停 G 入口）共用。
/// 内容完全由 Core KeymapTable 派生（KeycapScopeMap / KeycapParser），与 ShortcutRouter 门控同源。
/// </summary>
public sealed partial class KeymapContent : VBoxContainer
{
    private const float Unit = 28f;        // 键帽单元（含间隙）
    private const float KeyHeight = 22f;
    private const float KeyGap = 3f;
    private const float KeyboardPad = 5f;
    private const float KeyboardUnits = 10f; // 最宽排（QWERTYUIOP）单元数
    private const int DescColumns = 3;

    /// <summary>同键跨层例外的底部小字（少量分层显示，人类拍板 2026-09-25；须适配键帽宽度）。</summary>
    private static readonly Dictionary<string, string> SubHints = new()
    {
        ["Esc"] = "继续",
        ["R"] = "重开",
        ["S"] = "Ctrl+S",
    };

    public KeymapContent()
    {
        AddThemeConstantOverride("separation", 6);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(BuildKeyboard());
        AddChild(BuildDescriptionGrid());
    }

    // ---- ① 键盘模拟区：坐标全部来自 KeyboardLayout 数据，只渲染有功能的键 ----

    private Control BuildKeyboard()
    {
        var scopeMap = BuildPhysicalScopeMap();
        var kb = new Control
        {
            CustomMinimumSize = new Vector2(KeyboardUnits * Unit + KeyboardPad * 2, KeyboardLayout.TotalRows * Unit + KeyboardPad * 2),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };

        void Place(KeyboardLayout.PhysicalKey key, int row)
        {
            if (!scopeMap.TryGetValue(key.Name, out var scope))
            {
                return; // 无功能的物理键不渲染（对齐源 keyboard-sim）
            }
            var cap = new KeyCap(key.Name, scope, mini: false, sub: SubHints.GetValueOrDefault(key.Name));
            cap.Position = new Vector2((float)(key.OffsetUnits * Unit) + KeyboardPad, row * Unit + KeyboardPad);
            cap.Size = new Vector2((float)(key.WidthUnits * Unit) - KeyGap, KeyHeight);
            kb.AddChild(cap);
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
        return kb;
    }

    /// <summary>物理键 → 作用域：两遍法——素键（无修饰）先登记，组合键（Ctrl+S 等）只补空位。
    /// 否则先声明的组合键会抢占素键的层（S/Z/方向键实测被 Ctrl/Shift 组合抢走）。</summary>
    private static Dictionary<string, KeycapScope> BuildPhysicalScopeMap()
    {
        var map = new Dictionary<string, KeycapScope>();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var entry in KeymapTable.Entries)
            {
                var scope = KeycapScopeMap.ScopeOf(entry);
                foreach (var group in KeycapParser.Parse(entry.Keys))
                {
                    bool compound = group.Caps.Length > 1;
                    if ((pass == 0) == compound)
                    {
                        continue; // 第一遍只收素键，第二遍只收组合键
                    }
                    foreach (var cap in group.Caps)
                    {
                        if (KeyboardLayout.Find(cap) != null)
                        {
                            map.TryAdd(cap, scope);
                        }
                    }
                }
            }
        }
        return map;
    }

    /// <summary>作用域图例行（六层单色，宿主屏标题行右对齐嵌入）。</summary>
    public static Control BuildLegend()
    {
        var legend = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        legend.AddThemeConstantOverride("separation", 8);
        foreach (var scope in Enum.GetValues<KeycapScope>())
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

    // ---- ② 分类说明：三列网格（列主序铺放），键帽小样 + 作用域色点 + 描述 ----

    private Control BuildDescriptionGrid()
    {
        var entries = KeymapTable.Entries
            .Select(e => (Entry: e, Scope: KeycapScopeMap.ScopeOf(e), Caps: KeycapParser.Parse(e.Keys)))
            .ToList();
        int rows = Mathf.CeilToInt(entries.Count / (float)DescColumns);
        var grid = new GridContainer { Columns = DescColumns };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 2);
        // 列主序铺放（竖读同类相邻：全局→标题→引导→对局…按声明序竖排）
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < DescColumns; col++)
            {
                int index = col * rows + row;
                if (index < entries.Count)
                {
                    grid.AddChild(BuildDescItem(entries[index].Entry, entries[index].Scope, entries[index].Caps));
                }
                else
                {
                    grid.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.Expand });
                }
            }
        }
        return grid;
    }

    private static Control BuildDescItem(KeymapEntry entry, KeycapScope scope, List<KeycapParser.CapGroup> caps)
    {
        var item = new HBoxContainer();
        item.AddThemeConstantOverride("separation", 4);
        item.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var dot = MakeSwatch(scope);
        dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        item.AddChild(dot);

        foreach (var group in caps)
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
                // 大组合（Shift+WASD / Shift+方向键）合并键帽，避免撑破三列网格
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
        desc.AddThemeFontSizeOverride("font_size", 9);
        desc.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        item.AddChild(desc);
        return item;
    }

    /// <summary>作用域色板（图例圆点/说明行小点共用）。</summary>
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
}
