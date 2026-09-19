namespace Nmoku.Core;

/// <summary>
/// UI 层模态对话框与通知契约（04-contracts §4.1）。Game 逻辑只依赖此接口，
/// 不触碰具体 UI 框架（Core 侧零 Godot 类型）。
/// </summary>
public interface IOverlayManager
{
    /// <summary>模态确认框：onYes 仅在确认时恰好调用一次；取消/Esc 不调用。前置条件：无其他模态。</summary>
    void ShowConfirmDialog(string title, string message, Action onYes,
        Action? onCancel = null, string? yesText = null, string? noText = null);

    /// <summary>模态输入框：onSubmit 收到 trim 后的输入（空串仍提交，校验是调用方职责）；取消不调用。</summary>
    void ShowInputDialog(string title, string message, string placeholder, Action<string> onSubmit);

    /// <summary>非阻塞通知：可堆叠、自动消退、不抢焦点。isError 使用醒目样式。</summary>
    void ShowToast(string message, bool isError = false);

    /// <summary>关闭全部模态并丢弃挂起回调（不调用、不转移）。</summary>
    void CloseAll();

    /// <summary>当前是否有模态框打开（壳层据此暂停游戏快捷键）。</summary>
    bool IsModalOpen { get; }
}
