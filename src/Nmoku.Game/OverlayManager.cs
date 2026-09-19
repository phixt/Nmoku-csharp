using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game;

/// <summary>
/// 模态对话框与通知的唯一出口（04-contracts §4.1 IOverlayManager 的 Godot 实现）。
/// 契约要点：模态互斥（第二个弹窗调用直接拒绝并报错）、Esc=取消、确认/提交回调恰好一次、
/// 取消路径不触发 onYes、toast 不抢焦点可堆叠自动消退、CloseAll 丢弃挂起回调。
/// 扩展（源项目需要）：ShowConfirmDialog 增加可选 onCancel 与按钮文案——SWAP2 的
/// "取消=继续执白" 也是有效决策，非单纯放弃。
/// </summary>
public sealed partial class OverlayManager : CanvasLayer, IOverlayManager
{
    private const double ToastDurationSeconds = 2.5;
    private const int ToastMax = 5;

    private ConfirmationDialog _confirm = null!;
    private ConfirmationDialog _inputDialog = null!;
    private Label _inputMessage = null!;
    private LineEdit _inputField = null!;
    private VBoxContainer _toastHost = null!;

    private Action? _onConfirm;
    private Action? _onCancel;
    private Action<string>? _onSubmit;

    /// <summary>有模态框打开时为真（壳层据此暂停游戏快捷键）。</summary>
    public bool IsModalOpen => _confirm.Visible || _inputDialog.Visible;

    public override void _Ready()
    {
        Layer = 30; // 对话框高于路由三层（基座0/模态10/浮层20）

        _confirm = new ConfirmationDialog
        {
            Exclusive = true,
            OkButtonText = Text.Overlay.Confirm,
            CancelButtonText = Text.Overlay.Cancel,
            Unresizable = true,
            MinSize = new Vector2I(360, 120),
        };
        _confirm.Confirmed += OnConfirmAccepted;
        _confirm.Canceled += OnConfirmDismissed;
        AddChild(_confirm);

        _inputDialog = new ConfirmationDialog
        {
            Exclusive = true,
            OkButtonText = Text.Overlay.Confirm,
            CancelButtonText = Text.Overlay.Cancel,
            Unresizable = true,
            MinSize = new Vector2I(360, 140),
        };
        var box = new VBoxContainer();
        _inputMessage = new Label();
        _inputField = new LineEdit();
        _inputDialog.Confirmed += OnSubmitAccepted;
        _inputDialog.Canceled += OnSubmitDismissed;
        _inputField.TextSubmitted += _ =>
        {
            if (_onSubmit != null)
            {
                SubmitInput();
            }
        };
        _inputDialog.AboutToPopup += () => _inputField.GrabFocus();
        box.AddChild(_inputMessage);
        box.AddChild(_inputField);
        _inputDialog.AddChild(box);
        AddChild(_inputDialog);

        _toastHost = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _toastHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toastHost.GrowHorizontal = Control.GrowDirection.Both;
        _toastHost.OffsetTop = 48;
        _toastHost.AddThemeConstantOverride("separation", 8);
        AddChild(_toastHost);
    }

    // ---- 确认框 ----

    public void ShowConfirmDialog(string title, string message, Action onYes,
        Action? onCancel = null, string? yesText = null, string? noText = null)
    {
        if (IsModalOpen)
        {
            GD.PrintErr("OverlayManager: 已有模态框打开，ShowConfirmDialog 被拒绝（§4.1.1 队列或拒绝语义，取拒绝）");
            return;
        }
        _onConfirm = onYes;
        _onCancel = onCancel;
        _confirm.Title = title;
        _confirm.DialogText = message;
        _confirm.OkButtonText = yesText ?? Text.Overlay.Confirm;
        _confirm.CancelButtonText = noText ?? Text.Overlay.Cancel;
        _confirm.PopupCentered();
    }

    private void OnConfirmAccepted()
    {
        var callback = _onConfirm;
        CloseConfirm();
        callback?.Invoke(); // 回调恰好一次；关闭动作先行，避免回调内再开窗被互斥拒绝
    }

    private void OnConfirmDismissed()
    {
        var callback = _onCancel;
        CloseConfirm();
        callback?.Invoke();
    }

    private void CloseConfirm()
    {
        _onConfirm = null;
        _onCancel = null;
        _confirm.Hide();
    }

    // ---- 输入框 ----

    public void ShowInputDialog(string title, string message, string placeholder, Action<string> onSubmit)
    {
        if (IsModalOpen)
        {
            GD.PrintErr("OverlayManager: 已有模态框打开，ShowInputDialog 被拒绝");
            return;
        }
        _onSubmit = onSubmit;
        _inputDialog.Title = title;
        _inputMessage.Text = message;
        _inputField.PlaceholderText = placeholder;
        _inputField.Text = "";
        _inputDialog.PopupCentered();
    }

    private void SubmitInput()
    {
        var callback = _onSubmit;
        CloseInput();
        // §4.1.2：回调收到 trim 后的原串，空串仍提交（校验是调用方职责）
        callback?.Invoke(_inputField.Text.Trim());
    }

    private void OnSubmitAccepted() => SubmitInput();

    private void OnSubmitDismissed() => CloseInput();

    private void CloseInput()
    {
        _onSubmit = null;
        _inputDialog.Hide();
    }

    // ---- Toast ----

    public void ShowToast(string message, bool isError = false)
    {
        var toast = BuildToast(message, isError);
        _toastHost.AddChild(toast);
        while (_toastHost.GetChildCount() > ToastMax)
        {
            _toastHost.GetChild(0).QueueFree();
        }
        // 延迟消失；toast 不抢焦点（MouseFilter Ignore），到期自清
        CreateTimer().Timeout += () =>
        {
            if (IsInstanceValid(toast))
            {
                toast.QueueFree();
            }
        };
    }

    private SceneTreeTimer CreateTimer() =>
        GetTree().CreateTimer(ToastDurationSeconds, processAlways: false, processInPhysics: false, ignoreTimeScale: false);

    private Control BuildToast(string message, bool isError)
    {
        var box = new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = isError ? Palette.Active.Error : Palette.Active.Accent,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        box.SetBorderWidthAll(1);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", box);
        var label = new Label
        {
            Text = message,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", isError ? Palette.Active.Error : Palette.Active.TextPrimary);
        panel.AddChild(label);
        return panel;
    }

    // ---- CloseAll ----

    /// <summary>关闭全部模态并丢弃挂起回调（§4.1.4：不调用、不转移）。</summary>
    public void CloseAll()
    {
        CloseConfirm();
        CloseInput();
    }
}
