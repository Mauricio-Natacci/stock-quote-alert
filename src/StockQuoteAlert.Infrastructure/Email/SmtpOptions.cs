using System.ComponentModel.DataAnnotations;

namespace StockQuoteAlert.Infrastructure.Email;

public sealed class SmtpOptions : IValidatableObject
{
    public const string SectionName = "Smtp";

    [Required(AllowEmptyStrings = false, ErrorMessage = $"{SectionName}:{nameof(Host)} is required.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = $"{SectionName}:{nameof(Port)} must be between 1 and 65535.")]
    public int Port { get; set; } = 587;

    [EnumDataType(typeof(SmtpSecurity), ErrorMessage = $"{SectionName}:{nameof(Security)} must be StartTls, SslOnConnect or None.")]
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Disable only for local test servers (e.g. Mailpit) that do not support AUTH.</summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// MailKit checks OCSP/CRL by default, which fails on hosts that cannot reach the revocation servers
    /// (e.g. macOS with Homebrew .NET). Off by default, matching SslStream; the certificate chain is still validated.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Keep it out of appsettings.json: set it through the <c>Smtp__Password</c> environment variable.</summary>
    public string Password { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:00:01", "00:05:00", ErrorMessage = $"{SectionName}:{nameof(Timeout)} must be between 1 second and 5 minutes.")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RequireAuthentication)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Username)} is required when {SectionName}:{nameof(RequireAuthentication)} is true.",
                [nameof(Username)]);
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Password)} is required when {SectionName}:{nameof(RequireAuthentication)} is true (set it through the Smtp__Password environment variable).",
                [nameof(Password)]);
        }

        if (Security == SmtpSecurity.None)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Security)} None would send the password unencrypted; use StartTls or SslOnConnect, or set {SectionName}:{nameof(RequireAuthentication)} to false.",
                [nameof(Security), nameof(RequireAuthentication)]);
        }
    }
}