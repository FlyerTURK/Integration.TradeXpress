using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// CORE REÇETE DEĞİŞİNCE DEVRALINMIŞ KANAL KOPYALARI TAZELENİR — <see cref="ChannelRecipeRefresher"/>'ın
/// yazıcı (<see cref="ProductRecipeLineWriter"/>) çağrısı üzerinden uçtan uca ağı.
///
/// <para><b>Sabitlenen delik:</b> kanal reçetesi "klon-sonra-ayrış" yaşar; kullanıcı kanal formunu bileşime hiç
/// dokunmadan kaydettiyse kopya DONMUŞ ama fiilen devralınmıştır. Core sonra değişince push fiyatlaması
/// (yalnız persist edilmiş kanal satırlarını okur) ESKİ bileşimle fiyatlamaya devam ediyordu — hatasız, logsuz.
/// Tazeleme kararı KAYIT-ÖNCESİ core'a karşı verilir (kalıcı bayrak yok; yeni core ile kıyas her devralınmış
/// kopyayı "override" sanıp sonsuza dek dondururdu — bu test o yanlış kurgunun da ağıdır).</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ChannelRecipeRefreshTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ProductRecipeLineWriter _writer;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _coreLines;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _trendyolHeaders;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _trendyolLines;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11Headers;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11Lines;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public ChannelRecipeRefreshTests()
    {
        _writer = GetRequiredService<ProductRecipeLineWriter>();
        _coreLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _trendyolHeaders = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItem, Guid>>();
        _trendyolLines = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid>>();
        _n11Headers = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _n11Lines = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Core_recipe_change_refreshes_inherited_trendyol_copy_but_not_overrides()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var channelProductId = Guid.NewGuid();
        var metalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
            {
                BuildMetalLine(metalId, quantity: 2m, amount: 4m),
            });
            var coreLine = (await _coreLines.GetListAsync(l => l.ProductVariantId == variantId)).ShouldHaveSingleItem();

            // Başlık 1 — DEVRALINMIŞ persist kopya: bileşim core ile birebir + kanalın kendi komisyon satırı.
            var inherited = await _trendyolHeaders.InsertAsync(
                new SalesChannelTrTrendyolProductStockItem(companyId, channelProductId, variantId), autoSave: true);
            var inheritedLine = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                companyId, channelProductId, inherited.Id, RecipeComponentType.CatalogCommodity, 0);
            inheritedLine.SetCatalogCommodity(
                ProcessType.Metal, metalId, null, 2m, 4m, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
            await _trendyolLines.InsertAsync(inheritedLine, autoSave: true);
            var commissionLine = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                companyId, channelProductId, inherited.Id, RecipeComponentType.Service, 1);
            commissionLine.SetService(null, RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.GrossUp, 18.5m, null);
            commissionLine.SetSideCostKind(SideCostKind.Commission);
            await _trendyolLines.InsertAsync(commissionLine, autoSave: true);

            // Başlık 2 — OVERRIDE kopya: kullanıcı miktara dokunmuş (2 yerine 5).
            var overridden = await _trendyolHeaders.InsertAsync(
                new SalesChannelTrTrendyolProductStockItem(companyId, channelProductId, variantId), autoSave: true);
            var overriddenLine = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                companyId, channelProductId, overridden.Id, RecipeComponentType.CatalogCommodity, 0);
            overriddenLine.SetCatalogCommodity(
                ProcessType.Metal, metalId, null, 5m, 10m, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
            await _trendyolLines.InsertAsync(overriddenLine, autoSave: true);

            // Başlık 3 — hiç persist edilmemiş (canlı klon): tazeleme satır ÜRETMEMELİ.
            var untouched = await _trendyolHeaders.InsertAsync(
                new SalesChannelTrTrendyolProductStockItem(companyId, channelProductId, variantId), autoSave: true);

            // CORE DEĞİŞİR: miktar 2 → 3 (yazıcı yolu — üretimdeki tek yol).
            await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
            {
                BuildMetalLine(metalId, quantity: 3m, amount: 6m, id: coreLine.Id),
            });

            // Devralınmış kopya YENİ bileşimi aldı; komisyon satırı korunup bileşimin arkasına numaralandı.
            var refreshed = await _trendyolLines.GetListAsync(l => l.StockItemId == inherited.Id);
            refreshed.Count.ShouldBe(2);
            var refreshedCommodity = refreshed.Single(l => l.SideCostKind == null);
            refreshedCommodity.Quantity.ShouldBe(3m);
            refreshedCommodity.Amount.ShouldBe(6m);
            refreshedCommodity.LineOrder.ShouldBe(0);
            var keptCommission = refreshed.Single(l => l.SideCostKind == SideCostKind.Commission);
            keptCommission.DerivedOperand.ShouldBe(18.5m);
            keptCommission.LineOrder.ShouldBe(1);

            // Override kopyaya DOKUNULMADI — kullanıcının 5'i duruyor.
            var kept = (await _trendyolLines.GetListAsync(l => l.StockItemId == overridden.Id)).ShouldHaveSingleItem();
            kept.Quantity.ShouldBe(5m);

            // Persist edilmemiş başlık persist edilmemiş kaldı (canlı klon kendiliğinden devralır).
            (await _trendyolLines.GetListAsync(l => l.StockItemId == untouched.Id)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Core_recipe_change_refreshes_inherited_n11_copy()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var channelProductId = Guid.NewGuid();
        var metalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
            {
                BuildMetalLine(metalId, quantity: 1m, amount: 7m),
            });
            var coreLine = (await _coreLines.GetListAsync(l => l.ProductVariantId == variantId)).ShouldHaveSingleItem();

            var inherited = await _n11Headers.InsertAsync(
                new SalesChannelTrN11ProductStockItem(companyId, channelProductId, variantId), autoSave: true);
            var inheritedLine = new SalesChannelTrN11ProductStockItemRecipeLine(
                companyId, channelProductId, inherited.Id, RecipeComponentType.CatalogCommodity, 0);
            inheritedLine.SetCatalogCommodity(
                ProcessType.Metal, metalId, null, 1m, 7m, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
            await _n11Lines.InsertAsync(inheritedLine, autoSave: true);

            // Miktar da değişir — imza (Quantity/Factor/kimlikler) Amount'ı içermez; yalnız Amount değişimi
            // bileşim değişikliği DEĞİLDİR ve tazeleme bilinçli olarak kısa devre yapar.
            await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
            {
                BuildMetalLine(metalId, quantity: 2m, amount: 9m, id: coreLine.Id),
            });

            var refreshed = (await _n11Lines.GetListAsync(l => l.StockItemId == inherited.Id)).ShouldHaveSingleItem();
            refreshed.Quantity.ShouldBe(2m);
            refreshed.Amount.ShouldBe(9m);
        }
    }

    /// <summary>
    /// ÜRETİM KOŞULU: yazıcı TEK UoW içinde çalışır ve satırı YERİNDE günceller (GetAsync → ApplyFields → Update).
    /// EF kimlik haritası aynı instance'ı döndürdüğünden, "kayıt-öncesi core" entity referansı olarak
    /// tutulursa mutasyondan sonra yeni değeri gösterir → refresher "aynı bileşim" der → tazeleme HİÇ çalışmaz.
    /// Diğer testler repository çağrılarını ayrı UoW'larda koştuğu için bu deliği GÖRMÜYORDU (yeşil kalırken
    /// üretimde ölüydü — bağımsız denetimde yakalandı, 2026-08-14). Bu test tek UoW'u zorlar.
    /// </summary>
    [Fact]
    public async Task Refresh_survives_in_place_update_inside_a_single_unit_of_work()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var channelProductId = Guid.NewGuid();
        var metalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(metalId, quantity: 2m, amount: 4m),
                });
                return true;
            });
            var coreLine = (await _coreLines.GetListAsync(l => l.ProductVariantId == variantId)).ShouldHaveSingleItem();

            var inherited = await _trendyolHeaders.InsertAsync(
                new SalesChannelTrTrendyolProductStockItem(companyId, channelProductId, variantId), autoSave: true);
            var inheritedLine = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                companyId, channelProductId, inherited.Id, RecipeComponentType.CatalogCommodity, 0);
            inheritedLine.SetCatalogCommodity(
                ProcessType.Metal, metalId, null, 2m, 4m, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
            await _trendyolLines.InsertAsync(inheritedLine, autoSave: true);

            // TEK UoW: kayıt-öncesi okuma + yerinde güncelleme + refresh aynı DbContext'te.
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(metalId, quantity: 3m, amount: 6m, id: coreLine.Id),
                });
                return true;
            });

            var refreshed = (await _trendyolLines.GetListAsync(l => l.StockItemId == inherited.Id)).ShouldHaveSingleItem();
            refreshed.Quantity.ShouldBe(3m, "Tek UoW'da yerinde güncelleme sonrası devralınmış kopya TAZELENMELİ.");
        }
    }

    /// <summary>Core/kanal kıyasında kullanılan basit maden satırı grafı (Factor 0.916 sabit — imzanın parçası).</summary>
    private static ProductRecipeLineGraphDto BuildMetalLine(Guid metalId, decimal quantity, decimal amount, Guid id = default)
    {
        return new ProductRecipeLineGraphDto
        {
            Id = id,
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId = metalId,
            Quantity = quantity,
            Amount = amount,
            Factor = 0.916m,
        };
    }
}
