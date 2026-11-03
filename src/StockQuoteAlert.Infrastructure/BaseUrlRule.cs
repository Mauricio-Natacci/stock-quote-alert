using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Infrastructure;

/// <summary>A base URL (API root, trading platform) must be absolute http(s) and end with '/', so relative paths are appended to it.</summary>
internal static class BaseUrlRule
{
    public static ValidationResult? Validate(Uri? baseUrl, string key)
    {
        if (baseUrl is null)
        {
            return null;
        }

        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            return new ValidationResult($"{key} must be an absolute http or https URL.", ["BaseUrl"]);
        }

        return baseUrl.AbsolutePath.EndsWith('/')
            ? null
            : new ValidationResult($"{key} must end with '/'.", ["BaseUrl"]);
    }
}