namespace Nmoku.Core.Tests;

public class UiScaleMathTests
{
    [Theory]
    [InlineData(540, 1.0f, 1.0f)]
    [InlineData(720, 1.0f, 720f / 540f)]
    [InlineData(1080, 1.0f, 2.0f)]
    [InlineData(1440, 1.0f, 1440f / 540f)]
    [InlineData(2000, 1.0f, 2000f / 540f)]
    [InlineData(2160, 1.0f, 4.0f)]
    public void ComputeContentScaleFactor_StandardHeightsAt1x_Preserves540LogicalHeight(
        int heightPx, float mult, float expectedCsf)
    {
        float csf = UiScaleMath.ComputeContentScaleFactor(heightPx, mult);
        Assert.Equal(expectedCsf, csf, precision: 4);
        var (_, lh) = UiScaleMath.ComputeLogicalSize(heightPx * 16 / 9, heightPx, csf);
        Assert.Equal(540f, lh, precision: 2);
    }

    [Fact]
    public void ComputeContentScaleFactor_Multipliers_ScaleProportionally()
    {
        Assert.Equal(1.5f, UiScaleMath.ComputeContentScaleFactor(1080, 0.75f), precision: 4);
        Assert.Equal(2.0f, UiScaleMath.ComputeContentScaleFactor(1080, 1.0f), precision: 4);
        Assert.Equal(2.5f, UiScaleMath.ComputeContentScaleFactor(1080, 1.25f), precision: 4);
        Assert.Equal(3.0f, UiScaleMath.ComputeContentScaleFactor(1080, 1.5f), precision: 4);
    }

    [Fact]
    public void ComputeContentScaleFactor_ClampsExtremesAndInvalidInputs()
    {
        // 360px 窗口 1x: 360/540=0.667 → 钳到 MinContentScaleFactor (0.75)
        Assert.Equal(UiScaleMath.MinContentScaleFactor, UiScaleMath.ComputeContentScaleFactor(360, 1.0f));
        // 超高分屏 + 1.5x → 钳到 MaxContentScaleFactor (6.0)
        Assert.Equal(UiScaleMath.MaxContentScaleFactor, UiScaleMath.ComputeContentScaleFactor(4320, 1.5f));
        // 0/负高度回落 1080p 基准
        Assert.Equal(2.0f, UiScaleMath.ComputeContentScaleFactor(0, 1.0f));
        Assert.Equal(2.0f, UiScaleMath.ComputeContentScaleFactor(-100, float.NaN));
    }

    [Fact]
    public void TryParseResolutionKey_ValidAndInvalidCases()
    {
        Assert.Equal((1920, 1080), UiScaleMath.TryParseResolutionKey("1920x1080"));
        Assert.Equal((960, 540), UiScaleMath.TryParseResolutionKey(" 960X540 "));
        Assert.Null(UiScaleMath.TryParseResolutionKey(null));
        Assert.Null(UiScaleMath.TryParseResolutionKey(""));
        Assert.Null(UiScaleMath.TryParseResolutionKey("320x200")); // 低于最小窗口
        Assert.Null(UiScaleMath.TryParseResolutionKey("abc"));
    }
}
