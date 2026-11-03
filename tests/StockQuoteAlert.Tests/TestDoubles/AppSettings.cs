namespace StockQuoteAlert.Tests.TestDoubles;

internal static class AppSettings
{
    /// <summary>The minimum configuration the app needs to start.</summary>
    public static Dictionary<string, string?> Valid => new()
    {
        ["Email:Recipient"] = "dest@gmail.com",
        ["Email:SenderAddress"] = "me@gmail.com",
        ["Smtp:Host"] = "smtp.gmail.com",
        ["Smtp:Username"] = "me@gmail.com",
        ["Smtp:Password"] = "app-password",
    };

    /// <summary>
    /// <see cref="Valid"/> with the backup provider turned off. The shipped appsettings.json falls back to brapi,
    /// so hosts that fake only Yahoo use this to never reach the real brapi.dev.
    /// </summary>
    public static Dictionary<string, string?> WithoutFallback => new(Valid) { ["QuoteProvider:Fallback"] = "" };
}