using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// "SİL" HEM BİZDEN HEM TRENDYOL'DAN KALDIRIR (2026-08-16 Hakan kararı: <i>"önce DB sonra Trendyol; Trendyol'dan
/// silinemezse DB geri dönsün"</i>). Üç kilit: ① silme kanala barkodla gider + <c>Delete</c> delil satırı düşer;
/// ② kanal reddederse DB silmesi GERİ DÖNER (tek UoW rollback) + Failed delil satırı; ③ kanala hiç ulaşmamış
/// (SKU'suz) kayıtta pazaryerine hiç gidilmez.
/// </summary>
public abstract class SalesChannelTrTrendyolProductDeleteTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly FakeTrendyolProductClient _fakeClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _historyRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrTrendyolProductDeleteTests()
    {
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _fakeClient = GetRequiredService<FakeTrendyolProductClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _historyRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductPushHistory, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Deleting_a_listed_record_removes_it_locally_and_asks_trendyol_to_delete_the_barcodes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (recordId, _) = await SeedListedRecordAsync(companyId, "DEL1", "BR-DEL-1");
            _fakeClient.RejectDeletes = false;
            _fakeClient.DeletedBarcodeBatches.Clear();

            await _appService.DeleteAsync(recordId);

            // Bizde gitti, kanala barkodla gidildi, defterde Delete satırı var.
            (await WithUnitOfWorkAsync(async () => await _channelProductRepository.FindAsync(recordId))).ShouldBeNull();
            _fakeClient.DeletedBarcodeBatches.ShouldHaveSingleItem().ShouldBe(new[] { "BR-DEL-1" });
            var ledger = await WithUnitOfWorkAsync(async () =>
                await _historyRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == recordId));
            var row = ledger.ShouldHaveSingleItem();
            row.PushKind.ShouldBe(TrendyolProductPushKind.Delete);
            row.Outcome.ShouldBe(ChannelPushOutcome.Succeeded);
            row.Barcode.ShouldBe("BR-DEL-1");
        }
    }

    [Fact]
    public async Task When_trendyol_rejects_the_delete_the_local_record_is_kept()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (recordId, _) = await SeedListedRecordAsync(companyId, "DEL2", "BR-DEL-2");
            _fakeClient.RejectDeletes = true;
            try
            {
                var ex = await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(recordId));
                ex.Code.ShouldBe("TradeXpress:Trendyol:Product:DeleteFailed");

                // DB silmesi GERİ DÖNDÜ — kayıt yerinde (aynı UoW rollback; ikinci bir telafi kodu yok).
                (await WithUnitOfWorkAsync(async () => await _channelProductRepository.FindAsync(recordId))).ShouldNotBeNull();
            }
            finally
            {
                _fakeClient.RejectDeletes = false;
            }
        }
    }

    [Fact]
    public async Task Deleting_a_record_that_never_reached_the_channel_does_not_call_trendyol()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "DEL3");
            var product = await WithUnitOfWorkAsync(async () =>
                await _productRepository.InsertAsync(new Product(companyId, "NEVERSENT", "Hic Gonderilmedi"), autoSave: true));
            var dto = await _appService.CreateAsync(new SalesChannelTrTrendyolProductCreateDto
            {
                ProductId = product.Id, SalesChannelId = channel.Id, BrandId = "82",
            });
            _fakeClient.DeletedBarcodeBatches.Clear();

            await _appService.DeleteAsync(dto.Id);

            (await WithUnitOfWorkAsync(async () => await _channelProductRepository.FindAsync(dto.Id))).ShouldBeNull();
            _fakeClient.DeletedBarcodeBatches.ShouldBeEmpty();   // pazaryerinde silinecek listing yok
        }
    }

    // ── Tohum ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanala ULAŞMIŞ kayıt: SKU satırı olan (barkodu bilinen) Trendyol kanal ürünü.</summary>
    private async Task<(Guid RecordId, Guid ChannelId)> SeedListedRecordAsync(Guid companyId, string suffix, string barcode)
    {
        var channel = await SeedChannelAsync(companyId, suffix);
        return await WithUnitOfWorkAsync(async () =>
        {
            var product = await _productRepository.InsertAsync(new Product(companyId, "LISTED-" + suffix, "Listelenmis " + suffix), autoSave: true);
            var record = new SalesChannelTrTrendyolProduct(
                companyId, channel.Id, product.Id, productMainId: "LISTED-" + suffix, sequenceNo: 1, categoryId: "411", brandId: "82");
            record.UpsertImportedSku(Guid.NewGuid(), barcode, "STK-" + suffix, remoteContentId: 1);
            await _channelProductRepository.InsertAsync(record, autoSave: true);
            return (record.Id, channel.Id);
        });
    }

    private async Task<SalesChannelTrTrendyol> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol Kanal {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true));
    }
}
