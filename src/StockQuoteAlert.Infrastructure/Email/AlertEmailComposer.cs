using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Core.Formatting;

namespace StockQuoteAlert.Infrastructure.Email;

/// <summary>
/// Writes the alert e-mail in Portuguese (prices in R$, times in Brasília time), as plain text and as HTML with a
/// button to the trading platform. Every value placed in the HTML is encoded.
/// </summary>
internal sealed class AlertEmailComposer(IOptions<EmailOptions> options) : IAlertEmailComposer
{
    private const string Reminder =
        "Para evitar e-mails repetidos, um novo alerta só será enviado quando a cotação voltar a ultrapassar uma das " +
        "referências configuradas: subir acima da referência de venda ou cair abaixo da referência de compra.";

    private const string Disclaimer =
        "Alerta automático gerado a partir das referências configuradas por você. Não constitui recomendação de investimento.";

    // Escapes markup characters but keeps accented letters readable; the e-mail is sent as UTF-8.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public EmailContent Compose(PriceAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var copy = AlertCopy.For(alert.Kind);
        var ticker = alert.Ticker.Symbol;

        var gap = Math.Abs(alert.CurrentPrice - alert.ReferencePrice);
        var gapPercent = BrazilianFormat.Percent(gap / alert.ReferencePrice * 100m);
        var price = BrazilianFormat.Currency(alert.CurrentPrice);
        var referencePrice = BrazilianFormat.Currency(alert.ReferencePrice);
        var gapText = $"{BrazilianFormat.Currency(gap)} ({gapPercent} {copy.Direction})";
        var quotedAt = BrazilianFormat.DateTimeWithoutSeconds(alert.QuotedAt);
        var tradeUrl = BuildTradeUrl(alert, copy).AbsoluteUri;

        var subject = $"[{copy.Action}] {ticker} {copy.Moved} para {price}, {gapPercent} {copy.Direction} da referência de {copy.Reference}";
        var headline = $"{ticker} {copy.Crossed} seu preço de referência para {copy.Reference}";

        var textBody = $"""
            Olá,

            {headline}.

              {Row("Cotação atual")}{price}
              {Row($"Referência de {copy.Reference}")}{referencePrice}
              {Row("Diferença")}{gapText}
              {Row("Cotado em")}{quotedAt} (horário de Brasília)

            Recomendação: {copy.Action}. {copy.Advice}

            {copy.CallToAction}: {tradeUrl}

            {Reminder}

            --
            Stock Quote Alert
            {Disclaimer}
            """;

        var htmlBody = RenderHtml(new HtmlModel(
            copy,
            Encode(ticker),
            Encode(headline),
            Encode(price),
            Encode(referencePrice),
            Encode(gapText),
            Encode(quotedAt),
            Encode(copy.Advice),
            Encode(tradeUrl),
            Encode(Reminder),
            Encode(Disclaimer)));

        return new EmailContent(subject, textBody, htmlBody);
    }

    /// <summary>e.g. https://homebroker.example.com/negociar?ativo=PETR4&amp;operacao=venda&amp;preco=22.70</summary>
    private Uri BuildTradeUrl(PriceAlert alert, AlertCopy copy)
    {
        var price = alert.CurrentPrice.ToString("0.00", CultureInfo.InvariantCulture);
        var query = $"negociar?ativo={Uri.EscapeDataString(alert.Ticker.Symbol)}&operacao={copy.Reference}&preco={price}";

        return new Uri(options.Value.TradingPlatformUrl, query);
    }

    private static string Row(string label) => $"{label}:".PadRight(24);

    private static string Encode(string value) => Html.Encode(value);

    private static string RenderHtml(HtmlModel m) => $$"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Alerta de {{m.Copy.Reference}}: {{m.Ticker}}</title>
        </head>
        <body style="margin:0;padding:0;background:#f4f5f7;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#1f2933;">
          <span style="display:none;max-height:0;overflow:hidden;">{{m.Headline}}: {{m.Price}}</span>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5f7;padding:24px 12px;">
            <tr>
              <td align="center">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e4e7eb;">
                  <tr>
                    <td style="background:{{m.Copy.Color}};padding:20px 28px;color:#ffffff;">
                      <div style="font-size:13px;letter-spacing:1px;text-transform:uppercase;opacity:0.9;">Alerta de {{m.Copy.Reference}}</div>
                      <div style="font-size:26px;font-weight:700;margin-top:4px;">{{m.Ticker}} · {{m.Price}}</div>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:28px;">
                      <p style="margin:0 0 20px;font-size:16px;line-height:1.5;">Olá,<br>{{m.Headline}}.</p>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:15px;border-collapse:collapse;">
                        <tr><td style="padding:10px 0;color:#616e7c;border-bottom:1px solid #e4e7eb;">Cotação atual</td><td align="right" style="padding:10px 0;font-weight:700;border-bottom:1px solid #e4e7eb;">{{m.Price}}</td></tr>
                        <tr><td style="padding:10px 0;color:#616e7c;border-bottom:1px solid #e4e7eb;">Referência de {{m.Copy.Reference}}</td><td align="right" style="padding:10px 0;border-bottom:1px solid #e4e7eb;">{{m.ReferencePrice}}</td></tr>
                        <tr><td style="padding:10px 0;color:#616e7c;border-bottom:1px solid #e4e7eb;">Diferença</td><td align="right" style="padding:10px 0;color:{{m.Copy.Color}};font-weight:600;border-bottom:1px solid #e4e7eb;">{{m.Gap}}</td></tr>
                        <tr><td style="padding:10px 0;color:#616e7c;">Cotado em</td><td align="right" style="padding:10px 0;">{{m.QuotedAt}} <span style="color:#9aa5b1;">(Brasília)</span></td></tr>
                      </table>
                      <div style="margin:24px 0;padding:14px 16px;background:{{m.Copy.Tint}};border-left:4px solid {{m.Copy.Color}};border-radius:6px;font-size:15px;line-height:1.5;">
                        <strong>Recomendação: {{m.Copy.Action}}.</strong> {{m.Advice}}
                      </div>
                      <table role="presentation" cellpadding="0" cellspacing="0" align="center">
                        <tr>
                          <td style="border-radius:8px;background:{{m.Copy.Color}};">
                            <a href="{{m.TradeUrl}}" target="_blank" style="display:inline-block;padding:14px 32px;color:#ffffff;font-size:16px;font-weight:700;text-decoration:none;">{{m.Copy.CallToAction}}</a>
                          </td>
                        </tr>
                      </table>
                      <p style="margin:20px 0 0;font-size:13px;color:#616e7c;line-height:1.5;">{{m.Reminder}}</p>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:16px 28px;background:#f9fafb;border-top:1px solid #e4e7eb;font-size:12px;color:#9aa5b1;line-height:1.5;">
                      <strong>Stock Quote Alert</strong><br>
                      {{m.Disclaimer}}
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    private sealed record HtmlModel(
        AlertCopy Copy,
        string Ticker,
        string Headline,
        string Price,
        string ReferencePrice,
        string Gap,
        string QuotedAt,
        string Advice,
        string TradeUrl,
        string Reminder,
        string Disclaimer);

    private sealed record AlertCopy(
        string Action,
        string Reference,
        string Direction,
        string Moved,
        string Crossed,
        string Advice,
        string CallToAction,
        string Color,
        string Tint)
    {
        private static readonly AlertCopy Sell = new(
            Action: "VENDA",
            Reference: "venda",
            Direction: "acima",
            Moved: "subiu",
            Crossed: "superou o",
            Advice: "O ativo está sendo negociado acima do preço-alvo que você definiu; pode ser um bom momento para realizar o lucro.",
            CallToAction: "Vender agora",
            Color: "#c0392b",
            Tint: "#fdecea");

        private static readonly AlertCopy Buy = new(
            Action: "COMPRA",
            Reference: "compra",
            Direction: "abaixo",
            Moved: "caiu",
            Crossed: "caiu abaixo do",
            Advice: "O ativo está sendo negociado abaixo do preço-alvo que você definiu; pode ser uma boa oportunidade de entrada.",
            CallToAction: "Comprar agora",
            Color: "#1e8449",
            Tint: "#e9f7ef");

        public static AlertCopy For(AlertKind kind) => kind switch
        {
            AlertKind.Sell => Sell,
            AlertKind.Buy => Buy,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported alert kind."),
        };
    }
}