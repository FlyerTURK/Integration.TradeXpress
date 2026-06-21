using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// Harem canlı kurlarını HaremBridge (tools/HaremBridge) localhost endpoint'inden okur.
/// Harem fiyatları Cloudflare arkasındaki socket.io WebSocket'inden push edilir; köprü
/// (Playwright + Chrome) sayfayı açık tutup ham fiyatları JSON sunar:
/// <code>{ "lastUpdate":"&lt;ISO&gt;", "kurlar": { "ALTIN": {"alis","satis","tarih"}, ... } }</code>
///
/// <para>Ham Harem sembollerini iç birim kodlarına (<see cref="HaremCodeMapping"/>) eşler,
/// PLT/PLD'yi türetir. Source = "Harem". Ağ/format hatasında çağıran worker yakalar.</para>
/// </summary>
public sealed class HaremClient : ISingletonDependency
{
    internal const string HttpClientName = "Harem";
    public const string SourceName = "Harem";

    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
    private readonly Microsoft.Extensions.Options.IOptions<ExchangeRateOptions> _options;

    public HaremClient(
        System.Net.Http.IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Options.IOptions<ExchangeRateOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    private static readonly TimeZoneInfo TurkeyTimeZone = ResolveTurkeyTimeZone();

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"); }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    id: "TX-TR-Fallback",
                    baseUtcOffset: TimeSpan.FromHours(3),
                    displayName: "(UTC+03:00) Türkiye",
                    standardDisplayName: "TRT");
            }
        }
    }

    private static readonly string[] TrDateTimeFormats =
    [
        "dd-MM-yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm",
        "dd.MM.yyyy HH:mm:ss",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Köprüden tüm takip edilen kotasyonları çeker (HTTP GET poll). Köprü
    /// ulaşılamaz veya JSON bozuksa exception fırlatır (worker yakalar).</summary>
    public async Task<List<MarketQuote>> FetchAllAsync(CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        var json = await http.GetStringAsync(_options.Value.HaremBridgeUrl, cancellationToken);
        return ParseSnapshot(json);
    }

    /// <summary>Köprü JSON gövdesini (GET snapshot veya SSE event — aynı şema) iç
    /// kotasyon listesine çevirir. Test edilebilir saf metot.</summary>
    public static List<MarketQuote> ParseSnapshot(string json)
    {
        var kurlar = ParseKurlar(json);
        return kurlar is null ? new List<MarketQuote>() : MapQuotes(kurlar);
    }

    internal static Dictionary<string, RawQuote>? ParseKurlar(string json)
        => JsonSerializer.Deserialize<BridgePayload>(json, JsonOptions)?.Kurlar;

    internal static List<MarketQuote> MapQuotes(IReadOnlyDictionary<string, RawQuote> kurlar)
    {
        var result = new List<MarketQuote>();

        foreach (var (haremSymbol, raw) in kurlar)
        {
            var quote = Map(haremSymbol, raw);
            if (quote is not null)
                result.Add(quote);
        }

        // PLT/PLD: Harem PLATIN/PALADYUM USD/kg cinsinden → gram-TRY:
        //   alış = PLATIN.alis / 1000 × USDTRY.alis ; satış benzer.
        AddDerivedMetal(kurlar, "PLATIN",   CurrencyUnitCode.PLT, result);
        AddDerivedMetal(kurlar, "PALADYUM", CurrencyUnitCode.PLD, result);

        return result;
    }

    private static void AddDerivedMetal(
        IReadOnlyDictionary<string, RawQuote> kurlar,
        string haremSymbol,
        string unitCode,
        List<MarketQuote> result)
    {
        if (!kurlar.TryGetValue(haremSymbol, out var metal) ||
            !kurlar.TryGetValue("USDTRY", out var usd))
            return;

        if (!TryParseNumber(metal.Buy,  out var metalBuyUsdPerKg)  ||
            !TryParseNumber(metal.Sell, out var metalSellUsdPerKg) ||
            !TryParseNumber(usd.Buy,    out var usdBuy)            ||
            !TryParseNumber(usd.Sell,   out var usdSell))
            return;

        var metalTs = ParseTrDateTimeToUtc(metal.Date);
        var usdTs   = ParseTrDateTimeToUtc(usd.Date);
        DateTime? ts = metalTs is null || usdTs is null
            ? null
            : (metalTs < usdTs ? metalTs : usdTs);

        result.Add(new MarketQuote
        {
            Code         = unitCode,
            Description  = haremSymbol,
            Buy          = decimal.Round(metalBuyUsdPerKg  / 1000m * usdBuy,  4, MidpointRounding.AwayFromZero),
            Sell         = decimal.Round(metalSellUsdPerKg / 1000m * usdSell, 4, MidpointRounding.AwayFromZero),
            UpdatedAtRaw = metal.Date ?? string.Empty,
            UpdatedAtUtc = ts,
            Source       = SourceName,
        });
    }

    private static MarketQuote? Map(string haremSymbol, RawQuote raw)
    {
        var unitCode = HaremCodeMapping.ToUnitCode(haremSymbol);
        if (unitCode is null)
            return null; // takip etmediğimiz sembol

        if (!TryParseNumber(raw.Buy, out var buy) || !TryParseNumber(raw.Sell, out var sell))
            return null;

        return new MarketQuote
        {
            Code         = unitCode,
            Description  = haremSymbol,
            Buy          = buy,
            Sell         = sell,
            UpdatedAtRaw = raw.Date ?? string.Empty,
            UpdatedAtUtc = ParseTrDateTimeToUtc(raw.Date),
            Source       = SourceName,
        };
    }

    /// <summary>Harem sayı string'i → decimal. Ondalık ayraç sembole göre değişir
    /// (nokta veya virgül) ama binlik ayraç kullanılmaz → virgülü noktaya çevirip
    /// InvariantCulture ile parse doğrudur. value > 0 değilse false.</summary>
    private static bool TryParseNumber(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var normalized = input.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            && value > 0m;
    }

    private static DateTime? ParseTrDateTimeToUtc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        if (!DateTime.TryParseExact(input.Trim(), TrDateTimeFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return null;

        var trUnspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(trUnspecified, TurkeyTimeZone);
    }

    private sealed class BridgePayload
    {
        [JsonPropertyName("lastUpdate")] public string? LastUpdate { get; set; }
        [JsonPropertyName("kurlar")] public Dictionary<string, RawQuote>? Kurlar { get; set; }
    }

    internal sealed class RawQuote
    {
        [JsonPropertyName("alis")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Buy { get; set; }

        [JsonPropertyName("satis")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? Sell { get; set; }

        [JsonPropertyName("tarih")] public string? Date { get; set; }
    }

    /// <summary>Harem bazı sembollerde alis/satis'i JSON number push eder; number
    /// token'ı invariant metne çevirip default-string converter patlamasını önler.</summary>
    private sealed class LenientStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null   => null,
                _ => throw new JsonException($"Unexpected token type '{reader.TokenType}' for a Harem quote value."),
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
