using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// FİYAT BANDI (SIRADA-5) — kanal-ürünün <c>[MinPrice, MaxPrice]</c> bandı dışına düşen fiyatta push'un
/// FAIL-CLOSED durduğunu kilitler.
///
/// <para><b>Neden gerekli:</b> repricing motoru 15 dakikada bir, İNSANSIZ fiyat yazıyor. Girdilerinden biri
/// (kur, maliyet, marj, komisyon) bozulursa sonuç yanlış bir sayıdır — ama hâlâ geçerli bir sayıdır: hiçbir
/// katman onu reddetmez ve maliyetin altına satış sessizce başlar. Bant, bu zincirin son emniyetidir.</para>
///
/// <para><b>Neden KIRPMA yok:</b> bandı ihlal eden fiyatı sınıra çekmek, motorun ürettiği yanlış sayıyı meşru
/// bir fiyata dönüştürüp hatayı GİZLERDİ. Kursuz birime uydurma kur yazmama kararıyla aynı felsefe: eksik/bozuk
/// veri sayıya değil, DURUŞA çevrilir.</para>
/// </summary>
public abstract class SalesChannelTrN11ProductPriceGuardTests<TStartupModule> : SalesChannelTrN11ProductPushTests<TStartupModule>
    where TStartupModule : IAbpModule
{
    /// <summary>(a) Fiyat TABANIN altında → push durur, N11'e HİÇ istek gitmez.
    /// "İstek gitmedi" iddiası şart: guard sadece hata fırlatıp isteği zaten yollamış olsaydı, yanlış fiyat
    /// pazaryerine ulaşır ve hata mesajı yalnız bir teselli olurdu.</summary>
    [Fact]
    public async Task A_price_below_the_floor_stops_the_push_before_any_request()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND1", greenPrice: 150m, greenStock: 5);
            await SetPriceBandAsync(created.Id, min: 200m, max: null);

            var batchesBefore = _restClient.CreatedBatches.Count;

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToN11Async(created.Id));

            ex.Code.ShouldBe("TradeXpress:SalesChannel:Product:PriceOutOfBand");
            _restClient.CreatedBatches.Count.ShouldBe(batchesBefore);
        }
    }

    /// <summary>Hafif senkron dalı da AYNI guard'dan geçer — tam push korunup senkron açık kalsaydı, insansız
    /// yolun tamamı (repricing → senkron) guard'ı baypas ederdi ve bant yalnız elle push'ta işe yarardı.</summary>
    [Fact]
    public async Task The_light_sync_path_is_guarded_too()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND2", greenPrice: 150m, greenStock: 5);
            await _appService.PushToN11Async(created.Id);      // SKU'lar dondu (senkron ön koşulu)
            await SetPriceBandAsync(created.Id, min: 200m, max: null);

            var batchesBefore = _restClient.PriceStockBatches.Count;

            await Should.ThrowAsync<BusinessException>(() => _appService.SyncStockAndPriceAsync(created.Id));

            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore);

            // Hata operatöre GÖRÜNÜR olmalı: kanal formundaki LastError dolar (istisna yutulmaz).
            var reloaded = await _appService.GetAsync(created.Id);
            reloaded.LastError.ShouldNotBeNullOrEmpty();
        }
    }

    /// <summary>(a') Fiyat TAVANIN üstünde → aynı guard. Aşırı yüksek fiyat satmaz ama listelemeyi bozar ve
    /// genelde bir hesap hatasının işaretidir; sessizce göndermek onu "normal" yapardı.</summary>
    [Fact]
    public async Task A_price_above_the_ceiling_stops_the_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND3", greenPrice: 150m, greenStock: 5);
            await SetPriceBandAsync(created.Id, min: null, max: 120m);

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToN11Async(created.Id));

            ex.Code.ShouldBe("TradeXpress:SalesChannel:Product:PriceOutOfBand");
        }
    }

    /// <summary>(b) Bant İÇİNDEKİ fiyat geçer — guard'ın meşru işi engellemediği de pinli
    /// (yalnız "durduruyor mu" diye sormak, her şeyi durduran bir guard'ı da yeşil gösterirdi).</summary>
    [Fact]
    public async Task A_price_inside_the_band_passes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND4", greenPrice: 150m, greenStock: 5);
            await SetPriceBandAsync(created.Id, min: 50m, max: 500m);

            var pushed = await _appService.PushToN11Async(created.Id);

            pushed.LastError.ShouldBeNull();
            _restClient.LastCreatedRows.ShouldNotBeEmpty();
        }
    }

    /// <summary>(d) Bant TANIMSIZKEN davranış değişmez — regresyon testi (canlıdaki tüm kayıtlar bugün böyle).</summary>
    [Fact]
    public async Task Without_a_band_nothing_changes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND5", greenPrice: 150m, greenStock: 5);

            var pushed = await _appService.PushToN11Async(created.Id);

            pushed.LastError.ShouldBeNull();
            _restClient.LastCreatedRows.Count.ShouldBe(3);
        }
    }

    /// <summary>(c) <c>min &gt; max</c> KAYDEDİLEMEZ. Kaydedilseydi hiçbir fiyat bandı geçemez ve ürün, sebebi
    /// görünmeyen bir şekilde sonsuza kadar push edilemez hâle gelirdi.</summary>
    [Fact]
    public async Task An_inverted_band_is_rejected_on_save()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "BAND6", greenPrice: 150m, greenStock: 5);

            var ex = await Should.ThrowAsync<BusinessException>(() => SetPriceBandAsync(created.Id, min: 500m, max: 100m));

            ex.Code.ShouldBe("TradeXpress:SalesChannel:Product:PriceBandInverted");
        }
    }

    private async Task SetPriceBandAsync(Guid id, decimal? min, decimal? max)
    {
        var dto = await _appService.GetAsync(id);
        var update = BuildUpdateDto(dto);
        update.MinPrice = min;
        update.MaxPrice = max;
        await _appService.UpdateAsync(id, update);
    }
}
