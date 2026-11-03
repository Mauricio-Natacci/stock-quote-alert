using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

public sealed class BrapiOptions : IValidatableObject
{
    public const string SectionName = "QuoteProvider:Brapi";

    /// <summary>Root of the API. Must be absolute http(s) and end with '/'.</summary>
    [Required(ErrorMessage = $"{SectionName}:{nameof(BaseUrl)} is required.")]
    public Uri BaseUrl { get; set; } = new("https://brapi.dev/");

    /// <summary>
    /// Optional: without a token brapi only serves a few test tickers (e.g. PETR4, VALE3). Keep it out of
    /// appsettings.json: set it through user-secrets or the <c>QuoteProvider__Brapi__Token</c> environment variable.
    /// </summary>
    public string? Token { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrlRule.Validate(BaseUrl, $"{SectionName}:{nameof(BaseUrl)}") is { } invalidUrl)
        {
            yield return invalidUrl;
        }

        if (Token is { Length: > 0 } token && token.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            yield return new ValidationResult($"{SectionName}:{nameof(Token)} must not contain spaces or line breaks.", [nameof(Token)]);
        }
    }
}