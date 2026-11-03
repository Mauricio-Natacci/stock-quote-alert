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
    }

    // A single address such as "me@gmail.com": no display name, no list, no line breaks.
    private static bool IsBareAddress(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value;
}