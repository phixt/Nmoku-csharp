using Nmoku.Core.Saves;

namespace Nmoku.Core.Tests;

/// <summary>04-contracts §5.2：同步送达、按订阅顺序、退订未订阅为 no-op。</summary>
public class EventBusTests : IDisposable
{
    public void Dispose() => EventBus.ClearAll();

    [Fact]
    public void Subscribe_HandlerReceivesPayload()
    {
        bool? received = null;
        EventBus.ThemeToggled += v => received = v;

        EventBus.EmitThemeToggled(true);

        Assert.Equal(true, received);
    }

    [Fact]
    public void Subscribe_HandlersFireInSubscriptionOrder()
    {
        var order = new List<string>();
        EventBus.LayerChanged += _ => order.Add("first");
        EventBus.LayerChanged += _ => order.Add("second");

        EventBus.EmitLayerChanged(3);

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery_AndUnknownHandlerIsNoOp()
    {
        int hits = 0;
        Action handler = () => hits++;
        EventBus.BoardUpdated += handler;
        EventBus.BoardUpdated -= () => { }; // 从未订阅的回调：no-op，不得抛

        EventBus.EmitBoardUpdated();
        EventBus.BoardUpdated -= handler;
        EventBus.EmitBoardUpdated();

        Assert.Equal(1, hits);
    }

    [Fact]
    public void Emit_WithNoSubscribers_DoesNotThrow()
    {
        EventBus.EmitGameReset();
        EventBus.EmitAiMoveRequest();
        EventBus.EmitColorblindModeToggled(true);
        EventBus.EmitPlaySound("click");
        EventBus.EmitSaveUpdated(new SaveData?[6]);
    }
}
