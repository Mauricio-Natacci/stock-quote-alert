using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Core.Formatting;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>Writes the alert e-mail in Portuguese: prices in R$, times in Brasília time.</summary>
internal sealed class AlertEmailComposer : IAlertEmailComposer
{
    private const string Reminder =
        "Para evitar e-mails repetidos, um novo alerta só será enviado quando a cotação voltar a ultrapassar uma das " +
        "referências configuradas: subir acima da referência de venda ou cair abaixo da referência de compra.";

    public EmailContent Compose(PriceAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var copy = AlertCopy.For(alert.Kind);
        var ticker = alert.Ticker.Symbol;

        var gap = Math.Abs(alert.CurrentPrice - alert.ReferencePrice);
        var gapPercent = BrazilianFormat.Percent(gap / alert.ReferencePrice * 100m);
        var price = BrazilianFormat.Currency(alert.CurrentPrice);
        var referencePrice = BrazilianFormat.Currency(alert.ReferencePrice);
        var quotedAt = BrazilianFormat.DateTimeWithoutSeconds(alert.QuotedAt);

        var subject = $"[{copy.Action}] {ticker} {copy.Moved} para {price}, {gapPercent} {copy.Direction} da referência de {copy.Reference}";

        var textBody = $"""
            Olá,

            {ticker} {copy.Crossed} seu preço de referência para {copy.Reference}.

              {Row("Cotação atual")}{price}
              {Row($"Referência de {copy.Reference}")}{referencePrice}
              {Row("Diferença")}{BrazilianFormat.Currency(gap)} ({gapPercent} {copy.Direction})
              {Row("Cotado em")}{quotedAt} (horário de Brasília)

            Recomendação: {copy.Action}. {copy.Advice}

            {Reminder}

            --
            Stock Quote Alert
            Alerta automático gerado a partir das referências configuradas por você. Não constitui recomendação de investimento.
            """;

        return new EmailContent(subject, textBody);
    }

    private static string Row(string label) => $"{label}:".PadRight(24);

    private sealed record AlertCopy(
        string Action,
        string Reference,
        string Direction,
        string Moved,
        string Crossed,
        string Advice)
    {
        private static readonly AlertCopy Sell = new(
            Action: "VENDA",
            Reference: "venda",
            Direction: "acima",
            Moved: "subiu",
            Crossed: "superou o",
            Advice: "O ativo está sendo negociado acima do preço-alvo que você definiu; pode ser um bom momento para realizar o lucro.");

        private static readonly AlertCopy Buy = new(
            Action: "COMPRA",
            Reference: "compra",
            Direction: "abaixo",
            Moved: "caiu",
            Crossed: "caiu abaixo do",
            Advice: "O ativo está sendo negociado abaixo do preço-alvo que você definiu; pode ser uma boa oportunidade de entrada.");

        public static AlertCopy For(AlertKind kind) => kind switch
        {
            AlertKind.Sell => Sell,
            AlertKind.Buy => Buy,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported alert kind."),
        };
    }
}