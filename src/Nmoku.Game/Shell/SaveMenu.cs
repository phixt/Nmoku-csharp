using Godot;
using Nmoku.Core;
using Nmoku.Core.Saves;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 存档菜单（叠加层，非状态机状态；Title/Paused 均可打开）：6 槽位列表 +
/// 载入/复盘/导出/删除/导入（剪贴板统一嗅探）/关闭。槽位语义见 SaveManager（5=快存）。
/// </summary>
public sealed partial class SaveMenu : Control
{
    public event Action<int>? LoadSlotRequested;
    public event Action<int>? ReplaySlotRequested;
    public event Action<int>? ExportSlotRequested;
    public event Action<int>? ExportFileRequested;
    public event Action<int>? DeleteSlotRequested;
    public event Action? ImportRequested;
    public event Action? ImportFileRequested;
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
        var title = new Label { Text = Text.Save.ManageTitle, SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        header.AddChild(MakeButton(Text.Save.Import, () => ImportRequested?.Invoke()));
        header.AddChild(MakeButton(Text.Save.ImportFile, () => ImportFileRequested?.Invoke()));
        header.AddChild(MakeButton(Text.Overlay.Cancel, () => CloseRequested?.Invoke()));

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        box.AddChild(_rows);
    }

    /// <summary>重建 6 行槽位（SaveUpdated 事件或打开时调用）。</summary>
    public void RefreshSlots(IReadOnlyList<SaveData?> slots)
    {
        foreach (var child in _rows.GetChildren())
        {
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
            ? string.Format(Text.Save.QuickSlotLabel, index + 1)
            : string.Format(Text.Save.SlotLabel, index + 1);
        if (data != null)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(data.Timestamp).LocalDateTime;
            label += " — " + string.Format(Text.Save.SlotInfo, data.BoardSize, data.BoardSize, data.Layers,
                data.Moves.Count,
                (data.Status == "finished" ? Text.Save.SlotFinished : Text.Save.SlotPlaying) + " · " + time.ToString("MM-dd HH:mm"));
        }
        else
        {
            label += " — " + Text.Save.SlotEmpty;
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
            row.AddChild(MakeButton(Text.Title.LoadGame, () => LoadSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(Text.Replay.Label, () => ReplaySlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(Text.Save.Export, () => ExportSlotRequested?.Invoke(index)));
            row.AddChild(MakeButton(Text.Save.ExportFile, () => ExportFileRequested?.Invoke(index)));
            row.AddChild(MakeButton(Text.Save.Delete, () => DeleteSlotRequested?.Invoke(index)));
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
