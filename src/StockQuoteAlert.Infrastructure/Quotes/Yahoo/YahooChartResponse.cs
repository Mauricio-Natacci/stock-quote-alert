using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockQuoteAlert.Infrastructure.Quotes.Yahoo;

internal sealed record YahooChartResponse(YahooChart? Chart);

internal sealed record YahooChart(IReadOnlyList<YahooChartResult>? Result);

internal sealed record YahooChartResult(YahooChartMeta? Meta);

internal sealed record YahooChartMeta(decimal? RegularMarketPrice, long? RegularMarketTime);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(YahooChartResponse))]
internal sealed partial class YahooJsonContext : JsonSerializerContext;