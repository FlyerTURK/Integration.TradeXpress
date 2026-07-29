using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Pazaryeri anlaşmalı kargo tarifesi seed'i — <b>host-global</b> (TenantId=null) ve <b>idempotan</b>.
///
/// <para><b>Sürüm mantığı:</b> gömülü TSV bir YAYIN sürümüdür (<c>SourceVersion</c>, ör. "2026-07-26").
/// Aynı kanal+taşıyıcı için o sürüm zaten kuruluysa DOKUNULMAZ. Daha yeni bir sürüm gelirse yürürlükteki
/// satır KAPATILIR (<c>EffectiveTo</c>) ve yenisi açılır — <b>eski sürüm SİLİNMEZ</b>, çünkü geçmiş bir
/// siparişin kargo maliyeti kendi dönemindeki tarifeden doğrulanabilmeli.</para>
///
/// <para><b>Fail-fast:</b> TSV'de tek bir bozuk/eksik hücre varsa hiçbir şey yazılmaz. Yarım kurulmuş bir
/// tarife, eksik desi satırında sessizce "fiyat yok" demek olurdu.</para>
///
/// <para>Tarife değişince yapılacak: yeni tarihli TSV'yi <c>EmbeddedResource</c> olarak ekle ve
/// <see cref="EmbeddedTariffFiles"/> listesine yaz — mevcut dosyaya DOKUNMA.</para>
/// </summary>
public class MarketplaceShipmentTariffSeeder(
    IRepository<MarketplaceShipmentTariff, Guid> tariffRepository,
    ICurrentTenant currentTenant,
    IClock clock,
    ILogger<MarketplaceShipmentTariffSeeder> logger)
    : ITransientDependency
{
    #region Fields

    /// <summary>Kurulacak yayın dosyaları (eskiden yeniye). Yeni tarife = bu listeye YENİ satır.</summary>
    private static readonly string[] EmbeddedTariffFiles =
    [
        "n11-shipment-tariff-2026-07-26.tsv",
    ];

    private readonly IRepository<MarketplaceShipmentTariff, Guid> _tariffRepository = tariffRepository;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IClock _clock = clock;
    private readonly ILogger<MarketplaceShipmentTariffSeeder> _logger = logger;

    #endregion

    #region Seeding

    /// <summary>Gömülü tarife yayınlarını host kataloğuna kurar. Yalnız host bağlamında anlamlıdır.</summary>
    public async Task SeedAsync()
    {
        // Tarife host-global (TenantId yok). Change(null) hem sorguyu hem yazımı host bağlamına sabitler.
        using (_currentTenant.Change(null))
        {
            foreach (var file in EmbeddedTariffFiles)
            {
                await SeedFileAsync(file);
            }
        }
    }

    private async Task SeedFileAsync(string fileName)
    {
        var parsed = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(fileName));

        if (parsed.Errors.Count > 0)
        {
            // Kısmi kurulum YOK: bozuk kaynak sessizce eksik tarife kurmaktan iyidir.
            _logger.LogError(
                "Kargo tarifesi '{File}' okunamadı, seed ATLANDI. İlk hatalar: {Errors}",
                fileName, string.Join(" | ", parsed.Errors.Take(5)));
            return;
        }

        var effectiveFrom = ResolveEffectiveFrom(parsed.Version);
        var existing = await _tariffRepository.GetListAsync(t => t.Channel == parsed.Channel);

        var added = 0;
        var closed = 0;

        foreach (var carrier in parsed.Carriers)
        {
            var alreadySeeded = existing.Any(t =>
                t.CarrierCode == carrier.Code && t.SourceVersion == parsed.Version);

            if (alreadySeeded)
            {
                continue;
            }

            closed += await CloseSupersededAsync(existing, carrier.Code, effectiveFrom);
            await InsertTariffAsync(parsed, carrier, effectiveFrom);
            added++;
        }

        if (added == 0 && closed == 0)
        {
            _logger.LogInformation(
                "Kargo tarifesi [{Channel} · {Version}]: zaten güncel, değişiklik yok.", parsed.Channel, parsed.Version);
            return;
        }

        _logger.LogInformation(
            "Kargo tarifesi [{Channel} · {Version}]: {Added} taşıyıcı kuruldu ({Rates} desi satırı), {Closed} eski sürüm kapatıldı.",
            parsed.Channel, parsed.Version, added,
            added * (MarketplaceShipmentTariffConsts.TabulatedMaxDesi + 1), closed);
    }

    /// <summary>Aynı taşıyıcının hâlâ açık olan eski sürümlerini kapatır (silmez).</summary>
    private async Task<int> CloseSupersededAsync(
        List<MarketplaceShipmentTariff> existing, string carrierCode, DateTime effectiveFrom)
    {
        var open = existing
            .Where(t => t.CarrierCode == carrierCode && t.EffectiveTo is null && t.EffectiveFrom < effectiveFrom)
            .ToList();

        foreach (var tariff in open)
        {
            tariff.Close(effectiveFrom.AddDays(-1));
            tariff.SetActive(false);
            await _tariffRepository.UpdateAsync(tariff, autoSave: true);
        }

        return open.Count;
    }

    private async Task InsertTariffAsync(
        MarketplaceShipmentTariffParseResult parsed,
        MarketplaceShipmentTariffCarrierRow carrier,
        DateTime effectiveFrom)
    {
        var tariff = new MarketplaceShipmentTariff(
            parsed.Channel,
            carrier.Code,
            carrier.Name,
            carrier.ChargeBasis,
            carrier.OverflowIncrement,
            effectiveFrom,
            parsed.Version);

        tariff.SetSurcharges(parsed.VatRate, parsed.PostalServiceFeeRate, carrier.ExtraFee);
        tariff.SetFailedDeliveryRate(carrier.FailedDeliveryRate);
        tariff.SetConditionalMaxDesi(parsed.ConditionalMaxDesi);

        // HeavyCargoAmount BİLİNÇLİ olarak set EDİLMEZ — kaynaktaki yazım belirsiz (bkz. entity notu).

        foreach (var (desi, amount) in parsed.Rates[carrier.Code].OrderBy(p => p.Key))
        {
            tariff.SetRate(desi, amount);
        }

        foreach (var row in parsed.ConditionalRates.Where(r => r.CarrierCode == carrier.Code))
        {
            tariff.AddConditionalRate(row.BasketFrom, row.BasketTo, row.Amount);
        }

        await _tariffRepository.InsertAsync(tariff, autoSave: true);
    }

    /// <summary>Sürüm etiketi ("2026-07-26") yürürlük başlangıcıdır; çözülemezse bugüne düşer.</summary>
    private DateTime ResolveEffectiveFrom(string version)
    {
        if (DateTime.TryParseExact(
                version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.Date;
        }

        _logger.LogWarning(
            "Tarife sürümü '{Version}' tarih olarak çözülemedi — yürürlük bugünden başlatıldı.", version);
        return _clock.Now.Date;
    }

    #endregion
}
