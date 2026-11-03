using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Infrastructure.Quotes;

public sealed class QuoteProviderOptions : IValidatableObject
{
    public const string SectionName = "QuoteProvider";

    [EnumDataType(typeof(QuoteProviderKind), ErrorMessage = $"{SectionName}:{nameof(Provider)} must be Yahoo or Brapi.")]
    public QuoteProviderKind Provider { get; set; } = QuoteProviderKind.Yahoo;

    /// <summary>Optional provider asked when <see cref="Provider"/> fails. Leave empty to disable.</summary>
    [EnumDataType(typeof(QuoteProviderKind), ErrorMessage = $"{SectionName}:{nameof(Fallback)} must be Yahoo, Brapi or empty.")]
    public QuoteProviderKind? Fallback { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Fallback == Provider)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Fallback)} must be different from {SectionName}:{nameof(Provider)}.",
                [nameof(Fallback)]);
        }
    }
}