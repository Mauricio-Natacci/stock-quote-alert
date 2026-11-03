using Bunit;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class NotificationsPanelTests : DashboardTestContext
{
    [Fact]
    public void WithoutNotifications_ExplainsWhenAnEmailIsSent()
    {
        var panel = RenderPanel([]);

        Assert.Contains("Nenhum alerta disparado até agora.", panel.Markup, StringComparison.Ordinal);
        Assert.Equal("0", panel.Find(".count-pill").TextContent);
        Assert.Empty(panel.FindAll("table"));
    }

    [Fact]
    public void Rows_AreNewestFirstWithEveryDetail()
    {
        var sell = Notification(TestData.SellAlert(22.70m), NotificationStatus.Sent, 0);
        var buy = Notification(TestData.BuyAlert(22.50m), NotificationStatus.Failed, 60, "Connection refused");

        var panel = RenderPanel([sell, buy]);

        var rows = panel.FindAll("tbody tr");
        Assert.Equal("2", panel.Find(".count-pill").TextContent);
        Assert.Equal(2, rows.Count);
        var cells = rows[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["29/10/2026 14:31:00", "▼ Compra", "PETR4", "R$ 22,50", "R$ 22,59", "−0,40%", "dest@gmail.com", "✕ Falhou"], cells);
        Assert.Contains("▲ Venda", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("✓ Enviado", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_AreFocusableAndNamed()
    {
        var row = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0)]).Find("tbody tr");

        Assert.Equal("0", row.GetAttribute("tabindex"));
        Assert.Equal("Ver e-mail: [VENDA] PETR4 subiu", row.GetAttribute("aria-label"));
    }

    [Fact]
    public void ClickingARow_OpensTheFullEmail()
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(22.70m), NotificationStatus.Sent, 0)]);

        panel.Find("tbody tr").Click();

        var drawer = panel.Find("[role=dialog]");
        Assert.Equal("true", drawer.GetAttribute("aria-modal"));
        Assert.Equal("notification-detail-title", drawer.GetAttribute("aria-labelledby"));
        Assert.Equal("[VENDA] PETR4 subiu", drawer.QuerySelector(".email-preview__subject")!.TextContent);
        Assert.Equal("Olá,\n\nVender agora: https://homebroker.example.com/", drawer.QuerySelector(".email-preview__body")!.TextContent);
        Assert.Contains("R$ 22,70", drawer.TextContent, StringComparison.Ordinal);
        Assert.Contains("(+0,13% em relação à linha de R$ 22,67)", drawer.TextContent, StringComparison.Ordinal);
        Assert.Contains("E-mail", drawer.TextContent, StringComparison.Ordinal);
        Assert.Empty(drawer.QuerySelectorAll(".details__error"));
    }

    [Fact]
    public void FailedEmail_ShowsTheError()
    {
        var panel = RenderPanel([Notification(TestData.BuyAlert(), NotificationStatus.Failed, 0, "535 bad credentials")]);

        panel.Find("tbody tr").Click();

        Assert.Equal("535 bad credentials", panel.Find(".details__error").TextContent);
    }

    [Fact]
    public void OpeningTheDrawer_MovesFocusIntoIt()
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0)]);

        panel.Find("tbody tr").Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void EnterOrSpaceOnARow_OpensTheEmail(string key)
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0)]);

        panel.Find("tbody tr").KeyDown(key);

        Assert.Single(panel.FindAll("[role=dialog]"));
    }

    [Theory]
    [InlineData("Tab")]
    [InlineData("a")]
    [InlineData("Escape")]
    public void OtherKeysOnARow_DoNothing(string key)
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0)]);

        panel.Find("tbody tr").KeyDown(key);

        Assert.Empty(panel.FindAll("[role=dialog]"));
    }

    [Fact]
    public void CloseButton_ClosesTheDrawer()
    {
        var panel = OpenDrawer();

        panel.Find("button[aria-label='Fechar detalhes']").Click();

        Assert.Empty(panel.FindAll("[role=dialog]"));
    }

    [Fact]
    public void Escape_ClosesTheDrawerButOtherKeysDoNot()
    {
        var panel = OpenDrawer();

        panel.Find("[data-testid=drawer-backdrop]").KeyDown("Tab");
        Assert.Single(panel.FindAll("[role=dialog]"));

        panel.Find("[data-testid=drawer-backdrop]").KeyDown("Escape");
        Assert.Empty(panel.FindAll("[role=dialog]"));
    }

    [Fact]
    public void ClickingTheBackdrop_ClosesTheDrawer()
    {
        var panel = OpenDrawer();

        panel.Find("[data-testid=drawer-backdrop]").Click();

        Assert.Empty(panel.FindAll("[role=dialog]"));
    }

    [Fact]
    public void ClickingInsideTheDrawer_DoesNotReachTheBackdrop()
    {
        var panel = OpenDrawer();

        // In the browser, a click inside the drawer stops before the backdrop, which would close it.
        Assert.True(panel.Find("[role=dialog]").HasAttribute("blazor:onclick:stoppropagation"));
    }

    [Fact]
    public void OpeningAnotherRow_ShowsThatEmail()
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0), Notification(TestData.BuyAlert(), NotificationStatus.Sent, 30)]);

        panel.FindAll("tbody tr")[1].Click();

        Assert.Equal("[VENDA] PETR4 subiu", panel.Find(".email-preview__subject").TextContent);
    }

    private IRenderedComponent<NotificationsPanel> OpenDrawer()
    {
        var panel = RenderPanel([Notification(TestData.SellAlert(), NotificationStatus.Sent, 0)]);
        panel.Find("tbody tr").Click();
        return panel;
    }

    private IRenderedComponent<NotificationsPanel> RenderPanel(IReadOnlyList<NotificationRecord> notifications) =>
        Render<NotificationsPanel>(parameters => parameters.Add(p => p.Notifications, notifications));

    private static NotificationRecord Notification(PriceAlert alert, NotificationStatus status, int seconds, string? error = null) =>
        new(
            Guid.CreateVersion7(),
            alert,
            "E-mail",
            "dest@gmail.com",
            alert.Kind == AlertKind.Sell ? "[VENDA] PETR4 subiu" : "[COMPRA] PETR4 caiu",
            "Olá,\n\nVender agora: https://homebroker.example.com/",
            TestData.Now.AddSeconds(seconds),
            status,
            error);
}