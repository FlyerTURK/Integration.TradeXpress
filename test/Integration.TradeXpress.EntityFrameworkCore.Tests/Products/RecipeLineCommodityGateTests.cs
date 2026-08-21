using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KATALOG EMTİASI SATIRI EMTİASIZ KAYDEDİLEMEZ — <see cref="RecipeLineCommodityGate"/> /
/// <see cref="RecipeLineCommodityRule"/>, reçete yazımının TEK yolu olan
/// <see cref="ProductRecipeLineWriter"/> üzerinden.
///
/// <para><b>Kapatılan delik (2026-08-21 ölçümü):</b> <c>ComponentType = CatalogCommodity</c> satırında
/// <c>CommodityId</c> boş kalabiliyordu ve HİÇBİR yerde reddedilmiyordu — kayıtta hata yok, satışa hazırlık
/// doğrulamasında hata yok, push'ta hata yok. En sinsi sınıf hata: satır kabul edilir, sonra sessizce yanlış
/// cevap üretir. <c>ProductRecipeCostCalculator</c> katalog kaydını bulamadığı için satırı maliyete katmaz
/// (ürün ucuz görünür), <c>RecipeCommodityIndex</c> satırı hiçbir emtiaya bağlayamadığı için stok tetiği o
/// ürünü hiç uyandırmaz (kanalda eski adet canlı kalır). Kullanıcı bu üründe reçete olduğunu görür.</para>
///
/// <para><b>Guard'ın KAPSAMADIĞI satırlar da pinli:</b> hizmet satırında <c>CommodityId</c> yalnız etiket
/// referansıdır ve meşru şekilde boş kalır — canlıda emtiasız duran yüzlerce kanal hizmet satırı var; guard
/// oraya taşarsa o kayıtlar kaydedilemez hâle gelir. Türev (SelectedLines) satırı da bedelini üst satırlardan
/// alır, emtiaya ihtiyaç duymaz.</para>
///
/// <para><b>Mevcut veriyi kilitleme kaçış yolu:</b> emtiasız duran ESKİ bir satır silinerek kaydedilebilmeli
/// (silinecek satır denetlenmez) — aksi hâlde bozuk tek satır formu kalıcı olarak kapatırdı. Ölçüm: canlı
/// veritabanında emtiasız katalog satırı YOK (21 reçete satırının hepsinde <c>CommodityId</c> dolu), yani guard
/// bugün hiçbir kaydı kilitlemiyor; bu test o durumun ileride değişmesine karşı.</para>
///
/// KIRMIZIYSA ya emtiasız satır reçeteye sızıyor ya da guard hizmet/türev satırını da vuruyor — testi gevşetme.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class RecipeLineCommodityGateTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ProductRecipeLineWriter _writer;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public RecipeLineCommodityGateTests()
    {
        _writer = GetRequiredService<ProductRecipeLineWriter>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    /// <summary>Geçerli bir satırın ARDINDAN emtiasız satır: guard ikisini birden dışarıda bırakmalı
    /// (fail-fast; kısmi yazım reçeteyi yarım bırakırdı).</summary>
    [Fact]
    public async Task A_catalog_line_without_a_commodity_is_rejected_and_nothing_is_written()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() =>
                _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(Guid.NewGuid(), lineOrder: 0),
                    BuildMetalLine(commodityId: null, lineOrder: 1),
                })));

            exception.Code.ShouldBe(RecipeLineCommodityGate.CommodityRequiredErrorCode);
            exception.Code.ShouldBe("TradeXpress:Product:Recipe:CommodityRequired");
            exception.Data["LineOrder"].ShouldBe(2);   // kullanıcıya 1 tabanlı satır numarası
            exception.Data["Commodity"].ShouldBe(nameof(ProcessType.Metal));

            var persisted = await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId);
            persisted.ShouldBeEmpty("Fail-fast: reddedilen satırla birlikte GEÇERLİ satır da yazılmamalı.");
        }
    }

    /// <summary>Boş Guid de "seçilmedi"dir: istemci combo'su temizlendiğinde null yerine
    /// <see cref="Guid.Empty"/> gönderebiliyor ve o değer hiçbir katalog kaydını göstermez. Yalnız null'a
    /// bakan bir guard bu yoldan sessizce delinirdi.</summary>
    [Fact]
    public async Task A_catalog_line_with_an_empty_commodity_id_is_rejected_too()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() =>
                _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(Guid.Empty, lineOrder: 0),
                })));

            exception.Code.ShouldBe(RecipeLineCommodityGate.CommodityRequiredErrorCode);
            (await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)).ShouldBeEmpty();
        }
    }

    /// <summary>Hizmet satırı KAPSAM DIŞI — emtiasız kaydedilebilmeli.</summary>
    [Fact]
    public async Task A_service_line_without_a_commodity_is_out_of_scope_and_accepted()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildServiceLine(lineOrder: 0, operand: 10m),
                });
                return true;
            });

            var line = (await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)).ShouldHaveSingleItem();
            line.ComponentType.ShouldBe(RecipeComponentType.Service);
            line.CommodityId.ShouldBeNull();
        }
    }

    /// <summary>TÜREV satır (SelectedLines) da kapsam dışı: bedelini seçtiği ÜST satırlardan alır, kendi emtiası
    /// yoktur. Guard buraya taşarsa türev bedel kurma yolu tamamen kapanır — bu yüzden ayrıca pinlendi.</summary>
    [Fact]
    public async Task A_derived_service_line_selecting_an_upstream_catalog_line_is_still_saved()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var metalLine = BuildMetalLine(Guid.NewGuid(), lineOrder: 0);
        var derivedLine = BuildServiceLine(lineOrder: 1, operand: 5m);
        derivedLine.DerivedBaseMode = RecipeDerivedBaseMode.SelectedLines;
        derivedLine.DerivedSourceKeys = new List<Guid> { metalLine.ClientKey };

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    metalLine,
                    derivedLine,
                });
                return true;
            });

            var lines = (await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId))
                .OrderBy(l => l.LineOrder)
                .ToList();
            lines.Count.ShouldBe(2);

            var derived = lines[1];
            derived.CommodityId.ShouldBeNull();
            derived.DerivedBaseMode.ShouldBe(RecipeDerivedBaseMode.SelectedLines);
            derived.DerivedSourceLineIds.ShouldBe(
                lines[0].Id.ToString(),
                "Türev satırın kaynak çözümü (2. geçiş) guard'dan etkilenmemeli.");
        }
    }

    /// <summary>KAÇIŞ YOLU: emtiasız duran ESKİ bir satır SİLİNEREK kaydedilebilmeli. Silinecek satır
    /// denetlenmez (zaten gidiyor) — aksi hâlde geçmişten kalan tek bozuk satır, kullanıcının reçete formunu
    /// kalıcı olarak kapatır ve düzeltmenin hiçbir yolu kalmazdı.</summary>
    [Fact]
    public async Task An_existing_commodityless_line_can_still_be_deleted()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            // Guard'dan ÖNCE yazılmış bozuk satırı taklit et: yazıcıdan geçmeden doğrudan kurulur.
            var legacy = new ProductVariantRecipeLine(
                companyId, variantId, RecipeComponentType.CatalogCommodity, lineOrder: 0);
            legacy.SetCatalogCommodity(
                ProcessType.Metal, null, null, 1m, 2m, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
            await _recipeLines.InsertAsync(legacy, autoSave: true);

            var deletion = BuildMetalLine(commodityId: null, lineOrder: 0);
            deletion.Id = legacy.Id;
            deletion.IsDeleted = true;

            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    deletion,
                    BuildMetalLine(Guid.NewGuid(), lineOrder: 1),
                });
                return true;
            });

            var remaining = (await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId))
                .ShouldHaveSingleItem();
            remaining.CommodityId.ShouldNotBeNull();
        }
    }

    private static ProductRecipeLineGraphDto BuildMetalLine(Guid? commodityId, int lineOrder)
    {
        return new ProductRecipeLineGraphDto
        {
            LineOrder = lineOrder,
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId = commodityId,
            Quantity = 1m,
            Amount = 2m,
            Factor = 0.916m,
            PaymentType = ProcessPaymentType.Normal,
        };
    }

    private static ProductRecipeLineGraphDto BuildServiceLine(int lineOrder, decimal operand)
    {
        return new ProductRecipeLineGraphDto
        {
            LineOrder = lineOrder,
            ComponentType = RecipeComponentType.Service,
            CommodityId = null,
            DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = operand,
        };
    }
}
