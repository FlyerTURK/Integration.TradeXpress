using System;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

/// <summary>
/// KANAL REÇETESİ <c>CommodityVariantId</c> KALICILIK PİNİ — açık iş kaydının kapanışı.
///
/// <para><b>Kayıt neden bayattı:</b> ACIK-ISLER "kanal reçete satırlarında <c>CommodityVariantId</c> YOK
/// [MİGRATION]" diyordu. Yerinde ölçüm aksini gösterdi: kolon <c>Initial</c> migration'ında, entity alanı
/// duruyor, klon yolu (<c>CloneErpRecipeLines</c>) değeri doğrudan kopyalıyor. <b>Migration GEREKMİYOR.</b></para>
///
/// <para><b>Test neden yine de şart:</b> davranış kodda vardı ama hiçbir yerde SÜRÜLMÜYORDU. Sessiz kırılma
/// riski klonda değil <b>EF eşlemesinde</b>: kolon bir <c>ModelCreating</c> düzenlemesinde eşleme dışı kalırsa
/// değer kaydedilir gibi görünür, geri okununca <c>null</c> döner. Sonuç: reçete "maden" der ama HANGİ varyant
/// olduğunu unutur; maliyet yanlış varyanttan hesaplanır ve hiçbir hata çıkmaz.</para>
///
/// <para><b>null MEŞRUDUR</b> (canlıda 20 satırın 20'si null): varyant seçilmeden yazılmış satır ana-varyant
/// fallback'iyle çalışır. null'ı "eksik veri" sayıp doldurmak, kullanıcının hiç yapmadığı bir seçimi ona
/// atfetmek olurdu — bu yüzden o hâl de pinli.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreChannelRecipeVariantIdTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11Lines;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _trendyolLines;
    private readonly TestCompanyContextProvider _companyContext;

    public EfCoreChannelRecipeVariantIdTests()
    {
        _n11Lines       = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid>>();
        _trendyolLines  = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task N11_channel_recipe_line_round_trips_the_commodity_variant_id()
    {
        var companyId = SimpleGuidGenerator.Instance.Create();
        var variantId = SimpleGuidGenerator.Instance.Create();
        _companyContext.CompanyId = companyId;

        var lineId = await WithUnitOfWorkAsync(async () =>
        {
            var line = new SalesChannelTrN11ProductStockItemRecipeLine(
                companyId,
                SimpleGuidGenerator.Instance.Create(),
                SimpleGuidGenerator.Instance.Create(),
                RecipeComponentType.CatalogCommodity,
                lineOrder: 0);

            line.SetCatalogCommodity(
                ProcessType.Metal, SimpleGuidGenerator.Instance.Create(), variantId,
                quantity: 0m, amount: 5m, factor: 1m, valuationUnitId: null,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);

            await _n11Lines.InsertAsync(line, autoSave: true);
            return line.Id;
        });

        var reloaded = await WithUnitOfWorkAsync(() => _n11Lines.GetAsync(lineId));
        reloaded.CommodityVariantId.ShouldBe(variantId);
    }

    /// <summary>Trendyol dalı da AYNI değeri taşır — iki kanal pinlenince üçüncüsü konvansiyonla korunur.</summary>
    [Fact]
    public async Task Trendyol_channel_recipe_line_round_trips_the_commodity_variant_id()
    {
        var companyId = SimpleGuidGenerator.Instance.Create();
        var variantId = SimpleGuidGenerator.Instance.Create();
        _companyContext.CompanyId = companyId;

        var lineId = await WithUnitOfWorkAsync(async () =>
        {
            var line = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                companyId,
                SimpleGuidGenerator.Instance.Create(),
                SimpleGuidGenerator.Instance.Create(),
                RecipeComponentType.CatalogCommodity,
                lineOrder: 0);

            line.SetCatalogCommodity(
                ProcessType.Metal, SimpleGuidGenerator.Instance.Create(), variantId,
                quantity: 0m, amount: 3m, factor: 1m, valuationUnitId: null,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);

            await _trendyolLines.InsertAsync(line, autoSave: true);
            return line.Id;
        });

        var reloaded = await WithUnitOfWorkAsync(() => _trendyolLines.GetAsync(lineId));
        reloaded.CommodityVariantId.ShouldBe(variantId);
    }

    /// <summary>Varyant SEÇİLMEDEN yazılan satır <c>null</c> KALIR — uydurma değer yazılmaz.</summary>
    [Fact]
    public async Task Line_without_a_variant_stays_null()
    {
        var companyId = SimpleGuidGenerator.Instance.Create();
        _companyContext.CompanyId = companyId;

        var lineId = await WithUnitOfWorkAsync(async () =>
        {
            var line = new SalesChannelTrN11ProductStockItemRecipeLine(
                companyId,
                SimpleGuidGenerator.Instance.Create(),
                SimpleGuidGenerator.Instance.Create(),
                RecipeComponentType.CatalogCommodity,
                lineOrder: 0);

            line.SetCatalogCommodity(
                ProcessType.Metal, SimpleGuidGenerator.Instance.Create(), commodityVariantId: null,
                quantity: 0m, amount: 5m, factor: 1m, valuationUnitId: null,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);

            await _n11Lines.InsertAsync(line, autoSave: true);
            return line.Id;
        });

        (await WithUnitOfWorkAsync(() => _n11Lines.GetAsync(lineId)))
            .CommodityVariantId.ShouldBeNull();
    }
}
