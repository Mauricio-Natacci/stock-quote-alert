using MailKit.Security;

namespace StockQuoteAlert.Tests.TestDoubles;

internal sealed record SmtpConnection(string Host, int Port, SecureSocketOptions Security);