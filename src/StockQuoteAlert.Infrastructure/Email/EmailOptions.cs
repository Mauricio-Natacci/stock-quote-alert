using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace StockQuoteAlert.Infrastructure.Email;

public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";

    [Required(AllowEmptyStrings = false, ErrorMessage = $"{SectionName}:{nameof(Recipient)} is required.")]
    public string Recipient { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = $"{SectionName}:{nameof(SenderAddress)} is required.")]
    public string SenderAddress { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = $"{SectionName}:{nameof(SenderName)} is required.")]
    public string SenderName { get; set; } = "Stock Quote Alert";

    /// <summary>
    /// Home broker behind the e-mail's "Vender/Comprar agora" button; the trade path is appended to it, so it must end
    /// with '/'. The default is a fictitious placeholder.
    /// </summary>
    [Required(ErrorMessage = $"{SectionName}:{nameof(TradingPlatformUrl)} is required.")]
    public Uri TradingPlatformUrl { get; set; } = new("https://homebroker.example.com/");

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsBareAddress(Recipient))
        {
            yield return new ValidationResult($"{SectionName}:{nameof(Recipient)} must be a valid e-mail address.", [nameof(Recipient)]);
        }

        if (!IsBareAddress(SenderAddress))
        {
            yield return new ValidationResult($"{SectionName}:{nameof(SenderAddress)} must be a valid e-mail address.", [nameof(SenderAddress)]);
        }

        if (BaseUrlRule.Validate(TradingPlatformUrl, $"{SectionName}:{nameof(TradingPlatformUrl)}") is { } invalidUrl)
        {
            yield return invalidUrl;
        }
    }

    // A single address such as "me@gmail.com": no display name, no list, no line breaks.
    private static bool IsBareAddress(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value;
}