using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace StockQuoteAlert.Cli;

/// <summary>Recognizes startup failures caused by invalid configuration and turns them into readable messages.</summary>
internal static partial class ConfigurationErrors
{
    /// <returns>
    /// <see langword="true"/> when every failure in <paramref name="exception"/> is a configuration error: a value that
    /// failed validation or one that could not be read (e.g. "abc" for a port).
    /// </returns>
    public static bool TryExtract(Exception exception, out IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(exception);

        IReadOnlyCollection<Exception> candidates = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];

        var messages = new List<string>();

        foreach (var candidate in candidates)
        {
            switch (candidate)
            {
                case OptionsValidationException validation:
                    messages.AddRange(validation.Failures.Select(Unwrap));
                    break;

                case InvalidOperationException conversion when conversion.Message.StartsWith("Failed to convert configuration value", StringComparison.Ordinal):
                    messages.Add(conversion.Message);
                    break;

                default:
                    failures = [];
                    return false;
            }
        }

        failures = messages;
        return messages.Count > 0;
    }

    // Our messages already name the setting ("Email:Recipient is required."); the framework's
    // "DataAnnotation validation failed for 'X' members: 'Y' with the error: '…'." wrapper only adds noise.
    private static string Unwrap(string failure) =>
        DataAnnotationFailure().Match(failure) is { Success: true } match ? match.Groups["message"].Value : failure;

    [GeneratedRegex(@"^DataAnnotation validation failed for '[^']*' members: '[^']*' with the error: '(?<message>.*)'\.$", RegexOptions.Singleline)]
    private static partial Regex DataAnnotationFailure();
}