using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class AlertEmailComposerTests
{
    private readonly AlertEmailComposer _composer = new(Options.Create(TestData.EmailOptions));

    [Fact]
    public void Compose_SellAlert_WritesSellSubject()
    {
        var email = _composer.Compose(TestData.SellAlert(22.70m));

        Assert.Equal("[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda", email.Subject);
    }

    [Fact]
    public void Compose_BuyAlert_WritesBuySubject()
    {
        var email = _composer.Compose(TestData.BuyAlert(22.50m));

        Assert.Equal("[COMPRA] PETR4 caiu para R$ 22,50, 0,40% abaixo da referência de compra", email.Subject);
    }

    [Fact]
    public void Compose_SellAlert_WritesFullTextBody()
    {
        var email = _composer.Compose(TestData.SellAlert(22.70m));

        Assert.Equal(
            """
            Olá,

            PETR4 superou o seu preço de referência para venda.

              Cotação atual:          R$ 22,70
              Referência de venda:    R$ 22,67
              Diferença:              R$ 0,03 (0,13% acima)
              Cotado em:              29/10/2026 14:30 (horário de Brasília)

            Recomendação: VENDA. O ativo está sendo negociado acima do preço-alvo que você definiu; pode ser um bom momento para realizar o lucro.

            Vender agora: https://homebroker.example.com/negociar?ativo=PETR4&operacao=venda&preco=22.70

            Para evitar e-mails repetidos, um novo alerta só será enviado quando a cotação voltar a ultrapassar uma das referências configuradas: subir acima da referência de venda ou cair abaixo da referência de compra.

            --
            Stock Quote Alert
            Alerta automático gerado a partir das referências configuradas por você. Não constitui recomendação de investimento.
            """,
            email.TextBody,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Compose_BuyAlert_WritesFullTextBody()
    {
        var email = _composer.Compose(TestData.BuyAlert(22.50m));

        Assert.Equal(
            """
            Olá,

            PETR4 caiu abaixo do seu preço de referência para compra.

              Cotação atual:          R$ 22,50
              Referência de compra:   R$ 22,59
              Diferença:              R$ 0,09 (0,40% abaixo)
              Cotado em:              29/10/2026 14:30 (horário de Brasília)

            Recomendação: COMPRA. O ativo está sendo negociado abaixo do preço-alvo que você definiu; pode ser uma boa oportunidade de entrada.

            Comprar agora: https://homebroker.example.com/negociar?ativo=PETR4&operacao=compra&preco=22.50

            Para evitar e-mails repetidos, um novo alerta só será enviado quando a cotação voltar a ultrapassar uma das referências configuradas: subir acima da referência de venda ou cair abaixo da referência de compra.

            --
            Stock Quote Alert
            Alerta automático gerado a partir das referências configuradas por você. Não constitui recomendação de investimento.
            """,
            email.TextBody,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Compose_UsesTheAlertTicker()
    {
        var alert = new PriceAlert(Ticker.Create("BOVA11"), AlertKind.Sell, 131m, 130m, TestData.Now);

        var email = _composer.Compose(alert);

        Assert.StartsWith("[VENDA] BOVA11 subiu para R$ 131,00", email.Subject, StringComparison.Ordinal);
        Assert.Contains("BOVA11 superou o seu preço de referência para venda.", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_FormatsLargePricesWithThousandsSeparator()
    {
        var alert = new PriceAlert(TestData.Petr4, AlertKind.Sell, 1500m, 1234.5m, TestData.Now);

        var email = _composer.Compose(alert);

        Assert.Contains("R$ 1.500,00", email.Subject, StringComparison.Ordinal);
        Assert.Contains("R$ 1.234,50", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("R$ 265,50 (21,51% acima)", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ShowsQuoteTimeInBrasilia()
    {
        var justAfterUtcMidnight = new DateTimeOffset(2026, 10, 30, 1, 15, 0, TimeSpan.Zero);
        var alert = TestData.SellAlert() with { QuotedAt = justAfterUtcMidnight };

        var email = _composer.Compose(alert);

        Assert.Contains("29/10/2026 22:15 (horário de Brasília)", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_WhenPriceIsFarFromReference_RoundsPercentToTwoDecimals()
    {
        var email = _composer.Compose(TestData.BuyAlert(15m));

        Assert.Contains("R$ 7,59 (33,60% abaixo)", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_UnknownKind_Throws()
    {
        var alert = TestData.SellAlert() with { Kind = (AlertKind)42 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _composer.Compose(alert));

        Assert.Equal("kind", exception.ParamName);
    }

    [Fact]
    public void Compose_HtmlBody_IsAPortugueseDocumentTitledByTheAlert()
    {
        var html = _composer.Compose(TestData.SellAlert(22.70m)).HtmlBody;

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"pt-BR\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Alerta de venda: PETR4</title>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>", html.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_HtmlBody_ShowsTheSameFactsAsTheText()
    {
        var html = _composer.Compose(TestData.SellAlert(22.70m)).HtmlBody;

        Assert.Contains("PETR4 · R$ 22,70", html, StringComparison.Ordinal);
        Assert.Contains("Olá,<br>PETR4 superou o seu preço de referência para venda.", html, StringComparison.Ordinal);
        Assert.Contains(">R$ 22,67</td>", html, StringComparison.Ordinal);
        Assert.Contains(">R$ 0,03 (0,13% acima)</td>", html, StringComparison.Ordinal);
        Assert.Contains("29/10/2026 14:30 <span", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Recomendação: VENDA.</strong> O ativo está sendo negociado acima", html, StringComparison.Ordinal);
        Assert.Contains("Para evitar e-mails repetidos", html, StringComparison.Ordinal);
        Assert.Contains("Não constitui recomendação de investimento.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_HtmlBody_HasAHiddenPreviewLine()
    {
        var html = _composer.Compose(TestData.BuyAlert(22.50m)).HtmlBody;

        Assert.Contains("<span style=\"display:none;max-height:0;overflow:hidden;\">PETR4 caiu abaixo do seu preço de referência para compra: R$ 22,50</span>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AlertKind.Sell, "#c0392b", "#fdecea", "Vender agora", "Alerta de venda")]
    [InlineData(AlertKind.Buy, "#1e8449", "#e9f7ef", "Comprar agora", "Alerta de compra")]
    public void Compose_HtmlBody_IsColoredAndLabelledByAlertKind(AlertKind kind, string color, string tint, string button, string banner)
    {
        var alert = kind == AlertKind.Sell ? TestData.SellAlert() : TestData.BuyAlert();

        var html = _composer.Compose(alert).HtmlBody;

        Assert.Contains($"background:{color};padding:20px 28px", html, StringComparison.Ordinal);
        Assert.Contains($"background:{tint};border-left:4px solid {color}", html, StringComparison.Ordinal);
        Assert.Contains($">{button}</a>", html, StringComparison.Ordinal);
        Assert.Contains($">{banner}</div>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AlertKind.Sell, 22.70, "https://homebroker.example.com/negociar?ativo=PETR4&amp;operacao=venda&amp;preco=22.70")]
    [InlineData(AlertKind.Buy, 22.5, "https://homebroker.example.com/negociar?ativo=PETR4&amp;operacao=compra&amp;preco=22.50")]
    public void Compose_HtmlButton_LinksToTheTradeWithEncodedQuery(AlertKind kind, decimal price, string expectedHref)
    {
        var alert = kind == AlertKind.Sell ? TestData.SellAlert(price) : TestData.BuyAlert(price);

        var html = _composer.Compose(alert).HtmlBody;

        Assert.Contains($"<a href=\"{expectedHref}\" target=\"_blank\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_TradeLink_UsesTheConfiguredPlatform()
    {
        var options = TestData.EmailOptions;
        options.TradingPlatformUrl = new Uri("https://broker.test/app/");
        var composer = new AlertEmailComposer(Options.Create(options));

        var email = composer.Compose(TestData.SellAlert(22.70m));

        Assert.Contains("Vender agora: https://broker.test/app/negociar?ativo=PETR4&operacao=venda&preco=22.70", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://broker.test/app/negociar?ativo=PETR4&amp;operacao=venda&amp;preco=22.70\"", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_TradeLink_FormatsThePriceWithADotAndTwoDecimals()
    {
        var alert = new PriceAlert(TestData.Petr4, AlertKind.Sell, 1500m, 1234.5m, TestData.Now);

        Assert.Contains("&preco=1500.00", _composer.Compose(alert).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_HtmlBody_EncodesTheValuesItShows()
    {
        var options = TestData.EmailOptions;
        options.TradingPlatformUrl = new Uri("https://broker.test/\"onmouseover=\"x/");
        var composer = new AlertEmailComposer(Options.Create(options));

        var html = composer.Compose(TestData.SellAlert()).HtmlBody;

        Assert.DoesNotContain("\"onmouseover", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href=\"https://broker.test/\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NullAlert_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _composer.Compose(null!));
    }
}