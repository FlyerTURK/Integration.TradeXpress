using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// N11 GÜNLÜK MUTABAKAT ÇÖZÜCÜSÜ (2026-08-21) — dirty-check'in göremediği sapmayı kapatan turun sözleşmesi.
///
/// <para><b>Neden bu testler:</b> tüm oversell/fiyat savunmaları BİZİM gönderdiğimizi bilir (<c>LastSent</c>).
/// Satıcı panelinden elle değişiklik kanalda farklı fiyat/adet bırakır; dirty-check "değişiklik yok" der ve
/// sapma SONSUZA DEK kalırdı. Burada kilitlenen davranış: AKTİF kayıtta taban kanal gözlemine çekilir
/// (senkron turu doğruyu kendiliğinden geri yazsın), sapmasız taban DEĞİŞMEZ, PASİF kayıtta ve kanalda
/// listelenmeyen SKU'da yalnız log (taban oynatılmaz — otomatik push tetiklenmesin).</para>
///
/// <para>Testler çözücüyü tam da işçinin koştuğu şartta çağırır: <b>ambient şirket YOK, ambient kullanıcı
/// YOK</b> (TrendyolBatchStatusResolverTests deseni) — çözücü şirketi kayıttan, kimliği tenant admin'inden
/// kendisi kurmalıdır.</para>
/// </summary>
public abstract class N11ReconciliationResolverTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly N11ReconciliationResolver _resolver;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly FakeN11ProductQueryClient _queryClient;

    protected N11ReconciliationResolverTests()
    {
        _resolver = GetRequiredService<N11ReconciliationResolver>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _repository = GetRequiredService<IRepository<SalesChannelTrN11Product, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _queryClient = GetRequiredService<FakeN11ProductQueryClient>();
    }

    /// <summary>AKTİF kayıtta kanal farklı adet/fiyat bildiriyorsa taban kanal gözlemine çekilir — normal
    /// senkron turu (dirty-check) bizim doğruyu kendiliğinden geri yazabilsin diye.</summary>
    [Fact]
    public async Task A_drifted_active_sku_baseline_is_pulled_to_the_channel_observation()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedAsync(companyId, "RCN1", "RCN1-S1", lastQuantity: 5, lastPrice: 100m);
        SetRemote(("RCN1-S1", 3, 90m));

        var report = await RunWithoutAmbientContextAsync();

        report.SkippedNoAdmin.ShouldBeFalse("tenant admin bulunmalı — bulunamadıysa işçi yine sessiz kalır");
        report.CorrectedSkus.ShouldBeGreaterThanOrEqualTo(1);
        report.DriftedRecords.ShouldBeGreaterThanOrEqualTo(1);

        var sku = (await LoadAsync(recordId)).Skus.Single();
        sku.LastSentQuantity.ShouldBe(3);
        sku.LastSentOptionPrice.ShouldBe(90m);

        // Mutabakat GÖNDERIM DEĞİLDİR: PushHistory'ye satır yazılmaz ("gönderdim kaydı fiilen giden setten
        // yazılır" — biz bir şey göndermedik, yalnız gözlemi tabana işledik). Hakem bulgusu: bu değişmez
        // assert edilmeden korumasızdı.
        var ledger = await WithUnitOfWorkAsync(async () => await GetRequiredService<IRepository<SalesChannelTrN11ProductPushHistory, Guid>>()
            .GetListAsync(h => h.SalesChannelTrN11ProductId == recordId));
        ledger.ShouldBeEmpty();
    }

    /// <summary>Bekleyen push task'lı kayıt ATLANIR — gözlem, kuyruktaki gönderimin ÖNCESİNİ gösterebilir;
    /// kuyruk çözücüsünün terfisiyle yarışılmaz.
    ///
    /// <para><b>Pending, TOCTOU PENCERESİNDE düşer</b> (snapshot'tan SONRA, kayıt işlenmeden ÖNCE — fake'in
    /// sorgu kancasıyla): snapshot süzgeci bu kaydı TEMİZ görür, dolayısıyla iddia yalnız kayıt-seviyesi
    /// yeniden-kontrolle ayakta durur. O kontrol kaldırılırsa taban ezilir ve test kırmızı yanar; pending
    /// koşudan ÖNCE kurulsaydı snapshot süzgeci yakalar, yeniden-kontrol hiç sınanmazdı.</para></summary>
    [Fact]
    public async Task A_record_with_a_pending_push_task_is_skipped()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedAsync(companyId, "RCN5", "RCN5-S1", lastQuantity: 5, lastPrice: 100m);
        SetRemote(("RCN5-S1", 3, 90m));

        // TOCTOU penceresi: kanal sorgusu koşarken 15 dk'lık senkron push submit etmiş gibi davran.
        _queryClient.OnQueryAll = async () =>
        {
            _queryClient.OnQueryAll = null;   // tek sefer — sonraki testlere sızmasın
            using (_currentCompany.Change(companyId))
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var record = await _repository.GetAsync(recordId);
                    record.MarkPushQueued("task-rcn5", DateTime.UtcNow);
                    await _repository.UpdateAsync(record, autoSave: true);
                });
            }
        };

        var report = await RunWithoutAmbientContextAsync();

        report.SkippedPending.ShouldBeGreaterThanOrEqualTo(1);
        var sku = (await LoadAsync(recordId)).Skus.Single();
        sku.LastSentQuantity.ShouldBe(5, "Bekleyen task varken taban EZİLMEZ — uçuştaki gönderimle yarışılmaz.");
        sku.LastSentOptionPrice.ShouldBe(100m);
    }

    /// <summary>Kanal bizim tabanla AYNI değerleri bildiriyorsa hiçbir şey yazılmaz — mutabakat bir düzeltme
    /// yoludur, her turda "dokunuldu" izi bırakan bir senkron değil.</summary>
    [Fact]
    public async Task A_matching_sku_baseline_is_left_untouched()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedAsync(companyId, "RCN2", "RCN2-S1", lastQuantity: 5, lastPrice: 100m);
        SetRemote(("RCN2-S1", 5, 100m));

        var report = await RunWithoutAmbientContextAsync();

        // Uzak haritada yalnız bu testin kodu var → düzeltme sayacı sıfır olmak zorunda (başka kayıt eşleşemez).
        report.CorrectedSkus.ShouldBe(0);

        var sku = (await LoadAsync(recordId)).Skus.Single();
        sku.LastSentQuantity.ShouldBe(5);
        sku.LastSentOptionPrice.ShouldBe(100m);
    }

    /// <summary>PASİF kayıtta kanal hâlâ satılabilir adet gösteriyorsa YALNIZ loglanır: taban 0'da kalır ki
    /// dirty-check tetiklenip otomatik push gitmesin — pasif kayıtta beklenen kanal durumu adet-0'dır ve
    /// kararı kullanıcı verir.</summary>
    [Fact]
    public async Task A_passive_record_drift_is_only_logged_and_the_baseline_stays()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedAsync(companyId, "RCN3", "RCN3-S1", lastQuantity: 0, lastPrice: 100m, isActive: false);
        SetRemote(("RCN3-S1", 4, 100m));

        var report = await RunWithoutAmbientContextAsync();

        report.PassiveDrifts.ShouldBeGreaterThanOrEqualTo(1);
        report.CorrectedSkus.ShouldBe(0);

        var sku = (await LoadAsync(recordId)).Skus.Single();
        sku.LastSentQuantity.ShouldBe(0, "pasif kayıtta taban oynatılsaydı bir sonraki tur otomatik push denerdi");
        sku.LastSentOptionPrice.ShouldBe(100m);
    }

    /// <summary>Tabanı dolu SKU kanal listelemesinde HİÇ yoksa (elle silinmiş listeleme) taban değiştirilmez,
    /// yalnız loglanır: listelemenin kaybı değer sapmasından farklı bir durumdur — taban null'a çekilseydi
    /// otomatik yeniden-oluşturma tetiklenirdi, o karar kullanıcıya aittir.</summary>
    [Fact]
    public async Task A_sku_missing_from_the_channel_is_only_logged()
    {
        var companyId = Guid.NewGuid();
        var recordId = await SeedAsync(companyId, "RCN4", "RCN4-S1", lastQuantity: 5, lastPrice: 100m);
        SetRemote();   // kanal listelemesi boş — SKU kanalda yok

        var report = await RunWithoutAmbientContextAsync();

        report.MissingSkus.ShouldBeGreaterThanOrEqualTo(1);
        report.CorrectedSkus.ShouldBe(0);

        var sku = (await LoadAsync(recordId)).Skus.Single();
        sku.LastSentQuantity.ShouldBe(5);
        sku.LastSentOptionPrice.ShouldBe(100m);
    }

    // ── Fikstür ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanal + ürün + tek SKU'lu kanal kaydı kurar; taban (<c>LastSent*</c>) verilen değerlere,
    /// aktiflik bayrağı verilen duruma çekilir. Doğrudan repository ile — turun konusu push zinciri değil,
    /// tabanın kendisidir.</summary>
    private async Task<Guid> SeedAsync(
        Guid companyId, string code, string stockCode, int lastQuantity, decimal lastPrice, bool isActive = true)
    {
        Guid recordId = default;
        using (_currentCompany.Change(companyId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var channel = await _channelRepository.InsertAsync(
                    new SalesChannelTrN11(companyId, $"N11 {code}", $"N11 Kanal {code}", "app-key", "app-secret"),
                    autoSave: true);
                var product = await _productRepository.InsertAsync(
                    new Product(companyId, code, $"Urun {code}"), autoSave: true);

                var record = new SalesChannelTrN11Product(
                    companyId, channel.Id, product.Id,
                    sellerCode: code, sequenceNo: 1,
                    categoryExternalId: "1000846", shipmentTemplateName: "Sablon");
                record.UpsertImportedSku(Guid.NewGuid(), stockCode, n11SkuId: 1);
                record.RecordStockPriceSync(stockCode, lastQuantity, lastPrice, version: null);
                record.SetActive(isActive);

                recordId = (await _repository.InsertAsync(record, autoSave: true)).Id;
            });
        }

        return recordId;
    }

    /// <summary>Sahte kanal listelemesini verilen SKU satırlarıyla değiştirir (önceki testlerin kalıntısı
    /// kalmasın — kodlar test başına benzersiz olduğundan eski kayıtlar bu haritayla eşleşemez).</summary>
    private void SetRemote(params (string StockCode, int Quantity, decimal SalePrice)[] rows)
    {
        var items = rows
            .Select(r => new N11RestProductSummary(
                N11ProductId: 1001,
                ProductMainId: null,
                StockCode: r.StockCode,
                Title: "Uzak Urun",
                SalePrice: r.SalePrice,
                ListPrice: r.SalePrice,
                Quantity: r.Quantity,
                SaleStatus: "On_Sale",
                ProductStatus: "Active",
                CategoryId: null,
                ImageUrls: Array.Empty<string>()))
            .ToList();

        _queryClient.Page = new N11RestProductPage(items, 0, items.Count == 0 ? 0 : 1, items.Count);
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

    private async Task<SalesChannelTrN11Product> LoadAsync(Guid id)
    {
        return await WithUnitOfWorkAsync(() => _repository.GetAsync(id));
    }
}
