using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockQuoteAlert.Infrastructure.Quotes.Yahoo;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(YahooChartResponse))]
internal sealed partial class YahooJsonContext : JsonSerializerContext;