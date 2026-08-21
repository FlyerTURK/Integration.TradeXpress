using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.TrendyolProducts;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// TRENDYOL GÜNLÜK MUTABAKAT ÇÖZÜCÜSÜ (2026-08-21) — N11 ikizinin sözleşmesiyle aynı
/// (bkz. <see cref="N11ReconciliationResolverTests{TStartupModule}"/>): AKTİF kayıtta taban kanal gözlemine
/// çekilir, sapmasız taban değişmez, PASİF kayıtta yalnız log. Testler işçi şartında koşar (ambient şirket
/// YOK, ambient kullanıcı YOK) ve gerçek push zincirinden geçmiş SKU'lar üzerinde çalışır
/// (<see cref="TrendyolChannelProductTestSeeder"/> + <c>RecordSkuPush</c> tabanı).
/// </summary>
public abstract class TrendyolReconciliationResolverTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly TrendyolReconciliationResolver _resolver;
    private readonly TrendyolChannelProductTestSeeder _seeder;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly FakeTrendyolProductClient _client;

    protected TrendyolReconciliationResolverTests()
    {
        _resolver = GetRequiredService<TrendyolReconciliationResolver>();
        _seeder = GetRequiredService<TrendyolChannelProductTestSeeder>();
        _repository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _client = GetRequiredService<FakeTrendyolProductClient>();
    }

    /// <summary>AKTİF kayıtta kanal farklı adet/fiyat bildiriyorsa taban kanal gözlemine çekilir; aynı
    /// kaydın SAPMAYAN SKU'suna dokunulmaz (düzeltme SKU granülündedir, kayıt granülünde değil).</summary>
    [Fact]
    public async Task A_drifted_active_sku_baseline_is_pulled_to_the_channel_observation()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedWithBaselineAsync(companyId, "TYRC1", quantity: 10, listPrice: 100m, salePrice: 100m);

        var barcodes = (await LoadAsync(recordId)).Skus.Select(s => s.Barcode).OrderBy(b => b).ToList();
        SetRemote(
            (barcodes[0], 7, 100m, 95m),      // sapan: adet 10→7, satış 100→95
            (barcodes[1], 10, 100m, 100m));   // tabanla aynı

        var report = await RunWithoutAmbientContextAsync();

        report.SkippedNoAdmin.ShouldBeFalse("tenant admin bulunmalı — bulunamadıysa işçi yine sessiz kalır");
        report.CorrectedSkus.ShouldBeGreaterThanOrEqualTo(1);

        var skus = (await LoadAsync(recordId)).Skus.OrderBy(s => s.Barcode).ToList();
        skus[0].LastSentQuantity.ShouldBe(7);
        skus[0].LastSentSalePrice.ShouldBe(95m);
        skus[0].LastSentListPrice.ShouldBe(100m);
        skus[1].LastSentQuantity.ShouldBe(10);
        skus[1].LastSentSalePrice.ShouldBe(100m);
    }

    /// <summary>Kanal bizim tabanla AYNI değerleri bildiriyorsa hiçbir şey yazılmaz.</summary>
    [Fact]
    public async Task A_matching_sku_baseline_is_left_untouched()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedWithBaselineAsync(companyId, "TYRC2", quantity: 10, listPrice: 100m, salePrice: 100m);

        var barcodes = (await LoadAsync(recordId)).Skus.Select(s => s.Barcode).ToList();
        SetRemote(barcodes.Select(b => (b, 10, 100m, 100m)).ToArray());

        var report = await RunWithoutAmbientContextAsync();

        // Uzak haritada yalnız bu testin barkodları var → düzeltme sayacı sıfır olmak zorunda.
        report.CorrectedSkus.ShouldBe(0);

        var skus = (await LoadAsync(recordId)).Skus.ToList();
        skus.ShouldAllBe(s => s.LastSentQuantity == 10 && s.LastSentSalePrice == 100m);
    }

    /// <summary>PASİF kayıtta (kanalda arşiv beklenir) kanal hâlâ satılabilir adet gösteriyorsa YALNIZ
    /// loglanır: taban değişmez ki dirty-check tetiklenip otomatik push gitmesin — karar kullanıcıya aittir.</summary>
    [Fact]
    public async Task A_passive_record_drift_is_only_logged_and_the_baseline_stays()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedWithBaselineAsync(
            companyId, "TYRC3", quantity: 0, listPrice: 100m, salePrice: 100m, isActive: false);

        var barcodes = (await LoadAsync(recordId)).Skus.Select(s => s.Barcode).ToList();
        SetRemote((barcodes[0], 6, 100m, 100m));   // kanal hâlâ 6 adet gösteriyor

        var report = await RunWithoutAmbientContextAsync();

        report.PassiveDrifts.ShouldBeGreaterThanOrEqualTo(1);
        report.CorrectedSkus.ShouldBe(0);

        var skus = (await LoadAsync(recordId)).Skus.ToList();
        skus.ShouldAllBe(
            s => s.LastSentQuantity == 0,
            "pasif kayıtta taban oynatılsaydı bir sonraki tur otomatik push denerdi");
    }

    /// <summary>PROCESSING batch'li kayıt ATLANIR — batch çözücüsünün terfisiyle yarışılmaz. TOCTOU
    /// sertleştirmesi bu iddiayla pinli (taze entity üzerindeki yeniden-kontrol kaldırılırsa kırmızı).</summary>
    [Fact]
    public async Task A_record_with_a_processing_batch_is_skipped()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedWithBaselineAsync(companyId, "RCT4", quantity: 5, listPrice: 100m, salePrice: 100m);
        using (_currentCompany.Change(companyId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var entity = await _repository.GetAsync(recordId);
                entity.MarkSubmitted("batch-rct4", "updatePriceAndInventory", DateTime.UtcNow);
                await _repository.UpdateAsync(entity, autoSave: true);
            });
        }

        var barcodes = (await LoadAsync(recordId)).Skus.Select(b => b.Barcode).ToList();
        SetRemote(barcodes.Select(b => (b, 3, 90m, 90m)).ToArray());

        var report = await RunWithoutAmbientContextAsync();

        report.SkippedPending.ShouldBeGreaterThanOrEqualTo(1);
        (await LoadAsync(recordId)).Skus.ShouldAllBe(s2 => s2.LastSentQuantity == 5,
            "PROCESSING batch varken taban EZİLMEZ — uçuştaki gönderimle yarışılmaz.");
    }

    /// <summary>Kanalda HİÇ görünmeyen SKU'da taban KORUNUR (null'a çekilmez) — listeleme kaybı, değer
    /// sapmasından farklı bir durumdur ve kararı kullanıcıya bırakılır. Hakem bulgusu: missing dalı tabanı
    /// bozacak şekilde değişseydi hiçbir test kırmızı yanmıyordu.</summary>
    [Fact]
    public async Task A_sku_missing_from_the_channel_keeps_its_baseline()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedWithBaselineAsync(companyId, "RCT5", quantity: 7, listPrice: 110m, salePrice: 110m);
        SetRemote();   // kanal listelemesi BOŞ — hiçbir SKU bulunamayacak

        var report = await RunWithoutAmbientContextAsync();

        report.MissingSkus.ShouldBeGreaterThanOrEqualTo(1);
        (await LoadAsync(recordId)).Skus.ShouldAllBe(s2 => s2.LastSentQuantity == 7,
            "Kanalda görünmeyen SKU'nun tabanı null'a/0'a ÇEKİLMEZ — yalnız loglanır.");
    }

    // ── Fikstür ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanal + ürün + iki dondurulmuş SKU'lu kayıt kurar ve her SKU'nun tabanını (<c>LastSent*</c>)
    /// verilen değerlere çeker ("daha önce başarıyla push edilmiş" durumu). Pasif senaryo için bayrak düşürülür.</summary>
    private async Task<Guid> SeedWithBaselineAsync(
        Guid companyId, string productCode, int quantity, decimal listPrice, decimal salePrice, bool isActive = true)
    {
        Guid recordId;
        using (_currentCompany.Change(companyId))
        {
            var created = await _seeder.SeedAsync(companyId, productCode, verify: true, seedSkus: true);
            recordId = created.Id;

            await WithUnitOfWorkAsync(async () =>
            {
                var entity = await _repository.GetAsync(recordId);
                foreach (var sku in entity.Skus)
                {
                    entity.RecordSkuPush(
                        sku.Barcode, quantity, listPrice, salePrice,
                        Array.Empty<SalesChannelTrTrendyolProductSkuAttribute>());
                }

                entity.SetActive(isActive);
                await _repository.UpdateAsync(entity, autoSave: true);
            });
        }

        return recordId;
    }

    /// <summary>Sahte kanal listelemesini verilen kalemlerle DEĞİŞTİRİR (önceki testlerin kalıntısı kalmasın;
    /// barkodlar test başına benzersiz olduğundan eski kayıtlar bu haritayla eşleşemez).</summary>
    private void SetRemote(params (string Barcode, int Quantity, decimal ListPrice, decimal SalePrice)[] variants)
    {
        _client.RemoteItems.Clear();
        if (variants.Length == 0)
        {
            return;
        }

        _client.RemoteItems.Add(new TrendyolRemoteProduct(
            ProductMainId: "REMOTE-MAIN",
            Title: "Uzak Urun",
            Description: null,
            CategoryId: null,
            CategoryName: null,
            BrandId: null,
            BrandName: null,
            VatRate: null,
            DimensionalWeight: null,
            DeliveryDuration: null,
            ImageUrls: Array.Empty<string>(),
            Variants: variants
                .Select(v => new TrendyolRemoteVariant(
                    v.Barcode, null, v.Quantity, v.ListPrice, v.SalePrice,
                    ProductContentId: null, Approved: true, OnSale: true,
                    Attributes: Array.Empty<TrendyolRemoteAttribute>()))
                .ToList()));
    }

    /// <summary>İşçi şartı: şirket yok, kullanıcı yok — çözücü ikisini de kendisi kurmalı.</summary>
    private async Task<ChannelReconciliationReport> RunWithoutAmbientContextAsync()
    {
        using (_currentCompany.Change(null))
        using (_principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            return await _resolver.ReconcileAsync();
        }
    }

    private async Task<SalesChannelTrTrendyolProduct> LoadAsync(Guid id)
    {
        return await WithUnitOfWorkAsync(() => _repository.GetAsync(id));
    }
}
