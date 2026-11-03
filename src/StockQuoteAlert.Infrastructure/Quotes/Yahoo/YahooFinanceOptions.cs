using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;

namespace StockQuoteAlert.Infrastructure.Quotes.Yahoo;

public sealed class YahooFinanceOptions : IValidatableObject
{
    public const string SectionName = "QuoteProvider:Yahoo";

    /// <summary>Root of the API. Must be absolute http(s) and end with '/', so relative paths are appended to it.</summary>
    [Required(ErrorMessage = $"{SectionName}:{nameof(BaseUrl)} is required.")]
    public Uri BaseUrl { get; set; } = new("https://query1.finance.yahoo.com/");

    /// <summary>Yahoo rejects requests without a User-Agent.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = $"{SectionName}:{nameof(UserAgent)} is required.")]
    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; stock-quote-alert/1.0)";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is not null)
        {
            if (!BaseUrl.IsAbsoluteUri || (BaseUrl.Scheme != Uri.UriSchemeHttp && BaseUrl.Scheme != Uri.UriSchemeHttps))
            {
                yield return new ValidationResult($"{SectionName}:{nameof(BaseUrl)} must be an absolute http or https URL.", [nameof(BaseUrl)]);
            }
            else if (!BaseUrl.AbsolutePath.EndsWith('/'))
            {
                yield return new ValidationResult($"{SectionName}:{nameof(BaseUrl)} must end with '/'.", [nameof(BaseUrl)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(UserAgent) && !IsValidUserAgent(UserAgent))
        {
            yield return new ValidationResult($"{SectionName}:{nameof(UserAgent)} is not a valid User-Agent header value.", [nameof(UserAgent)]);
        }
    }

    private static bool IsValidUserAgent(string value)
    {
        using var request = new HttpRequestMessage();

        return request.Headers.UserAgent.TryParseAdd(value) && request.Headers.UserAgent.Count > 0;
    }
}