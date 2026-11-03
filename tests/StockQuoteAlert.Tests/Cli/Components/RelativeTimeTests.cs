using Bunit;
using Microsoft.AspNetCore.Components;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class RelativeTimeTests : DashboardTestContext
{
    [Fact]
    public void ShowsHowLongAgoTheValueWas()
    {
        Time.Advance(TimeSpan.FromSeconds(12));

        var component = Render<RelativeTime>(parameters => parameters.Add(p => p.Value, TestData.Now));

        Assert.Equal("há 12 s", component.Markup.Trim());
    }

    [Fact]
    public void UpdatesEverySecondOnItsOwn()
    {
        var component = Render<RelativeTime>(parameters => parameters.Add(p => p.Value, TestData.Now));

        Time.Advance(TimeSpan.FromSeconds(1));
        component.WaitForAssertion(() => Assert.Equal("há 1 s", component.Markup.Trim()));

        Time.Advance(TimeSpan.FromSeconds(1));
        component.WaitForAssertion(() => Assert.Equal("há 2 s", component.Markup.Trim()));
    }

    [Fact]
    public void NewValue_IsMeasuredFromNow()
    {
        Time.Advance(TimeSpan.FromSeconds(40));
        var component = Render<RelativeTime>(parameters => parameters.Add(p => p.Value, TestData.Now));

        component.Render(parameters => parameters.Add(p => p.Value, TestData.Now.AddSeconds(35)));

        Assert.Equal("há 5 s", component.Markup.Trim());
    }

    [Fact]
    public void WithoutAnInteractiveCircuit_DoesNotStartATimer()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var component = Render<RelativeTime>(parameters => parameters.Add(p => p.Value, TestData.Now));
        var renders = component.RenderCount;

        Time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(renders, component.RenderCount);
        Assert.Equal("há 0 s", component.Markup.Trim());
    }

    [Fact]
    public async Task Dispose_StopsTheTimer()
    {
        var component = Render<RelativeTime>(parameters => parameters.Add(p => p.Value, TestData.Now));
        var renders = component.RenderCount;

        await DisposeComponentsAsync();
        Time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(renders, component.RenderCount);
    }
}