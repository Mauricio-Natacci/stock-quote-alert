using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class AlertEmailComposerTests
{
    private readonly AlertEmailComposer _composer = new();

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
    public void Compose_NullAlert_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _composer.Compose(null!));
    }
}