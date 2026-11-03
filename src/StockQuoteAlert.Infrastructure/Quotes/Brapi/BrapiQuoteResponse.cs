using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

internal sealed record BrapiQuoteResponse(IReadOnlyList<BrapiQuoteResult>? Results);

internal sealed record BrapiQuoteResult(string? Symbol, decimal? RegularMarketPrice, DateTimeOffset? RegularMarketTime);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(BrapiQuoteResponse))]
internal sealed partial class BrapiJsonContext : JsonSerializerContext;