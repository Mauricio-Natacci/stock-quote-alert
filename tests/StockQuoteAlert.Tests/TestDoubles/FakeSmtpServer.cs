using System.Collections.Concurrent;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NSubstitute;
using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// Simulates the external SMTP server by handing out substituted MailKit clients. Records what every client was asked
/// to do and every delivered message; each stage can be scripted to fail.
/// </summary>
internal sealed class FakeSmtpServer : ISmtpClientFactory
{
    public ConcurrentQueue<ISmtpClient> Clients { get; } = new();

    public ConcurrentQueue<SmtpConnection> Connections { get; } = new();

    public ConcurrentQueue<SmtpLogin> Logins { get; } = new();

    public ConcurrentQueue<DeliveredEmail> Delivered { get; } = new();

    public ISmtpClient LastClient => Clients.Last();

    public Exception? ConnectionFailure { get; set; }

    public Exception? AuthenticationFailure { get; set; }

    public Exception? SendFailure { get; set; }

    public ISmtpClient Create()
    {
        var client = Substitute.For<ISmtpClient>();

        client
            .ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Connections.Enqueue(new SmtpConnection(call.ArgAt<string>(0), call.ArgAt<int>(1), call.ArgAt<SecureSocketOptions>(2)));
                return ConnectionFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
            });

        client
            .AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Logins.Enqueue(new SmtpLogin(call.ArgAt<string>(0), call.ArgAt<string>(1)));
                return AuthenticationFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
            });

        client
            .SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(call =>
            {
                if (SendFailure is { } failure)
                {
                    return Task.FromException<string>(failure);
                }

                Delivered.Enqueue(DeliveredEmail.From(call.Arg<MimeMessage>()));
                return Task.FromResult("250 OK");
            });

        Clients.Enqueue(client);
        return client;
    }
}