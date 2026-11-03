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
}