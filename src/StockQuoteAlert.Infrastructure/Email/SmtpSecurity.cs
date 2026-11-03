namespace StockQuoteAlert.Infrastructure.Email;

public enum SmtpSecurity
{
    /// <summary>Connects in plain text and upgrades with STARTTLS (usually port 587).</summary>
    StartTls,

    /// <summary>Connects with TLS from the start (usually port 465).</summary>
    SslOnConnect,

    /// <summary>No encryption. Only for local test servers (e.g. Mailpit).</summary>
    None,
}