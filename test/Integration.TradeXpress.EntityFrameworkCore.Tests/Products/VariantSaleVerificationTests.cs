using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SATIŞA DOĞRULAMANIN İNSAN YOLU — <see cref="ProductSaleVerifier"/> + push guard'ı birlikte.
///
/// <para><b>Kapatılan açık:</b> guard (<see cref="VariantSaleReadinessResolver"/>) fail-closed ÇALIŞIYORDU ama
/// onayı verecek yol yoktu — <c>MarkVerified</c>/<c>MarkSaleReady</c>'nin üretim kodunda sıfır çağıranı vardı.
/// Canlıda 165/165 varyant <c>Draft</c>, hiçbir ürün pazaryerine çıkamıyordu. Guard tasarlandığı gibi
/// çalışıyordu; yalnız açacak kimse yoktu.</para>
///
/// <para><b>Neden entegrasyon testi:</b> sınanan şey iki ayrı bileşenin AYNI stamp'i üretip tüketmesi. Birim
/// testi her iki tarafı ayrı ayrı yeşil gösterirdi; formüller ayrışsaydı ortaya "onaylandı ama asla geçerli
/// sayılmıyor" gibi mesajsız bir kilit çıkardı ve hiçbir birim testi bunu göremezdi.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VariantSaleVerificationTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";

    private readonly ProductSaleVerifier _verifier;
    private readonly VariantSaleReadinessResolver _resolver;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantDetail, Guid> _details;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public VariantSaleVerificationTests()
    {
        _verifier       = GetRequiredService<ProductSaleVerifier>();
        _resolver       = GetRequiredService<VariantSaleReadinessResolver>();
        _products       = GetRequiredService<IRepository<Product, Guid>>();
        _variants       = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _details        = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _recipeLines    = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _asyncExecuter  = GetRequiredService<IAsyncQueryableExecuter>();
    }

    /// <summary>① Doğrulanan varyant guard'dan GEÇER. Doğrulama ÖNCESİ geçmediği de aynı testte pinli —
    /// yoksa test "guard hep açık" hâlinde de yeşil kalırdı.</summary>
    [Fact]
    public async Task Verified_variant_passes_the_sale_gate()
    {
        var scenario = await SeedAsync("VSV1", variantCount: 2);

        (await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds)))
            .ShouldBeEmpty();   // guard KAPALI: onay yok

        var result = await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));

        result.VerifiedVariants.ShouldBe(2);
        result.ProductMarkedReady.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();

        var sellable = await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds));
        sellable.ShouldBe(scenario.VariantIds.ToHashSet(), ignoreOrder: true);

        (await WithUnitOfWorkAsync(() => _products.GetAsync(scenario.ProductId)))
            .SaleStatus.ShouldBe(ProductSaleStatus.Ready);
    }

    /// <summary>② REÇETE DEĞİŞİRSE onay kendiliğinden düşer — stamp eskir.
    /// <para>Bu, tasarımın en önemli parçası: onay bir kereye mahsus bir mühür değil, o ANDAKİ reçeteye
    /// verilmiş bir onaydır. Reçete değişip onay ayakta kalsaydı, kullanıcı 5 gram için onayladığı ürünü
    /// 50 gram olarak satmaya devam ederdi ve hiçbir uyarı almazdı.</para></summary>
    [Fact]
    public async Task Recipe_change_after_verification_closes_the_gate_again()
    {
        var scenario = await SeedAsync("VSV2", variantCount: 1);

        await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));
        (await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds)))
            .ShouldHaveSingleItem();

        // Reçeteye satır eklenir → stamp değişir.
        await WithUnitOfWorkAsync(async () =>
        {
            var line = new ProductVariantRecipeLine(
                scenario.CompanyId, scenario.VariantIds[0], RecipeComponentType.Service, lineOrder: 1);
            await _recipeLines.InsertAsync(line, autoSave: true);
        });

        (await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds)))
            .ShouldBeEmpty();
    }

    /// <summary>③ KULLANICININ KAPATTIĞI varyant yeniden doğrulanabilir (<c>Closed → Ready</c>).
    /// <para>Sistem yolu (<c>Suspend</c>) <c>Closed</c>'a dokunmaz — kullanıcının kararını ezmez; geri dönüş
    /// yalnız buradan, yani insandan geçer.</para></summary>
    [Fact]
    public async Task Closed_variant_can_be_reopened_by_verification()
    {
        var scenario = await SeedAsync("VSV3", variantCount: 1);

        // Detay kaydı fixture'da (fiyatla) açılmıştır; burada yalnız KAPATILIR.
        await WithUnitOfWorkAsync(async () =>
        {
            var detail = await _asyncExecuter.FirstAsync(
                (await _details.GetQueryableAsync()).Where(d => d.EntityVariantId == scenario.VariantIds[0]));
            detail.Close();
            await _details.UpdateAsync(detail, autoSave: true);
        });

        await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));

        var reopened = await WithUnitOfWorkAsync(async () => await _asyncExecuter.FirstOrDefaultAsync(
            (await _details.GetQueryableAsync()).Where(d => d.EntityVariantId == scenario.VariantIds[0])));
        reopened!.SaleStatus.ShouldBe(ProductSaleStatus.Ready);
    }

    /// <summary>④ Yalnız SEÇİLEN varyant doğrulanır; seçilmeyene DOKUNULMAZ.
    /// <para>"Hepsini aç" varsayılanı kolaylık olsun diye var, ama kullanıcı tek varyant seçtiyse diğerini de
    /// açmak onun kararını sessizce genişletmek olurdu.</para></summary>
    [Fact]
    public async Task Only_the_requested_variants_are_verified()
    {
        var scenario = await SeedAsync("VSV4", variantCount: 2);

        var result = await WithUnitOfWorkAsync(() => _verifier.VerifyAsync(new ProductSaleVerifyInputDto
        {
            ProductId = scenario.ProductId,
            VariantIds = new List<Guid> { scenario.VariantIds[0] },
        }));

        result.VerifiedVariants.ShouldBe(1);

        var sellable = await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds));
        sellable.ShouldHaveSingleItem().ShouldBe(scenario.VariantIds[0]);
    }

    /// <summary>Bu ürüne ait OLMAYAN varyant sessizce atlanmaz — gerekçe raporlanır.</summary>
    [Fact]
    public async Task Foreign_variant_id_is_reported_not_silently_skipped()
    {
        var scenario = await SeedAsync("VSV5", variantCount: 1);
        var stranger = Volo.Abp.Guids.SimpleGuidGenerator.Instance.Create();

        var result = await WithUnitOfWorkAsync(() => _verifier.VerifyAsync(new ProductSaleVerifyInputDto
        {
            ProductId = scenario.ProductId,
            VariantIds = new List<Guid> { scenario.VariantIds[0], stranger },
        }));

        result.VerifiedVariants.ShouldBe(1);
        result.Issues.ShouldHaveSingleItem().ShouldContain(stranger.ToString());
    }

    // ── otomatik validasyon (2026-08-19 satışa hazırlık paneli ölçeği) ──────────────────────────────────────────────

    /// <summary>⑥ FİYATSIZ varyant doğrulanMAZ ve Issues'ta görünür; fiyatlı kardeşi doğrulanır.
    /// <para>Fiyatsız varyant push aday setinden SESSİZCE elenir; onu Ready yapmak, kullanıcıya "satışa açıldı"
    /// deyip kanala hiç göndermemek olurdu. Hata doğrulama anında, kodla (<c>Variant:NoSalePrice</c>) söylenir.</para></summary>
    [Fact]
    public async Task Unpriced_variant_is_not_verified_and_is_reported()
    {
        var scenario = await SeedAsync("VSV6", variantCount: 2, priceSecondVariant: false);

        var result = await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));

        result.VerifiedVariants.ShouldBe(1);
        result.ProductMarkedReady.ShouldBeTrue();
        // Yalnız KOD pinlenir: mesaj metni localizer'dan gelir ve anahtar tr/en.json'a eklenmeden
        // (ABP eksik anahtarda anahtar adını aynen döndürür) varyant kodu metne girmez — metne bağımlı
        // assert, lokalizasyon kaydına bağımlı kırılganlık üretirdi. Hangi varyantın elendiği aşağıda
        // guard'dan (ResolveSellableAsync) davranışsal olarak pinlenir.
        result.Issues.ShouldContain(i => i.StartsWith(ProductSaleValidator.VariantNoSalePrice, StringComparison.Ordinal));

        var sellable = await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds));
        sellable.ShouldHaveSingleItem().ShouldBe(scenario.VariantIds[0]);
    }

    /// <summary>⑦ KDV'SİZ ürün YİNE doğrulanır ve KDV doğrulama SONUCUNA HİÇ taşınmaz — ne Issues ne Warnings
    /// (Hakan 2026-08-19: KDV yalnız Info; satışa hazırlık panelinin issue listesinde bilgi satırı olarak yaşar, doğrulama
    /// diyaloğunda tekrarlanmaz). Fixture KDV yazmaz; yani ① zaten KDV'siz doğruluyor — bu fact o gerçeği
    /// AÇIKÇA sabitler.</summary>
    [Fact]
    public async Task Missing_vat_does_not_block_verification_and_stays_out_of_the_result()
    {
        var scenario = await SeedAsync("VSV7", variantCount: 1);

        var result = await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));

        result.VerifiedVariants.ShouldBe(1);
        result.Issues.ShouldNotContain(i => i.Contains(ProductSaleValidator.ProductVatMissing));
        result.Warnings.ShouldNotContain(w => w.StartsWith(ProductSaleValidator.ProductVatMissing, StringComparison.Ordinal));
    }

    /// <summary>⑧ ÜRÜN-DÜZEYİ Error (Calculated ama takip edilen emtia satırı yok) HİÇBİR varyantı doğrulatmaz.</summary>
    [Fact]
    public async Task Product_level_error_blocks_every_variant()
    {
        var scenario = await SeedAsync("VSV8", variantCount: 2, stockPolicy: ProductStockPolicy.Calculated);

        var result = await WithUnitOfWorkAsync(
            () => _verifier.VerifyAsync(new ProductSaleVerifyInputDto { ProductId = scenario.ProductId }));

        result.VerifiedVariants.ShouldBe(0);
        result.ProductMarkedReady.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.StartsWith(
            ProductSaleValidator.ProductCalculatedWithoutTrackedCommodity, StringComparison.Ordinal));
        (await WithUnitOfWorkAsync(() => _resolver.ResolveSellableAsync(scenario.VariantIds))).ShouldBeEmpty();
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reçeteli + fiyatlı bir ürün + varyantları. Reçete ŞART: stamp'in gerçekten bir içerikten
    /// hesaplandığını (boş-reçete stamp'iyle yanlışlıkla eşleşmediğini) sürmek için. Fiyat ŞART (2026-08-19):
    /// fiyatsız varyant artık doğrulanmaz. Stok politikası varsayılan <c>Fixed</c> — hizmet satırlı reçete
    /// Calculated'ı karşılamaz (takip edilen emtia yok) ve o hâl ⑧'de ayrıca sınanır.</summary>
    private async Task<VerificationScenario> SeedAsync(
        string prefix,
        int variantCount,
        bool priceSecondVariant = true,
        ProductStockPolicy stockPolicy = ProductStockPolicy.Fixed)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;

        return await WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(data.CompanyId, $"{prefix}-URN", $"{prefix} Ürünü");
            product.SetStockPolicy(stockPolicy);
            product.SetProductCategory(Guid.NewGuid());   // kategori bağı: yalnız "var mı" sorulur (FK doğrulaması yok)
            await _products.InsertAsync(product, autoSave: true);

            var variantIds = new List<Guid>();
            for (var i = 1; i <= variantCount; i++)
            {
                var variant = new EntityVariant(
                    data.CompanyId, ProductEntityName, product.Id, $"{prefix}-V{i}", $"{prefix} Varyant {i}",
                    isMain: i == 1);
                await _variants.InsertAsync(variant, autoSave: true);
                variantIds.Add(variant.Id);

                var line = new ProductVariantRecipeLine(
                    data.CompanyId, variant.Id, RecipeComponentType.Service, lineOrder: 0);
                await _recipeLines.InsertAsync(line, autoSave: true);

                var detail = new ProductVariantDetail(data.CompanyId, variant.Id);
                if (i == 1 || priceSecondVariant)
                {
                    detail.SetSalePrice(1000m + i, null);
                }

                await _details.InsertAsync(detail, autoSave: true);
            }

            return new VerificationScenario(data.CompanyId, product.Id, variantIds, prefix);
        });
    }

    private sealed record VerificationScenario(Guid CompanyId, Guid ProductId, List<Guid> VariantIds, string Prefix);
}
