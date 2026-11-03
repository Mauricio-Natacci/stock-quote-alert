using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockQuoteAlert.Infrastructure.Quotes.Brapi;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(BrapiQuoteResponse))]
internal sealed partial class BrapiJsonContext : JsonSerializerContext;