using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

/// <summary>Sends the configured token as a Bearer header; without a token, requests go out anonymous.</summary>
internal sealed class BrapiAuthenticationHandler(IOptions<BrapiOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = options.Value.Token;

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}