using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键页共用内容（三段式，对齐源 guide 视觉）：①作用域图例（六层单色，不叠层）
/// ②键盘模拟区（坐标由 Core KeyboardLayout 正式排布数据生成，视图禁手摆位置）
/// ③分类说明列表（键帽小样化）。由 F1 帮助浮层（KeymapScreen）与引导屏（GuideScreen，
/// 标题「快捷键说明」/暂停 G 入口）共用——2026-09-25 修复"说明按钮打开的却是占位页"的入口错位。
/// 内容完全由 Core KeymapTable 派生（KeycapScopeMap / KeycapParser），与 ShortcutRouter 门控同源。
/// </summary>
public sealed partial class KeymapContent : VBoxContainer
{
    private const float Unit = 48f;        // 键帽单元（含间隙）
    private const float KeyHeight = 40f;
    private const float KeyGap = 6f;
    private const float KeyboardPad = 8f;
    private const float KeyboardUnits = 10f; // 最宽排（QWERTYUIOP）单元数

    /// <summary>同键跨层例外的底部小字（少量分层显示，人类拍板 2026-09-25；须适配键帽宽度）。</summary>
    private static readonly Dictionary<string, string> SubHints = new()
    {
        ["Esc"] = "继续",
        ["R"] = "重开",
        ["S"] = "Ctrl+S",
    };

    public KeymapContent()
    {
        AddThemeConstantOverride("separation", 10);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(BuildLegend());
        AddChild(BuildKeyboard());
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);
        scroll.AddChild(BuildDescriptionList());
    }

    // ---- ① 键盘模拟区：坐标全部来自 KeyboardLayout 数据 ----

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
            bool mapped = scopeMap.TryGetValue(key.Name, out var scope);
            var cap = new KeyCap(
                key.Name,
                mapped ? scope : KeycapScope.Global,
                idle: !mapped,
                mini: false,
                sub: SubHints.GetValueOrDefault(key.Name));
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
        Place(KeyboardLayout.ArrowUp, 3);
        foreach (var key in KeyboardLayout.ArrowRow)
        {
            Place(key, 4);
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

    // ---- ② 作用域图例 ----

    private Control BuildLegend()
    {
        var legend = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        legend.AddThemeConstantOverride("separation", 14);
        foreach (var scope in Enum.GetValues<KeycapScope>())
        {
            var item = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            item.AddThemeConstantOverride("separation", 5);
            item.AddChild(MakeSwatch(scope));
            var label = new Label { Text = KeycapScopeMap.DisplayName(scope) };
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", Palette.Active.TextDim);
            item.AddChild(label);
            legend.AddChild(item);
        }
        return legend;
    }

    /// <summary>作用域色板（图例圆点/说明行小点共用）。</summary>
    private static Control MakeSwatch(KeycapScope scope)
    {
        var swatch = new Panel { CustomMinimumSize = new Vector2(13, 13) };
        var face = KeyCap.FaceOf(scope);
        var box = new StyleBoxFlat
        {
            BgColor = face,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
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

    // ---- ③ 分类说明列表（键帽小样化）----

    private Control BuildDescriptionList()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        foreach (var (category, rows) in KeymapTable.ByCategory())
        {
            var head = new Label { Text = category };
            head.AddThemeFontSizeOverride("font_size", 18);
            head.AddThemeColorOverride("font_color", Palette.Active.Accent);
            box.AddChild(head);

            foreach (var row in rows)
            {
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", 10);

                var scope = KeycapScopeMap.ScopeOf(row);
                var dot = MakeSwatch(scope);
                dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                line.AddChild(dot);

                var capsBox = new HBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
                capsBox.AddThemeConstantOverride("separation", 4);
                capsBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                var groups = KeycapParser.Parse(row.Keys);
                for (int g = 0; g < groups.Count; g++)
                {
                    foreach (var cap in groups[g].Caps)
                    {
                        capsBox.AddChild(new KeyCap(cap, scope, mini: true));
                    }
                    if (g < groups.Count - 1)
                    {
                        var sep = new Label { Text = "/" };
                        sep.AddThemeColorOverride("font_color", Palette.Active.TextDim);
                        capsBox.AddChild(sep);
                    }
                }
                line.AddChild(capsBox);

                var desc = new Label
                {
                    Text = row.Description,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                desc.AddThemeColorOverride("font_color", Palette.Active.TextDim);
                line.AddChild(desc);
                box.AddChild(line);
            }
        }
        return box;
    }
}
