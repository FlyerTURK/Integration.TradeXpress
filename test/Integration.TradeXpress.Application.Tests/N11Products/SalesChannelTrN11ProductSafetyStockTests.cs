using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// EMNİYET PAYI (E-6) — kanal-ürün başına "kanalda gösterme" payının push kenarında uygulandığını kilitler.
///
/// <para><b>Neden gerekli:</b> aşırı satış savunmasının mevcut üç katmanı (<c>bundle=false</c> · stok bitince
/// adet-0 · sipariş rezervasyonu) AYNI AN çakışmasını kapatır. Kapatmadığı pencere şudur: senkron turları
/// arasında (dakikalar) kanal, elimizde gerçekte olandan fazla adet göstermeye devam eder. Pay bu pencereyi
/// daraltır — bilerek az gösterir.</para>
///
/// <para><b>Sessiz kırılma riski:</b> pay tek bir çıkışta (<c>BuildPushRowsAsync</c>) uygulanmazsa, çağıranlardan
/// biri paylı diğeri paysız değeri görür. O anda hiçbir hata çıkmaz; yalnız dirty-check her turda "değişti" der ve
/// N11'e sonsuza kadar aynı ürün yazılır (kota yakılır) — ya da tam tersi, pay hiç uygulanmaz ve savunma
/// kâğıt üstünde kalır. Bu yüzden ÜÇ çağıranın da (önizleme · tam push · hafif senkron) aynı sayıyı görmesi pinli.</para>
/// </summary>
public abstract class SalesChannelTrN11ProductSafetyStockTests<TStartupModule> : SalesChannelTrN11ProductPushTests<TStartupModule>
    where TStartupModule : IAbpModule
{
    /// <summary>(a) Pay satılabilir adetten DÜŞÜLÜR — önizleme, tam push ve hafif senkron AYNI sayıyı görür.</summary>
    [Fact]
    public async Task Safety_stock_is_subtracted_on_every_push_surface()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // Green (N11-only) satırı 8 adet; ERP satırları Red=10, Blue=20.
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SAFE1", greenPrice: 150m, greenStock: 8);
            await SetSafetyStockAsync(created.Id, 5);

            var preview = await _appService.GetPushPreviewAsync(created.Id);
            preview.Variants.Single(v => !v.IsErpBacked).StockQuantity.ShouldBe(3);   // 8 − 5
            preview.Variants.Single(v => v.Code == "RED").StockQuantity.ShouldBe(5);  // 10 − 5 (ERP satırı da paylı)
            preview.Variants.Single(v => v.Code == "BLUE").StockQuantity.ShouldBe(15);

            await _appService.PushToN11Async(created.Id);

            var rows = _restClient.LastCreatedRows;
            rows.Single(r => r.StockCode.StartsWith("GREEN", StringComparison.Ordinal)).Quantity.ShouldBe(3);
            rows.Single(r => r.StockCode.StartsWith("BLUE", StringComparison.Ordinal)).Quantity.ShouldBe(15);
        }
    }

    /// <summary>(b) Pay satılabilir adetten BÜYÜKSE sonuç 0'dır — negatif adet ASLA üretilmez.
    /// Negatif gitseydi N11 satırı reddeder, push topluca düşer ve sebebi bizim tarafımızda görünmezdi.</summary>
    [Fact]
    public async Task Safety_stock_larger_than_the_available_quantity_floors_at_zero()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SAFE2", greenPrice: 150m, greenStock: 8);
            await SetSafetyStockAsync(created.Id, 50);

            var preview = await _appService.GetPushPreviewAsync(created.Id);

            preview.Variants.Select(v => v.StockQuantity).ShouldAllBe(q => q == 0);
        }
    }

    /// <summary>(c) Pay YOKKEN bugünkü değerler bit-bit aynı — regresyon testi. Yeni bir alan eklemenin en
    /// sinsi yan etkisi, alanı hiç kullanmayan kayıtların davranışını değiştirmesidir.</summary>
    [Fact]
    public async Task Without_a_safety_stock_the_quantities_are_unchanged()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SAFE3", greenPrice: 150m, greenStock: 8);

            var preview = await _appService.GetPushPreviewAsync(created.Id);

            preview.Variants.Single(v => !v.IsErpBacked).StockQuantity.ShouldBe(8);
            preview.Variants.Single(v => v.Code == "RED").StockQuantity.ShouldBe(10);
            preview.Variants.Single(v => v.Code == "BLUE").StockQuantity.ShouldBe(20);
        }
    }

    /// <summary>(d) LastSent* PAYLI değeri saklar → ikinci senkron "değişiklik yok" der.
    ///
    /// <para>Ham (paysız) adet saklansaydı dirty-check her turda fark görür ve hiçbir şey değişmemiş ürün
    /// N11'e sonsuza kadar yeniden yazılırdı: kota yanar, 60 sn kuralı zorlanır, log şişer — ve hiçbiri
    /// hata olarak görünmez.</para></summary>
    [Fact]
    public async Task The_margined_quantity_is_remembered_so_the_next_sync_is_a_no_op()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SAFE4", greenPrice: 150m, greenStock: 8);
            await SetSafetyStockAsync(created.Id, 5);
            await _appService.PushToN11Async(created.Id);

            var firstSync = await _appService.SyncStockAndPriceAsync(created.Id);
            var batchesAfterFirst = _restClient.PriceStockBatches.Count;

            // Hatırlanan değer PAYLI olmalı — ham 8 değil 3. Ham değer saklansaydı test yine "no-op" görürdü
            // (push de ham gönderirdi), o yüzden sayının KENDİSİ ayrıca iddia edilir.
            firstSync.LastError.ShouldBeNull();
            firstSync.Skus.Single(s => s.SellerStockCode.StartsWith("GREEN", StringComparison.Ordinal))
                .LastSentQuantity.ShouldBe(3);

            var secondSync = await _appService.SyncStockAndPriceAsync(created.Id);

            // İkinci turda N11'e HİÇ istek gitmemeli.
            _restClient.PriceStockBatches.Count.ShouldBe(batchesAfterFirst);
            secondSync.SyncWarnings.ShouldNotBeEmpty();
        }
    }

    /// <summary>Negatif pay REDDEDİLİR — kaydedilseydi stoğu ŞİŞİRİRDİ; yani emniyet alanı, tam da önlemesi
    /// gereken şeyin (aşırı satış) aracına dönüşürdü.</summary>
    [Fact]
    public async Task A_negative_safety_stock_is_rejected()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SAFE5", greenPrice: 150m, greenStock: 8);

            await Should.ThrowAsync<Volo.Abp.BusinessException>(() => SetSafetyStockAsync(created.Id, -1));
        }
    }

    private async Task SetSafetyStockAsync(Guid id, int? safetyStock)
    {
        var dto = await _appService.GetAsync(id);
        var update = BuildUpdateDto(dto);
        update.SafetyStock = safetyStock;
        await _appService.UpdateAsync(id, update);
    }
}
