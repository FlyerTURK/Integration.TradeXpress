using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// PASİF KANAL ÜRÜNÜ FİYAT/STOK YAZIMI ALMAZ (2026-08-21) — Trendyol'da zaten çalışan iki savunmanın N11
/// karşılığını kilitler: <c>N11ChannelStockPusher</c>'ın <c>IsActive</c> süzgeci + <c>SyncStockAndPriceAsync</c>
/// girişindeki guard (<c>TradeXpress:N11:Product:PassiveNoSync</c>).
///
/// <para><b>Neden gerekliydi (2026-08-21 ölçümü):</b> N11'de iki savunma da YOKTU
/// (<c>TrendyolChannelStockPusher</c> <c>.Where(... &amp;&amp; p.IsActive)</c> diyordu, N11 ikizi demiyordu).
/// Ürün pasife alınınca <c>SalesChannelTrN11ProductRemover.DeactivateForProductAsync</c> kanal kaydının
/// bayrağını düşürüyor ama 15 dakikalık repricing turu (<c>ProductStockSyncJob</c> → pusher) o kayda fiyat/stok
/// yazmaya DEVAM ediyordu. Ne hata, ne uyarı, ne log: kullanıcı ürünü "kaldırdım" sanıyor, N11 listelemesindeki
/// adet her turda tazeleniyor ve durum ancak o listelemeden SİPARİŞ gelince ortaya çıkıyordu. Kullanıcı
/// açısından en pahalı sessiz hata sınıfı budur — sonucu ancak karşılanamayacak bir sipariş olarak görünür.</para>
///
/// <para><b>Bu testlerin İDDİA ETMEDİĞİ şey:</b> pasifleşme ANININ adet-0 gönderimi (2026-08-21 Hakan kararı,
/// <c>N11StockWithdrawer</c>) ayrı sınıfta kilitlidir: <c>SalesChannelTrN11ProductZeroStockOnDeactivateTests</c>.
/// Burada kilitlenen tek şey: pasif kayda <b>bizden yeni yazım gitmez</b> (guard + tetik süzgeci).</para>
/// </summary>
public abstract class SalesChannelTrN11ProductPassiveSyncTests<TStartupModule>
    : SalesChannelTrN11ProductPushTests<TStartupModule>
    where TStartupModule : IAbpModule
{
    /// <summary>(a) Elle "Senkronla": pasif kayıt TİPLİ hatayla reddedilir ve N11'e hiçbir satır gitmez.
    ///
    /// <para>Reddediş biçimi Trendyol ikiziyle aynı sınıftadır — sessiz atlama DEĞİL <c>BusinessException</c>:
    /// kullanıcı butona bastığında neden hiçbir şey olmadığını görmeli.</para></summary>
    [Fact]
    public async Task A_passive_channel_product_is_rejected_by_the_light_sync()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PASV1", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);

            // Kayıt hem PASİF hem KİRLİ olmalı. Yalnız pasifleştirip "gönderim yok" demek YALANCI-YEŞİL olurdu:
            // push'tan hemen sonra dirty-check zaten "değişiklik yok" diyeceği için guard kaldırılsa bile test
            // geçerdi. Emniyet payı adedi düşürür → guard olmasaydı bu senkron gerçekten yazardı.
            await MutateAsync(created.Id, u => { u.IsActive = false; u.SafetyStock = 5; });
            var batchesBefore = _restClient.PriceStockBatches.Count;

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _appService.SyncStockAndPriceAsync(created.Id));

            exception.Code.ShouldBe("TradeXpress:N11:Product:PassiveNoSync");
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore);
        }
    }

    /// <summary>(b) 15 dakikalık stok turunun N11 ayağı pasif kaydı ATLAR — uçtan uca iddia: pasif kanal
    /// ürününe N11'e giden TEK bir istek bile yoktur.
    ///
    /// <para><b>Kontrol kolu neden var:</b> "hiç istek gitmedi" iddiası tek başına hiçbir şey kanıtlamaz —
    /// hep boş dönen bir sorgu da sessizdir. Önce AKTİF + kirli kayıtta turun GERÇEKTEN yazdığı gösterilir,
    /// sonra aynı kayıt pasifleştirilip yeniden kirletilir.</para>
    ///
    /// <para>İki savunma da (süzgeç + guard) bu sonucu tek başına üretebilir; test kasten uçtan uca sonucu
    /// kilitler — biri kaldırılırsa diğeri hâlâ tutar, ikisi birden kaldırılırsa KIRMIZI olur.</para></summary>
    [Fact]
    public async Task The_stock_trigger_skips_a_passive_channel_product()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PASV2", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);

            // Job'ın gördüğü composite değil, N11 ÜYESİ — iddia tek kanala ait olmalı.
            var n11Pusher = ServiceProvider.GetServices<IChannelStockPusherMember>()
                .Single(m => m.ChannelName == "N11");

            // KONTROL: aktif + kirli kayıtta tur gerçekten yazar.
            await MutateAsync(created.Id, u => u.SafetyStock = 5);
            var batchesBeforeActiveRun = _restClient.PriceStockBatches.Count;

            await n11Pusher.PushProductAsync(created.ProductId);

            _restClient.PriceStockBatches.Count.ShouldBe(batchesBeforeActiveRun + 1);

            // Ürün pasife alındı (üretimde bunu SalesChannelTrN11ProductRemover.DeactivateForProductAsync yapar)
            // ve kayıt YENİDEN kirletildi: savunma olmasaydı tur bir kez daha yazardı.
            await MutateAsync(created.Id, u => { u.IsActive = false; u.SafetyStock = 7; });
            var batchesBeforePassiveRun = _restClient.PriceStockBatches.Count;

            // Üye hatayı YUTAR (bir kanalın arızası job'ı düşürmemeli) → bu çağrı fırlatmaz; iddia trafiktedir.
            await n11Pusher.PushProductAsync(created.ProductId);

            _restClient.PriceStockBatches.Count.ShouldBe(batchesBeforePassiveRun);
        }
    }

    /// <summary>Kaydı GERÇEK güncelleme yolundan değiştirir (entity'ye elle dokunmak, UpdateAsync'in taşıma
    /// sözleşmesini baypas ederdi).</summary>
    private async Task MutateAsync(Guid id, Action<SalesChannelTrN11ProductUpdateDto> mutate)
    {
        var dto = await _appService.GetAsync(id);
        var update = BuildUpdateDto(dto);
        mutate(update);
        await _appService.UpdateAsync(id, update);
    }
}
