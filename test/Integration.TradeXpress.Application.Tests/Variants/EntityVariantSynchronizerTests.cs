using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// KARAKTERİZASYON ağı (S1, 2026-07-09; agnostiğe port 2026-07-15) — <see cref="EntityVariantSynchronizer"/>'ın
/// ORKESTRASYON davranışını kilitler: nitelik×değer kartezyeni ↔ mevcut varyant seti mutabakatı (üret/koru/sil +
/// tekil-main). Paylaşılan çekirdeğin (<c>VariantCombinationEngine</c>/<c>VariantSetReconciler</c>) kendi testleri
/// var; orkestrasyon ağı YALNIZ burada. Synchronizer Product+Good+Metal+Stone+Jewelry'ye hizmet ettiğinden bu ağ
/// hepsini korur. Sahip entity olarak Product kullanılır (agnostik bağ: EntityName+EntityId).
/// Gerçek Sqlite repository'leriyle çalışır (EfCore concrete: EfCoreEntityVariantSynchronizerTests).
/// KIRMIZIYSA refactor davranışı değiştirmiş demektir — testi gevşetme, çekirdeği düzelt.
/// </summary>
public abstract class EntityVariantSynchronizerTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // Sahip entity tipi adı — ProductAppService.ProductEntityName ile AYNI değer (agnostik bağ anahtarı).
    private const string ProductEntityName = "Product";

    private readonly EntityVariantSynchronizer _synchronizer;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _valueRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected EntityVariantSynchronizerTests()
    {
        _synchronizer = GetRequiredService<EntityVariantSynchronizer>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _attributeRepository = GetRequiredService<IRepository<EntityAttribute, Guid>>();
        _valueRepository = GetRequiredService<IRepository<EntityAttributeValue, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityVariantAttributeValue, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── Statik türetme snapshot'ları (BuildVariantCode / BuildVariantName / BuildKey) ────────────────

    [Fact]
    public void BuildVariantCode_joins_value_names_with_dash_and_upper_cases()
    {
        // Agnostik türetme Türkçe-farkında BÜYÜTME yapar (ı→I, i→İ); eski Product'ınki düz join'di (case-fold yoktu).
        // Kod kullanıcıya SKU olarak göründüğünden bu BİLİNÇLİ davranış — beklenen değer agnostiğin gerçek çıktısı.
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Red", "Small" }).ShouldBe("RED-SMALL");
    }

    [Fact]
    public void BuildVariantCode_upper_cases_turkish_value_names_without_corruption()
    {
        // Türkçe-farkındalık kanıtı: "Kırmızı"→"KIRMIZI" (ı→I noktasız), "Yeşil"→"YEŞİL" (i→İ noktalı).
        // Invariant büyütme bunları "KıRMıZı"/"YEŞIL" (bozuk) yapardı.
        EntityVariantSynchronizer.BuildVariantCode(new[] { "Kırmızı", "Yeşil" }).ShouldBe("KIRMIZI-YEŞİL");
    }

    [Fact]
    public void BuildVariantCode_truncates_prefix_at_code_max_length()
    {
        var longA = new string('A', 40);
        var longB = new string('B', 40);
        var joined = $"{longA}-{longB}";   // 81 karakter > VariantCodeMaxLength (64)

        var code = EntityVariantSynchronizer.BuildVariantCode(new[] { longA, longB });

        // Kesme = baştan VariantCodeMaxLength karakter (istisna değil, sessiz prefix kesmesi — mevcut davranış).
        code.Length.ShouldBe(EntityVariantConsts.VariantCodeMaxLength);
        code.ShouldBe(joined[..EntityVariantConsts.VariantCodeMaxLength]);
    }

    [Fact]
    public void BuildVariantName_prefixes_owner_name_and_joins_values_with_space()
    {
        EntityVariantSynchronizer.BuildVariantName("Tshirt Basic", new[] { "Red", "Small" })
            .ShouldBe("Tshirt Basic Red Small");
    }

    [Fact]
    public void BuildVariantName_truncates_prefix_at_name_max_length()
    {
        var ownerName = new string('P', 200);
        var value = new string('V', 100);
        var joined = $"{ownerName} {value}";   // 301 karakter > VariantNameMaxLength (256)

        var name = EntityVariantSynchronizer.BuildVariantName(ownerName, new[] { value });

        name.Length.ShouldBe(EntityVariantConsts.VariantNameMaxLength);
        name.ShouldBe(joined[..EntityVariantConsts.VariantNameMaxLength]);
    }

    [Fact]
    public void BuildKey_is_order_independent_and_sorts_value_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (first, second) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        var key1 = EntityVariantSynchronizer.BuildKey(new[] { a, b });
        var key2 = EntityVariantSynchronizer.BuildKey(new[] { b, a });

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

            // Code = değer adları '-' ile (normalize: Türkçe-farkında UPPER); eksen sırası = attribute DisplayOrder.
            // "Medium"→"MEDİUM": agnostik BuildVariantCode i→İ map'ler (Türkçe değerler için doğru; İngilizce değerde
            // de aynı deterministik kural işler). Eski Product türetmesi case-fold yapmıyordu → bu MEŞRU fark.
            variants.Select(v => v.Code).ShouldBe(
                new[] { "RED-SMALL", "RED-MEDİUM", "RED-LARGE", "BLUE-SMALL", "BLUE-MEDİUM", "BLUE-LARGE" },
                ignoreOrder: true);

            // Name = "{SahipAdı} {değer1} {değer2}" (normalize: TitleCase).
            variants.Single(v => v.Code == "RED-SMALL").Name.ShouldBe("Tshirt Basic Red Small");
            variants.Single(v => v.Code == "BLUE-LARGE").Name.ShouldBe("Tshirt Basic Blue Large");

            // Yeni kombinasyonlar AKTİF doğar; her varyantın attribute başına TEK bağ satırı (2 eksen → 2 bağ).
            variants.ShouldAllBe(v => v.IsActive);
            var links = await GetLinksAsync(variants.Select(v => v.Id).ToList());
            links.GroupBy(l => l.EntityVariantId).ShouldAllBe(g => g.Count() == 2);

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

            // Kullanıcı override'ı: stok + barkod (senkron bu alanlara DOKUNMAMALI). Eski Product ağındaki SalePrice
            // override'ı agnostik EntityVariant'ta YOK — Product-özel uzantıya (ProductVariantDetail) taşındı; onun
            // yerine varyantın KENDİ override alanları (stok/barkod) korunuyor mu diye bakılır.
            await WithUnitOfWorkAsync(async () =>
            {
                var v = await _variantRepository.GetAsync(redSmall.Id);
                v.SetStock(5);
                v.SetBarcode("RS-0001");
                await _variantRepository.UpdateAsync(v, autoSave: true);
            });

            // Yeni değer → yeni kombinasyonlar; mevcutlar korunur.
            await WithUnitOfWorkAsync(async () =>
            {
                await _valueRepository.InsertAsync(
                    new EntityAttributeValue(companyId, renk.Id, "Green", 2), autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Count.ShouldBe(9);

            var preservedIds = before.Select(v => v.Id).ToHashSet();
            after.Count(v => preservedIds.Contains(v.Id)).ShouldBe(6);   // 6 eski kombinasyonun Id'si aynen durur

            var redSmallAfter = after.Single(v => v.Code == "RED-SMALL");
            redSmallAfter.Id.ShouldBe(redSmall.Id);
            redSmallAfter.StockQuantity.ShouldBe(5);
            redSmallAfter.Barcode.ShouldBe("RS-0001");

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
            // "MEDİUM" — Türkçe-farkında büyütme (i→İ); bkz. üstteki kartezyen testinin notu.
            after.Select(v => v.Code).ShouldBe(
                new[] { "RED-SMALL", "RED-MEDİUM", "RED-LARGE" }, ignoreOrder: true);

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
            variants[0].Code.ShouldBe(EntityVariantConsts.MainVariantCode);
            variants[0].Name.ShouldBe(EntityVariantConsts.MainVariantName);
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
                await _attributeRepository.DeleteAsync(
                    a => a.EntityName == ProductEntityName && a.EntityId == product.Id, autoSave: true);
            });
            await SynchronizeAsync(product);

            var after = await GetVariantsAsync(product.Id);
            after.Count.ShouldBe(1);
            after[0].Code.ShouldBe(EntityVariantConsts.MainVariantCode);
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
                    new EntityAttribute(companyId, ProductEntityName, product.Id, "Materyal", 2), autoSave: true);
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

    private async Task<EntityAttribute> AddAttributeWithValuesAsync(
        Guid companyId, Guid productId, string attributeName, int displayOrder, params string[] values)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var attribute = await _attributeRepository.InsertAsync(
                new EntityAttribute(companyId, ProductEntityName, productId, attributeName, displayOrder),
                autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _valueRepository.InsertAsync(
                    new EntityAttributeValue(companyId, attribute.Id, values[i], i), autoSave: true);
            }

            return attribute;
        });
    }

    private async Task SynchronizeAsync(Product product)
    {
        await WithUnitOfWorkAsync(async () =>
            await _synchronizer.SynchronizeAsync(ProductEntityName, product.Id, product.CompanyId, product.Name));
    }

    private async Task<List<EntityVariant>> GetVariantsAsync(Guid productId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _variantRepository.GetListAsync(
                v => v.EntityName == ProductEntityName && v.EntityId == productId));
    }

    private async Task<List<EntityVariantAttributeValue>> GetLinksAsync(List<Guid> variantIds)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _linkRepository.GetListAsync(l => variantIds.Contains(l.EntityVariantId)));
    }
}
