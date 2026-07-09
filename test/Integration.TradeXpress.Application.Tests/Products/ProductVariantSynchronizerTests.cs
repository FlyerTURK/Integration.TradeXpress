using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KARAKTERİZASYON ağı (S1, 2026-07-09) — <see cref="ProductVariantSynchronizer"/>'ın MEVCUT davranışını kilitler:
/// varyant üretim/reconcile mekaniği S2-S4'te paylaşılan çekirdeğe taşınırken "davranış birebir korundu" güvencesi
/// bu testlerden gelir. Gerçek Sqlite repository'leriyle çalışır (EfCore concrete: EfCoreProductVariantSynchronizerTests).
/// KIRMIZIYSA refactor davranışı değiştirmiş demektir — testi gevşetme, çekirdeği düzelt.
/// </summary>
public abstract class ProductVariantSynchronizerTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ProductVariantSynchronizer _synchronizer;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _valueRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected ProductVariantSynchronizerTests()
    {
        _synchronizer = GetRequiredService<ProductVariantSynchronizer>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _attributeRepository = GetRequiredService<IRepository<ProductAttribute, Guid>>();
        _valueRepository = GetRequiredService<IRepository<ProductAttributeValue, Guid>>();
        _variantRepository = GetRequiredService<IRepository<ProductVariant, Guid>>();
        _linkRepository = GetRequiredService<IRepository<ProductVariantAttributeValue, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── Statik türetme snapshot'ları (BuildVariantCode / BuildVariantName / BuildKey) ────────────────

    [Fact]
    public void BuildVariantCode_joins_value_names_with_dash()
    {
        ProductVariantSynchronizer.BuildVariantCode(new[] { "Red", "Small" }).ShouldBe("Red-Small");
    }

    [Fact]
    public void BuildVariantCode_truncates_prefix_at_code_max_length()
    {
        var longA = new string('A', 40);
        var longB = new string('B', 40);
        var joined = $"{longA}-{longB}";   // 81 karakter > CodeMaxLength (64)

        var code = ProductVariantSynchronizer.BuildVariantCode(new[] { longA, longB });

        // Kesme = baştan CodeMaxLength karakter (istisna değil, sessiz prefix kesmesi — mevcut davranış).
        code.Length.ShouldBe(ProductConsts.CodeMaxLength);
        code.ShouldBe(joined[..ProductConsts.CodeMaxLength]);
    }

    [Fact]
    public void BuildVariantName_prefixes_product_name_and_joins_values_with_space()
    {
        ProductVariantSynchronizer.BuildVariantName("Tshirt Basic", new[] { "Red", "Small" })
            .ShouldBe("Tshirt Basic Red Small");
    }

    [Fact]
    public void BuildVariantName_truncates_prefix_at_name_max_length()
    {
        var productName = new string('P', 200);
        var value = new string('V', 100);
        var joined = $"{productName} {value}";   // 301 karakter > NameMaxLength (256)

        var name = ProductVariantSynchronizer.BuildVariantName(productName, new[] { value });

        name.Length.ShouldBe(ProductConsts.NameMaxLength);
        name.ShouldBe(joined[..ProductConsts.NameMaxLength]);
    }

    [Fact]
    public void BuildKey_is_order_independent_and_sorts_value_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (first, second) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        var key1 = ProductVariantSynchronizer.BuildKey(new[] { a, b });
        var key2 = ProductVariantSynchronizer.BuildKey(new[] { b, a });

        // Deterministik imza: sıra bağımsız + "id1|id2" (artan Guid sırası) formatı.
        key1.ShouldBe(key2);
        key1.ShouldBe($"{first}|{second}");
    }

    // ── Kartezyen üretim ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_attributes_with_2x3_values_produce_six_variants_with_derived_code_and_name()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "TSHIRT", "Tshirt Basic");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Beden", 1, "Small", "Medium", "Large");

            await SynchronizeAsync(product);

            var variants = await GetVariantsAsync(product.Id);
            variants.Count.ShouldBe(6);

            // Code = değer adları '-' ile (normalize: UPPER); eksen sırası = attribute DisplayOrder.
            variants.Select(v => v.Code).ShouldBe(
                new[] { "RED-SMALL", "RED-MEDIUM", "RED-LARGE", "BLUE-SMALL", "BLUE-MEDIUM", "BLUE-LARGE" },
                ignoreOrder: true);

            // Name = "{ÜrünAdı} {değer1} {değer2}" (normalize: TitleCase).
            variants.Single(v => v.Code == "RED-SMALL").Name.ShouldBe("Tshirt Basic Red Small");
            variants.Single(v => v.Code == "BLUE-LARGE").Name.ShouldBe("Tshirt Basic Blue Large");

            // Yeni kombinasyonlar AKTİF doğar; her varyantın attribute başına TEK bağ satırı (2 eksen → 2 bağ).
            variants.ShouldAllBe(v => v.IsActive);
            var links = await GetLinksAsync(variants.Select(v => v.Id).ToList());
            links.GroupBy(l => l.ProductVariantId).ShouldAllBe(g => g.Count() == 2);

            // Tekil main garantisi: main = en düşük Code'lu varyant.
            variants.Count(v => v.IsMain).ShouldBe(1);
            variants.Single(v => v.IsMain).Code.ShouldBe("BLUE-LARGE");
        }
    }

    // ── Diff-koruma: eşleşen kombinasyonun kimliği + override edilen alanları DEĞİŞMEZ ───────────────

    [Fact]
    public async Task Existing_combination_keeps_variant_id_and_overridden_fields_when_new_value_added()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "TSHIRT", "Tshirt Basic");
            var renk = await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Beden", 1, "Small", "Medium", "Large");
            await SynchronizeAsync(product);

            var before = await GetVariantsAsync(product.Id);
            var redSmall = before.Single(v => v.Code == "RED-SMALL");

            // Kullanıcı override'ı: fiyat + stok (senkron bu alanlara DOKUNMAMALI).
            await WithUnitOfWorkAsync(async () =>
            {
                var v = await _variantRepository.GetAsync(redSmall.Id);
                v.SetSalePrice(100m, null);
                v.SetStock(5);
                await _variantRepository.UpdateAsync(v, autoSave: true);
            });

            // Yeni değer → yeni kombinasyonlar; mevcutlar korunur.
            await WithUnitOfWorkAsync(async () =>
            {
                await _valueRepository.InsertAsync(
                    new ProductAttributeValue(companyId, renk.Id, "Green", 2), autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Count.ShouldBe(9);

            var preservedIds = before.Select(v => v.Id).ToHashSet();
            after.Count(v => preservedIds.Contains(v.Id)).ShouldBe(6);   // 6 eski kombinasyonun Id'si aynen durur

            var redSmallAfter = after.Single(v => v.Code == "RED-SMALL");
            redSmallAfter.Id.ShouldBe(redSmall.Id);
            redSmallAfter.SalePrice.ShouldBe(100m);
            redSmallAfter.StockQuantity.ShouldBe(5);

            after.Select(v => v.Code).ShouldContain("GREEN-SMALL");
            after.Count(v => v.IsMain).ShouldBe(1);
        }
    }

    // ── Diff-silme: değer silinince o kombinasyonların varyant + bağları silinir ─────────────────────

    [Fact]
    public async Task Removed_value_deletes_its_combinations_with_links_and_promotes_new_main()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "TSHIRT", "Tshirt Basic");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Beden", 1, "Small", "Medium", "Large");
            await SynchronizeAsync(product);

            var before = await GetVariantsAsync(product.Id);
            before.Single(v => v.IsMain).Code.ShouldBe("BLUE-LARGE");   // ön koşul: main Blue kombinasyonunda
            var blueIds = before.Where(v => v.Code.StartsWith("BLUE-")).Select(v => v.Id).ToList();

            // "Blue" değeri silinir → BLUE-* kombinasyonları artık hedefte yok.
            await WithUnitOfWorkAsync(async () =>
            {
                await _valueRepository.DeleteAsync(v => v.CompanyId == companyId && v.Value == "Blue", autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Count.ShouldBe(3);
            after.Select(v => v.Code).ShouldBe(
                new[] { "RED-SMALL", "RED-MEDIUM", "RED-LARGE" }, ignoreOrder: true);

            // Silinen varyantların bağ satırları da gitti.
            var orphanLinks = await GetLinksAsync(blueIds);
            orphanLinks.ShouldBeEmpty();

            // Main silindi → kalanların en düşük Code'lusu main'e yükselir.
            after.Count(v => v.IsMain).ShouldBe(1);
            after.Single(v => v.IsMain).Code.ShouldBe("RED-LARGE");
        }
    }

    // ── 0 attribute ↔ base varyant geçişleri ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Zero_attributes_produce_single_main_base_variant()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "PLAIN", "Plain Product");

            await SynchronizeAsync(product);

            var variants = await GetVariantsAsync(product.Id);
            variants.Count.ShouldBe(1);
            variants[0].Code.ShouldBe(ProductConsts.MainVariantCode);
            variants[0].Name.ShouldBe(ProductConsts.MainVariantName);
            variants[0].IsMain.ShouldBeTrue();
            variants[0].IsActive.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Linkless_base_variant_is_replaced_when_attributes_are_added()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "PLAIN", "Plain Product");
            await SynchronizeAsync(product);
            var baseVariant = (await GetVariantsAsync(product.Id)).ShouldHaveSingleItem();

            await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await SynchronizeAsync(product);

            // Bağ'sız base varyant hedef kombinasyonlarda YOK → silinir; yerini kombinasyonlar alır.
            var after = await GetVariantsAsync(product.Id);
            after.Select(v => v.Code).ShouldBe(new[] { "RED", "BLUE" }, ignoreOrder: true);
            after.ShouldNotContain(v => v.Id == baseVariant.Id);
            after.Count(v => v.IsMain).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Removing_all_attributes_deletes_linked_variants_and_restores_base_variant()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "TSHIRT", "Tshirt Basic");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Beden", 1, "Small", "Medium", "Large");
            await SynchronizeAsync(product);
            var linkedIds = (await GetVariantsAsync(product.Id)).Select(v => v.Id).ToList();
            linkedIds.Count.ShouldBe(6);

            // Attribute'lar tamamen kaldırılır → attribute'lu dönemin bağlı varyantları temizlenir.
            await WithUnitOfWorkAsync(async () =>
            {
                await _valueRepository.DeleteAsync(v => v.CompanyId == companyId, autoSave: true);
                await _attributeRepository.DeleteAsync(a => a.ProductId == product.Id, autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Count.ShouldBe(1);
            after[0].Code.ShouldBe(ProductConsts.MainVariantCode);
            after[0].IsMain.ShouldBeTrue();
            (await GetLinksAsync(linkedIds)).ShouldBeEmpty();
        }
    }

    // ── Değersiz-eksen koruması ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Attribute_without_values_preserves_existing_variant_set_and_only_guarantees_main()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var product = await CreateProductAsync(companyId, "TSHIRT", "Tshirt Basic");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Renk", 0, "Red", "Blue");
            await AddAttributeWithValuesAsync(companyId, product.Id, "Beden", 1, "Small", "Medium", "Large");
            await SynchronizeAsync(product);
            var before = await GetVariantsAsync(product.Id);
            before.Count.ShouldBe(6);

            // Kullanıcı henüz değer giriyor: yeni eksen değersiz → kartezyen boş; mevcut set SİLİNMEZ.
            await WithUnitOfWorkAsync(async () =>
            {
                await _attributeRepository.InsertAsync(
                    new ProductAttribute(companyId, product.Id, "Materyal", 2), autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Select(v => v.Id).ShouldBe(before.Select(v => v.Id), ignoreOrder: true);
            after.Count(v => v.IsMain).ShouldBe(1);
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private async Task<Product> CreateProductAsync(Guid companyId, string code, string name)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _productRepository.InsertAsync(new Product(companyId, code, name), autoSave: true));
    }

    private async Task<ProductAttribute> AddAttributeWithValuesAsync(
        Guid companyId, Guid productId, string attributeName, int displayOrder, params string[] values)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var attribute = await _attributeRepository.InsertAsync(
                new ProductAttribute(companyId, productId, attributeName, displayOrder), autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _valueRepository.InsertAsync(
                    new ProductAttributeValue(companyId, attribute.Id, values[i], i), autoSave: true);
            }

            return attribute;
        });
    }

    private async Task SynchronizeAsync(Product product)
    {
        await WithUnitOfWorkAsync(async () => await _synchronizer.SynchronizeAsync(product));
    }

    private async Task<List<ProductVariant>> GetVariantsAsync(Guid productId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _variantRepository.GetListAsync(v => v.ProductId == productId));
    }

    private async Task<List<ProductVariantAttributeValue>> GetLinksAsync(List<Guid> variantIds)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _linkRepository.GetListAsync(l => variantIds.Contains(l.ProductVariantId)));
    }
}
