using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public abstract class EffectivePriceAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IEffectivePriceAppService _appService;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentTenant _currentTenant;

    protected EffectivePriceAppServiceTests()
    {
        _appService = GetRequiredService<IEffectivePriceAppService>();
        _unitRepository = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _countryRepository = GetRequiredService<IRepository<Country, Guid>>();
        _companyRepository = GetRequiredService<IRepository<Company, Guid>>();
        _rateRepository = GetRequiredService<IRepository<ExchangeRate, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Host_current_prices_include_seeded_TRY_at_one()
    {
        var prices = await _appService.GetCurrentPricesAsync();

        // Seed yalnız TRY için host ham ExchangeRate (1/1) yazar → en az TRY gelir.
        var tryPrice = prices.SingleOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
        tryPrice.ShouldNotBeNull();

        // TRY margin = FinalPrice(1) → host efektifi 1/1 (ham 1/1 üstüne).
        tryPrice!.Buy.ShouldBe(1m);
        tryPrice.Sell.ShouldBe(1m);
        tryPrice.RawBuy.ShouldBe(1m);
        tryPrice.GuardFired.ShouldBeFalse();
    }

    [Fact]
    public async Task Units_without_a_raw_rate_are_flagged_not_priced()
    {
        // Tasarım (2026-08-05): fiyat kaynağı (feed/rate/takip) olmayan birim GÖSTERİM listesinde BOŞ geçmez —
        // ham 1/1 ile görünür — AMA o 1/1 bir kur DEĞİL, yer tutucudur ve RateMissing ile İŞARETLENİR.
        // Seed yalnız TRY'ye ham rate yazar.
        var prices = await _appService.GetCurrentPricesAsync();

        // USD seed'li ama ham rate'i yok → omit DEĞİL; ham 1/1 ile listede yer alır (marjdan bağımsız).
        var usd = prices.SingleOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.USD);
        usd.ShouldNotBeNull();
        usd!.RawBuy.ShouldBe(1m);
        usd.RawSell.ShouldBe(1m);

        // ⚠ ASIL KURAL: yer tutucu, kurdan AYIRT EDİLEBİLİR olmalı. Bu bayrak düşerse UI 1/1'i gerçek kur
        // gibi basar — 2026-08-05'te tam bu oldu: HAS kursuz olduğu halde panoda "1" göründü ve 7 gram has
        // altın reçetede "7,00 TRY" olarak fiyatlandı.
        usd.RateMissing.ShouldBeTrue();

        // Kuru OLAN birim işaretlenmez (bayrak "her şeye true" diye geçmesin).
        prices.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY).RateMissing.ShouldBeFalse();
    }

    [Fact]
    public async Task Valuation_excludes_units_without_a_rate()
    {
        // DEĞERLEME sözleşmesi: kuru olmayan birim sözlüğe HİÇ girmez. Aşağı akış bunu zaten "kur yok" diye
        // okur (PositionReportAppService 'MissingRate = val == null'; ProductRecipeCostCalculator satırı
        // MissingRate işaretleyip net toplama almaz). Uydurma 1/1 sözlüğe girerse o fail-fast ağı BAYPAS olur
        // ve reçete/pozisyon/bilanço sessizce yanlış rakam üretir — bu testin koruduğu şey budur.
        var valuation = await _appService.GetValuationAsync();   // companyId yok → HQ (base = TRY)

        valuation.ShouldNotContain(p => p.CurrencyUnitCode == CurrencyUnitCode.USD);

        // Kuru olan birim elenmez — filtre "hepsini at"a dönüşmesin.
        valuation.ShouldContain(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
    }

    [Fact]
    public async Task Valuation_with_TR_headquarters_is_identity()
    {
        // HQ = TR/TRY → base=TRY; TRY efektifi 1/1, kendi base'ine re-base → 1/1 (identity).
        var valuation = await _appService.GetValuationAsync(); // companyId yok → HQ

        var tryV = valuation.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
        tryV.Buy.ShouldBe(1m);
        tryV.Sell.ShouldBe(1m);
        tryV.BaseCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
    }

    [Fact]
    public async Task Foreign_company_rebases_the_board_to_its_own_local_currency()
    {
        // YABANCI ŞİRKET YOLU (Hakan 2026-08-05: "yabancı şirket için de rebase edildiğine emin ol").
        // Feed TRY-quote'tur (pivot = TRY); ABD şirketinde pano YEREL paraya (USD) re-base edilmelidir.
        // Konvansiyon: her satır "1 birim = X yerel" → yerel satır 1.00, TRY satırı 1/USD.
        const decimal usdRate = 40m;
        var usdId = await SeedHostRateForAsync(CurrencyUnitCode.USD, usdRate);
        var companyId = await SeedForeignCompanyAsync("USA", usdId);

        using (_currentCompany.Change(companyId))
        {
            var prices = await _appService.GetCurrentPricesAsync();

            // Yerel birim daima identity — marj alamaz (host TRY üzerinden gider).
            var usd = prices.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.USD);
            usd.Buy.ShouldBe(1m);
            usd.Sell.ShouldBe(1m);

            // Pivot satırı yerele bölünmüş olmalı: 1 TRY = 1/40 = 0.025 USD. Re-base ATLANIRSA 1.00 kalırdı.
            var tryRow = prices.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
            tryRow.Buy.ShouldBe(1m / usdRate);
            tryRow.Sell.ShouldBe(1m / usdRate);
        }
    }

    [Fact]
    public async Task Foreign_company_with_an_unpriced_local_currency_does_not_fake_a_rebase()
    {
        // ⚠ SESSİZ YANLIŞ ETİKET KORUMASI (2026-08-05). Kuru olmayan birim motorda 1/1 YER TUTUCU alır ve o
        // yer tutucu "Buy > 0 && Sell > 0" kontrolünden GEÇER. Bayrak okunmazsa her satır 1'e bölünür ve pano
        // PİVOT (TRY) rakamlarını YEREL paraymış gibi gösterir — kuru olmayan USD'li şirkette "HAS = 6458"
        // USD sanılırdı (gerçekte TRY). Re-base mümkün değilse UYDURULMAZ: pivot görüntüye düşülür.
        var usdId = await ResolveUnitIdAsync(CurrencyUnitCode.USD);   // seed USD'ye ham kur YAZMAZ
        var companyId = await SeedForeignCompanyAsync("NOR", usdId);

        using (_currentCompany.Change(companyId))
        {
            var prices = await _appService.GetCurrentPricesAsync();

            // Yerelin kuru yok → işaretli olmalı (yer tutucu kur sanılmasın).
            prices.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.USD).RateMissing.ShouldBeTrue();

            // Pivot görüntüye düşüldü: TRY 1.00 (pivot identity), yerele bölünmüş DEĞİL.
            prices.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY).Buy.ShouldBe(1m);
        }
    }

    // ── fixture yardımcıları ────────────────────────────────────────────────────────────────────────

    private Task<Guid> ResolveUnitIdAsync(string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var unit = await _unitRepository.FirstOrDefaultAsync(u => u.Code == code);
            unit.ShouldNotBeNull();
            return unit!.Id;
        });
    }

    /// <summary>Birime HOST (TenantId=null) ham kur yazar — motor ham kurları yalnız host satırından okur.</summary>
    private async Task<Guid> SeedHostRateForAsync(string code, decimal rate)
    {
        var unitId = await ResolveUnitIdAsync(code);
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(null))
            {
                await _rateRepository.InsertAsync(
                    new ExchangeRate(
                        currencyUnitId: unitId,
                        marketPriceOnBuy: rate,
                        marketPriceOnSell: rate,
                        appliedMarginOnBuy: MarginSetting.Passthrough,
                        appliedMarginOnSell: MarginSetting.Passthrough,
                        source: "Test",
                        rateDate: DateTime.UtcNow),
                    autoSave: true);
            }
        });
        return unitId;
    }

    /// <summary>Yerel parası <paramref name="localUnitId"/> olan bir ülkeye bağlı şirket kurar —
    /// <c>LocalCurrencyResolver</c> yerel kodu şirketin ÜLKESİNDEN çözer (bilanço biriminden DEĞİL).</summary>
    private Task<Guid> SeedForeignCompanyAsync(string prefix, Guid localUnitId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var country = await _countryRepository.InsertAsync(
                new Country(prefix[..2], $"{prefix} Country", localUnitId), autoSave: true);

            var company = await _companyRepository.InsertAsync(
                new Company($"{prefix}CO", $"{prefix} Company", country.Id, localUnitId, isHeadquarters: false),
                autoSave: true);

            return company.Id;
        });
    }
}
