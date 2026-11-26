using Godot;
using Nmoku.Core;
using Nmoku.Core.Saves;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>
/// 存档菜单（叠加层，非状态机状态；Title/Paused 均可打开）：档案管理面板——只管已有存档的进出，
/// 不承担保存当前对局（写局走 S/Ctrl+S，底部键位提示行补可发现性；批9 语义定稿 2026-09-26）。
/// 剪贴板=显式中转站：行内「复制」槽→剪贴板，「填写/覆盖」剪贴板→该槽（空槽叫填写、占用叫覆盖，
/// 占用确认在 store 层）；文件路径：头部「导入」文件→统一落槽（首空槽，全满覆盖槽 5 并确认），
/// 空槽行「导入」文件→该槽。快存槽（第 6 槽）永不被统一导入覆盖。
/// </summary>
public sealed partial class SaveMenu : Control
{
    public event Action<int>? LoadSlotRequested;       // 读取
    public event Action<int>? ReplaySlotRequested;     // 复盘
    public event Action<int>? FillSlotRequested;       // 填写/覆盖：剪贴板→该槽
    public event Action<int>? ExportSlotRequested;     // 复制：槽→剪贴板
    public event Action<int>? ExportFileRequested;     // 导出：槽→.nzsave 文件
    public event Action<int>? ImportFileSlotRequested; // 导入（空槽行）：文件→该槽
    public event Action? ImportFileRequested;          // 导入（头部）：文件→统一落槽
    public event Action<int>? DeleteSlotRequested;
    public event Action? CloseRequested;

    private VBoxContainer _rows = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // dim 背板由 SceneRouter 提供，此处为内容层

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 16,
            ContentMarginBottom = 16,
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        var title = new Label { Text = I18n.T(Text.Save.ManageTitle), SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        header.AddChild(MakeButton(I18n.T(Text.Save.Import), () => ImportFileRequested?.Invoke()));
        header.AddChild(MakeButton(I18n.T(Text.Overlay.Cancel), () => CloseRequested?.Invoke()));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        box.AddChild(_rows);

        var hint = new Label
        {
            Text = I18n.T(Text.Save.MenuKeyHint),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.Expand,
        };
        hint.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        box.AddChild(hint);
    }

    /// <summary>重建 6 行槽位（SaveUpdated 事件或打开时调用）。</summary>
    public void RefreshSlots(IReadOnlyList<SaveData?> slots)
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            _rows.AddChild(BuildRow(i, slots.Count > i ? slots[i] : null));
        }
    }

    private Control BuildRow(int index, SaveData? data)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        string label = index == GameConfig.QuickSaveIndex
            ? I18n.T(Text.Save.QuickSlotLabel, index + 1)
            : I18n.T(Text.Save.SlotLabel, index + 1);
        if (data != null)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(data.Timestamp).LocalDateTime;
            label += " — " + I18n.T(Text.Save.SlotInfo, data.BoardSize, data.BoardSize, data.Layers,
                data.Moves.Count,
                (data.Status == "finished" ? I18n.T(Text.Save.SlotFinished) : I18n.T(Text.Save.SlotPlaying)) + " · " + time.ToString("MM-dd HH:mm"));
        }
        else
        {
            label += " — " + I18n.T(Text.Save.SlotEmpty);
        }

        var nameLabel = new Label
        {
            Text = label,
            SizeFlagsHorizontal = SizeFlags.Expand,
            SizeFlagsStretchRatio = 2.2f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeColorOverride("font_color", data != null ? Palette.Active.TextPrimary : Palette.Active.TextDim);
        row.AddChild(nameLabel);

        if (data != null)
        {
            row.AddChild(MakeButton(I18n.T(Text.Save.Read), () => LoadSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Replay.Label), () => ReplaySlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Save.Overwrite), () => FillSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Save.Copy), () => ExportSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Save.Export), () => ExportFileRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Save.Delete), () => DeleteSlotRequested?.Invoke(index)));
        }
        else
        {
            row.AddChild(MakeButton(I18n.T(Text.Save.Fill), () => FillSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(I18n.T(Text.Save.Import), () => ImportFileSlotRequested?.Invoke(index)));
        }
        return row;
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
