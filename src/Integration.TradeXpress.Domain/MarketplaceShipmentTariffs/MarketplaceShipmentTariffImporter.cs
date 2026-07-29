using System.Globalization;
using System.IO;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Pazaryeri anlaşmalı kargo tarifesi TSV'sinin <b>SAF</b> parse çekirdeği — DB'siz, test edilir
/// (emsal: <see cref="N11Categories.N11CategoryCommissionImporter"/>).
///
/// <para><b>Fail-fast, interpolasyon YASAK:</b> eksik/bozuk hücrede satır SESSİZCE ATLANMAZ — parse hatası
/// toplanır ve çağıran seed'i durdurur. Eksik bir desi satırını komşularından türetmek, gerçekte var olmayan
/// bir fiyatı doğruymuş gibi sunardı; kargo maliyeti doğrudan satış fiyatına giriyor.</para>
///
/// <para><b>Biçim (bölümlü TSV):</b> <c>[META]</c> anahtar/değer · <c>[CARRIER]</c> taşıyıcı başlıkları ·
/// <c>[DESI]</c> desi × taşıyıcı fiyat matrisi · <c>[CONDITIONAL]</c> şartlı barem. <c>#</c> ile başlayan
/// satırlar yorumdur. Tutarlar INVARIANT ondalıkla (nokta) yazılır — TR biçim dönüşümü TSV ÜRETİMİNDE
/// yapılır, burada değil (kaynakta virgül hem ondalık hem binlik anlamına gelebiliyor, o ikili anlam
/// parser'a taşınmaz).</para>
/// </summary>
public static class MarketplaceShipmentTariffImporter
{
    private const string MetaSection = "[META]";
    private const string CarrierSection = "[CARRIER]";
    private const string DesiSection = "[DESI]";
    private const string ConditionalSection = "[CONDITIONAL]";

    /// <summary>Gömülü TSV'yi okur (deploy'da <c>.claude</c> klasörü yok → kaynak paketle taşınır).</summary>
    public static string ReadEmbeddedTsv(string resourceFileName)
    {
        var assembly = typeof(MarketplaceShipmentTariffImporter).Assembly;
        var resourceName = $"Integration.TradeXpress.MarketplaceShipmentTariffs.{resourceFileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new BusinessException("TradeXpress:ShipmentTariff:TsvMissing")
                .WithData("resource", resourceName);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>TSV içeriğini ayrıştırır. Hata varsa <see cref="MarketplaceShipmentTariffParseResult.Errors"/>
    /// dolu döner — çağıran seed'i BAŞLATMADAN durdurmalıdır.</summary>
    public static MarketplaceShipmentTariffParseResult ParseTsv(string content)
    {
        var errors = new List<string>();
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var carriers = new List<MarketplaceShipmentTariffCarrierRow>();
        var conditionals = new List<MarketplaceShipmentTariffConditionalRow>();
        var rates = new Dictionary<string, Dictionary<int, decimal>>(StringComparer.Ordinal);

        var section = string.Empty;
        var desiColumns = new List<string>();
        var conditionalColumns = new List<string>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            var lineNo = i + 1;

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                section = line;
                continue;
            }

            var cells = line.Split('\t');

            switch (section)
            {
                case MetaSection:
                    if (cells.Length < 2)
                    {
                        errors.Add($"satır {lineNo}: [META] anahtar/değer bekleniyordu.");
                        break;
                    }

                    meta[cells[0].Trim()] = cells[1].Trim();
                    break;

                case CarrierSection:
                    if (string.Equals(cells[0].Trim(), "Code", StringComparison.OrdinalIgnoreCase))
                    {
                        break;   // başlık satırı
                    }

                    ParseCarrier(cells, lineNo, carriers, errors);
                    break;

                case DesiSection:
                    if (string.Equals(cells[0].Trim(), "Desi", StringComparison.OrdinalIgnoreCase))
                    {
                        desiColumns = cells.Skip(1).Select(c => c.Trim()).ToList();
                        break;
                    }

                    ParseDesiRow(cells, lineNo, desiColumns, rates, errors);
                    break;

                case ConditionalSection:
                    if (string.Equals(cells[0].Trim(), "BasketFrom", StringComparison.OrdinalIgnoreCase))
                    {
                        conditionalColumns = cells.Skip(2).Select(c => c.Trim()).ToList();
                        break;
                    }

                    ParseConditionalRow(cells, lineNo, conditionalColumns, conditionals, errors);
                    break;

                default:
                    errors.Add($"satır {lineNo}: bölüm başlığından ([META]/[CARRIER]/[DESI]/[CONDITIONAL]) önce veri.");
                    break;
            }
        }

        EnsureCarriersComplete(carriers, rates, errors);

        return new MarketplaceShipmentTariffParseResult(
            ResolveChannel(meta, errors),
            ReadMetaText(meta, "Version", errors),
            ReadMetaDecimal(meta, "VatRate", errors),
            ReadMetaDecimal(meta, "PostalServiceFeeRate", errors),
            ReadMetaOptionalInt(meta, "ConditionalMaxDesi", errors),
            carriers,
            rates,
            conditionals,
            errors);
    }

    private static void ParseCarrier(
        string[] cells, int lineNo, List<MarketplaceShipmentTariffCarrierRow> carriers, List<string> errors)
    {
        if (cells.Length < 6)
        {
            errors.Add($"satır {lineNo}: [CARRIER] 6 kolon bekliyor, {cells.Length} geldi.");
            return;
        }

        if (!Enum.TryParse<ShipmentChargeBasis>(cells[2].Trim(), ignoreCase: true, out var basis))
        {
            errors.Add($"satır {lineNo}: ChargeBasis çözülemedi ('{cells[2].Trim()}').");
            return;
        }

        if (!TryParseAmount(cells[3], out var overflow) ||
            !TryParseAmount(cells[4], out var failedRate) ||
            !TryParseAmount(cells[5], out var extraFee))
        {
            errors.Add($"satır {lineNo}: taşıyıcı sayısal alanları çözülemedi.");
            return;
        }

        carriers.Add(new MarketplaceShipmentTariffCarrierRow(
            cells[0].Trim(), cells[1].Trim(), basis, overflow, failedRate, extraFee));
    }

    private static void ParseDesiRow(
        string[] cells,
        int lineNo,
        List<string> columns,
        Dictionary<string, Dictionary<int, decimal>> rates,
        List<string> errors)
    {
        if (columns.Count == 0)
        {
            errors.Add($"satır {lineNo}: [DESI] başlık satırı okunmadan veri geldi.");
            return;
        }

        if (!int.TryParse(cells[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var desi))
        {
            errors.Add($"satır {lineNo}: desi değeri sayı değil ('{cells[0].Trim()}').");
            return;
        }

        if (cells.Length - 1 != columns.Count)
        {
            errors.Add($"satır {lineNo}: desi {desi} için {columns.Count} fiyat bekleniyordu, {cells.Length - 1} geldi.");
            return;
        }

        for (var c = 0; c < columns.Count; c++)
        {
            if (!TryParseAmount(cells[c + 1], out var amount))
            {
                errors.Add($"satır {lineNo}: desi {desi} · {columns[c]} fiyatı çözülemedi ('{cells[c + 1].Trim()}').");
                continue;
            }

            if (!rates.TryGetValue(columns[c], out var byDesi))
            {
                byDesi = new Dictionary<int, decimal>();
                rates[columns[c]] = byDesi;
            }

            if (!byDesi.TryAdd(desi, amount))
            {
                errors.Add($"satır {lineNo}: desi {desi} · {columns[c]} birden fazla kez tanımlı.");
            }
        }
    }

    private static void ParseConditionalRow(
        string[] cells,
        int lineNo,
        List<string> columns,
        List<MarketplaceShipmentTariffConditionalRow> conditionals,
        List<string> errors)
    {
        if (columns.Count == 0)
        {
            errors.Add($"satır {lineNo}: [CONDITIONAL] başlık satırı okunmadan veri geldi.");
            return;
        }

        if (cells.Length - 2 != columns.Count)
        {
            errors.Add($"satır {lineNo}: barem için {columns.Count} tutar bekleniyordu, {cells.Length - 2} geldi.");
            return;
        }

        if (!TryParseAmount(cells[0], out var basketFrom))
        {
            errors.Add($"satır {lineNo}: BasketFrom çözülemedi ('{cells[0].Trim()}').");
            return;
        }

        decimal? basketTo = null;
        if (cells[1].Trim().Length > 0)
        {
            if (!TryParseAmount(cells[1], out var upper))
            {
                errors.Add($"satır {lineNo}: BasketTo çözülemedi ('{cells[1].Trim()}').");
                return;
            }

            basketTo = upper;
        }

        for (var c = 0; c < columns.Count; c++)
        {
            if (!TryParseAmount(cells[c + 2], out var amount))
            {
                errors.Add($"satır {lineNo}: barem {columns[c]} tutarı çözülemedi ('{cells[c + 2].Trim()}').");
                continue;
            }

            conditionals.Add(new MarketplaceShipmentTariffConditionalRow(columns[c], basketFrom, basketTo, amount));
        }
    }

    /// <summary>Her taşıyıcının desi tablosu 0..<c>TabulatedMaxDesi</c> aralığında EKSİKSİZ mi — boşluk varsa
    /// seed durur (eksik satır sessizce "fiyat yok" demek olurdu).</summary>
    private static void EnsureCarriersComplete(
        List<MarketplaceShipmentTariffCarrierRow> carriers,
        Dictionary<string, Dictionary<int, decimal>> rates,
        List<string> errors)
    {
        if (carriers.Count == 0)
        {
            errors.Add("[CARRIER] bölümünde hiç taşıyıcı yok.");
            return;
        }

        foreach (var carrier in carriers)
        {
            if (!rates.TryGetValue(carrier.Code, out var byDesi))
            {
                errors.Add($"taşıyıcı '{carrier.Code}' için [DESI] kolonu yok.");
                continue;
            }

            var missing = Enumerable
                .Range(MarketplaceShipmentTariffConsts.DocumentDesi, MarketplaceShipmentTariffConsts.TabulatedMaxDesi + 1)
                .Where(d => !byDesi.ContainsKey(d))
                .ToList();

            if (missing.Count > 0)
            {
                errors.Add($"taşıyıcı '{carrier.Code}' desi satırları eksik: {string.Join(",", missing.Take(10))}" +
                           (missing.Count > 10 ? $" (+{missing.Count - 10} daha)" : string.Empty));
            }
        }

        var orphanColumns = rates.Keys.Where(k => carriers.All(c => c.Code != k)).ToList();
        foreach (var orphan in orphanColumns)
        {
            errors.Add($"[DESI] kolonu '{orphan}' hiçbir taşıyıcıyla eşleşmiyor.");
        }
    }

    private static SalesChannelType ResolveChannel(Dictionary<string, string> meta, List<string> errors)
    {
        if (!meta.TryGetValue("Channel", out var raw))
        {
            errors.Add("[META] Channel eksik.");
            return default;
        }

        if (!Enum.TryParse<SalesChannelType>(raw, ignoreCase: true, out var channel))
        {
            errors.Add($"[META] Channel çözülemedi ('{raw}').");
            return default;
        }

        return channel;
    }

    private static string ReadMetaText(Dictionary<string, string> meta, string key, List<string> errors)
    {
        if (meta.TryGetValue(key, out var value) && value.Length > 0)
        {
            return value;
        }

        errors.Add($"[META] {key} eksik.");
        return string.Empty;
    }

    private static decimal ReadMetaDecimal(Dictionary<string, string> meta, string key, List<string> errors)
    {
        if (!meta.TryGetValue(key, out var raw))
        {
            errors.Add($"[META] {key} eksik.");
            return 0m;
        }

        if (!TryParseAmount(raw, out var value))
        {
            errors.Add($"[META] {key} çözülemedi ('{raw}').");
            return 0m;
        }

        return value;
    }

    private static int? ReadMetaOptionalInt(Dictionary<string, string> meta, string key, List<string> errors)
    {
        if (!meta.TryGetValue(key, out var raw) || raw.Length == 0)
        {
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"[META] {key} çözülemedi ('{raw}').");
            return null;
        }

        return value;
    }

    /// <summary>INVARIANT ondalık (nokta) bekler. Binlik ayırıcı KABUL EDİLMEZ — kaynakta virgülün ondalık mı
    /// binlik mi olduğu belirsiz olabildiğinden, o belirsizlik TSV üretiminde çözülür, burada tolere edilmez.</summary>
    private static bool TryParseAmount(string raw, out decimal value)
    {
        return decimal.TryParse(
            raw.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>TSV'den okunan taşıyıcı başlığı (fiyat satırları ayrı sözlükte).</summary>
public sealed record MarketplaceShipmentTariffCarrierRow(
    string Code,
    string Name,
    ShipmentChargeBasis ChargeBasis,
    decimal OverflowIncrement,
    decimal FailedDeliveryRate,
    decimal ExtraFee);

/// <summary>TSV'den okunan tek barem satırı (taşıyıcı × sepet dilimi).</summary>
public sealed record MarketplaceShipmentTariffConditionalRow(
    string CarrierCode,
    decimal BasketFrom,
    decimal? BasketTo,
    decimal Amount);

/// <summary>Parse sonucu. <see cref="Errors"/> doluysa seed BAŞLATILMAZ.</summary>
public sealed record MarketplaceShipmentTariffParseResult(
    SalesChannelType Channel,
    string Version,
    decimal VatRate,
    decimal PostalServiceFeeRate,
    int? ConditionalMaxDesi,
    IReadOnlyList<MarketplaceShipmentTariffCarrierRow> Carriers,
    IReadOnlyDictionary<string, Dictionary<int, decimal>> Rates,
    IReadOnlyList<MarketplaceShipmentTariffConditionalRow> ConditionalRates,
    IReadOnlyList<string> Errors);
