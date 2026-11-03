using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Tests.TestDoubles;

internal static class OptionsValidation
{
    /// <summary>Validates like <c>ValidateDataAnnotations</c> does and returns the error messages.</summary>
    public static List<string?> Errors(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results.Select(result => result.ErrorMessage)];
    }
}