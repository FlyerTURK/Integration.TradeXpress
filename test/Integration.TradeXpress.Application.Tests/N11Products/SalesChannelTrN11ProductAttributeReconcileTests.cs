using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// KARAKTERİZASYON ağı (S1, 2026-07-09) — N11 attribute-reconcile mekaniğinin MEVCUT davranışını kilitler
/// (SynchronizeStockItemsAsync + klon-sonra-ayrış taslağı; public AppService yüzeyinden, gerçek
/// Sqlite repository'leriyle — EfCore concrete: EfCoreSalesChannelTrN11ProductAttributeReconcileTests). S2-S4
/// paylaşılan çekirdeğe taşıma sırasında imza formatı/koruma/silme/eşleştirme davranışı değişirse KIRMIZI olur.
/// </summary>
public abstract class SalesChannelTrN11ProductAttributeReconcileTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // Agnostik varyant tablosunda Product varyantları bu sahip-adıyla tutulur (production: ProductEntityName).
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly EntityVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<EntityVariant, Guid> _erpVariantRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttribute, Guid> _channelAttributeRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeValue, Guid> _channelAttributeValueRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _headerRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _recipeLineRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrN11ProductAttributeReconcileTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _erpSynchronizer = GetRequiredService<EntityVariantSynchronizer>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _erpAttributeRepository = GetRequiredService<IRepository<EntityAttribute, Guid>>();
        _erpValueRepository = GetRequiredService<IRepository<EntityAttributeValue, Guid>>();
        _erpVariantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _channelAttributeRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductAttribute, Guid>>();
        _channelAttributeValueRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductAttributeValue, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _recipeLineRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── Kartezyen + CombinationSignature formatı ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_with_two_attributes_generates_cartesian_rows_with_attribute_sorted_id_signatures()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD1");
            var created = await CreateChannelProductAsync(channel, product,
                BuildAttribute("Renk", 0, "Red", "Blue"),
                BuildAttribute("Beden", 1, "Small", "Medium"));

            // Eksen grafı persist edildi (2 eksen × 2 değer).
            var channelAttributes = await GetChannelAttributesAsync(created.Id);
            channelAttributes.Select(a => a.Name).ShouldBe(new[] { "Renk", "Beden" });
            var valuesByAttribute = await GetChannelAttributeValuesAsync(channelAttributes.Select(a => a.Id).ToList());
            valuesByAttribute.Values.SelectMany(v => v).Count().ShouldBe(4);

            // 2×2 kartezyen → 4 imzalı StockItem satırı.
            var headers = await GetSignedHeadersAsync(created.Id);
            headers.Count.ShouldBe(4);

            // İmza SNAPSHOT'ı: "{AttributeId}={ValueId}|..." — çiftler AttributeId'ye göre ARTAN sıralı (Guid.CompareTo).
            // S3/S4 formatı bozarsa (ör. Name tabanlı imza, farklı ayraç, sırasız) burada KIRMIZI.
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var renk = channelAttributes.Single(a => a.Name == "Renk");
            var beden = channelAttributes.Single(a => a.Name == "Beden");
            foreach (var renkValue in valuesByAttribute[renk.Id])
            {
                foreach (var bedenValue in valuesByAttribute[beden.Id])
                {
                    expected.Add(BuildExpectedSignature((renk.Id, renkValue.Id), (beden.Id, bedenValue.Id)));
                }
            }

            headers.Select(h => h.CombinationSignature!).ShouldBe(expected, ignoreOrder: true);

            foreach (var header in headers)
            {
                var pairs = ParseSignature(header.CombinationSignature!);
                pairs.Count.ShouldBe(2);
                pairs[0].AttributeId.CompareTo(pairs[1].AttributeId).ShouldBeLessThan(0);   // AttributeId artan sıralı

                // ERP'de attribute yok → fırsatçı eşleşme kaynağı yok → tüm satırlar N11-only.
                header.ProductVariantId.ShouldBeNull();
            }
        }
    }

    // ── İmza eşleşen mevcut satır KORUNUR (Id + override alanları) ───────────────────────────────────

    [Fact]
    public async Task Matching_signature_rows_keep_id_and_overrides_and_only_missing_combinations_are_inserted()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD2");
            var created = await CreateChannelProductAsync(channel, product,
                BuildAttribute("Renk", 0, "Red", "Blue"),
                BuildAttribute("Beden", 1, "Small", "Medium"));

            // Kullanıcı emeği: 4 satıra override yaz (N11-only → fiyat + stok İKİSİ de zorunlu).
            var dto = await _appService.GetAsync(created.Id);
            foreach (var node in dto.StockItems)
            {
                node.OverridePrice = 150m;
                node.OverrideStock = 7;
                node.Margin = 20m;
            }

            await _appService.UpdateAsync(created.Id, BuildUpdateDto(dto));
            var before = await GetSignedHeadersAsync(created.Id);
            before.Count.ShouldBe(4);

            // Beden eksenine "Large" eklenir → yalnız 2 EKSİK kombinasyon insert edilir.
            var attributesInput = ToAttributesInput(await GetChannelAttributesAsync(created.Id), await GetChannelAttributeValuesAsync((await GetChannelAttributesAsync(created.Id)).Select(a => a.Id).ToList()));
            attributesInput.Single(a => a.Name == "Beden").Values.Add(new SalesChannelTrN11ProductAttributeValueDto
            {
                Value = "Large",
                DisplayOrder = 2,
            });
            await _appService.RegenerateStockItemsAsync(created.Id, attributesInput);

            var after = await GetSignedHeadersAsync(created.Id);
            after.Count.ShouldBe(6);

            // Mevcut 4 satır: Id DEĞİŞMEDİ + override/marj alanları aynen duruyor.
            foreach (var old in before)
            {
                // imzası hâlâ üretilebilen satır korunmalı (Single: yoksa/çoğaldıysa KIRMIZI)
                var preserved = after.Single(h => h.Id == old.Id);
                preserved.CombinationSignature.ShouldBe(old.CombinationSignature);
                preserved.OverridePrice.ShouldBe(150m);
                preserved.OverrideStock.ShouldBe(7);
                preserved.Margin.ShouldBe(20m);
            }

            // Yeni 2 satır override'sız + (ERP eşleşmesi olmadığından) N11-only doğar.
            var fresh = after.Where(h => before.All(b => b.Id != h.Id)).ToList();
            fresh.Count.ShouldBe(2);
            fresh.ShouldAllBe(h => h.OverridePrice == null && h.OverrideStock == null && h.ProductVariantId == null);
        }
    }

    // ── Kaldırılan kombinasyon: satır + reçetesi cascade silinir ─────────────────────────────────────

    [Fact]
    public async Task Removed_attribute_value_deletes_orphan_rows_and_cascades_their_recipe_lines()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD3");
            var created = await CreateChannelProductAsync(channel, product,
                BuildAttribute("Renk", 0, "Red", "Blue"),
                BuildAttribute("Beden", 1, "Small", "Medium"));

            var headers = await GetSignedHeadersAsync(created.Id);
            var channelAttributes = await GetChannelAttributesAsync(created.Id);
            var valuesByAttribute = await GetChannelAttributeValuesAsync(channelAttributes.Select(a => a.Id).ToList());
            var renk = channelAttributes.Single(a => a.Name == "Renk");
            var blue = valuesByAttribute[renk.Id].Single(v => v.Value == "Blue");

            // "Blue" içeren bir satıra kanal reçete satırı iliştir (cascade'in kanıt nesnesi).
            var blueHeader = headers.Single(h =>
                ParseSignature(h.CombinationSignature!).Any(p => p.ValueId == blue.Id)
                && ParseSignature(h.CombinationSignature!).Any(p => p.ValueId == valuesByAttribute[channelAttributes.Single(a => a.Name == "Beden").Id].Single(v => v.Value == "Small").Id));
            var recipeLineId = await WithUnitOfWorkAsync(async () =>
            {
                var line = await _recipeLineRepository.InsertAsync(
                    new SalesChannelTrN11ProductStockItemRecipeLine(
                        companyId, created.Id, blueHeader.Id, RecipeComponentType.CatalogCommodity, 0),
                    autoSave: true);
                return line.Id;
            });

            // "Blue" değeri silinir → Blue'lu 2 kombinasyon artık üretilemez.
            var attributesInput = ToAttributesInput(channelAttributes, valuesByAttribute);
            attributesInput.Single(a => a.Name == "Renk").Values.Single(v => v.Value == "Blue").IsDeleted = true;
            await _appService.RegenerateStockItemsAsync(created.Id, attributesInput);

            var after = await GetSignedHeadersAsync(created.Id);
            after.Count.ShouldBe(2);
            after.ShouldAllBe(h => !h.CombinationSignature!.Contains(blue.Id.ToString()));

            // Orphan satırın reçetesi de gitti (cascade).
            (await WithUnitOfWorkAsync(async () =>
                await _recipeLineRepository.FindAsync(recipeLineId))).ShouldBeNull();
        }
    }

    // ── Fırsatçı ERP eşleşmesi (MatchErpVariant) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Reconcile_assigns_erp_variant_on_exact_name_value_overlap_and_leaves_n11_only_null()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD4");

            // ERP tarafı: Renk[Red,Blue] → synchronizer 2 ERP varyantı + bağlarını üretir (agnostik EntityVariant).
            await WithUnitOfWorkAsync(async () =>
            {
                var attribute = await _erpAttributeRepository.InsertAsync(
                    new EntityAttribute(companyId, ProductEntityName, product.Id, "Renk", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new EntityAttributeValue(companyId, attribute.Id, "Red", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new EntityAttributeValue(companyId, attribute.Id, "Blue", 1), autoSave: true);
                await _erpSynchronizer.SynchronizeAsync(ProductEntityName, product.Id, companyId, product.Name);
            });
            var erpVariants = await WithUnitOfWorkAsync(async () =>
                await _erpVariantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
            var erpRed = erpVariants.Single(v => v.Code == "RED");
            var erpBlue = erpVariants.Single(v => v.Code == "BLUE");

            // N11 tarafı: aynı eksen adı + değerler ("red" küçük harf → normalize eşleşme TRIM+UPPER)
            // + ERP'de OLMAYAN "Green".
            var created = await CreateChannelProductAsync(channel, product,
                BuildAttribute("Renk", 0, "red", "Blue", "Green"));

            var headers = await GetSignedHeadersAsync(created.Id);
            headers.Count.ShouldBe(3);

            var channelAttributes = await GetChannelAttributesAsync(created.Id);
            var values = (await GetChannelAttributeValuesAsync(channelAttributes.Select(a => a.Id).ToList())).Values.Single();
            SalesChannelTrN11ProductStockItem HeaderOf(string value)
            {
                var valueId = values.Single(v => v.Value == value).Id;
                return headers.Single(h => ParseSignature(h.CombinationSignature!).Any(p => p.ValueId == valueId));
            }

            // TAM örtüşme → ERP varyant Id'si atanır (fırsatçı, bir kerelik); örtüşmeyen → null (N11-only).
            HeaderOf("Red").ProductVariantId.ShouldBe(erpRed.Id);
            HeaderOf("Blue").ProductVariantId.ShouldBe(erpBlue.Id);
            HeaderOf("Green").ProductVariantId.ShouldBeNull();
        }
    }

    // ── N11-only satırda override zorunluluğu ────────────────────────────────────────────────────────

    [Fact]
    public async Task Saving_n11_only_row_without_price_and_stock_overrides_throws_override_required()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD5");
            var created = await CreateChannelProductAsync(channel, product, BuildAttribute("Renk", 0, "Red"));

            var dto = await _appService.GetAsync(created.Id);
            var node = dto.StockItems.ShouldHaveSingleItem();
            node.ProductVariantId.ShouldBeNull();   // ön koşul: ERP eşleşmesi yok → N11-only

            // Fiyat VE stok boş → fail-fast (ERP fallback kaynağı yok).
            node.OverridePrice = null;
            node.OverrideStock = null;
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _appService.UpdateAsync(created.Id, BuildUpdateDto(dto)));
            exception.Code.ShouldBe("TradeXpress:N11:ProductVariant:OverrideRequiredForN11Only");

            // Tek eksik alan da yetmez: fiyat dolu ama stok boş → yine KIRMIZI.
            node.OverridePrice = 99m;
            node.OverrideStock = null;
            (await Should.ThrowAsync<BusinessException>(
                () => _appService.UpdateAsync(created.Id, BuildUpdateDto(dto))))
                .Code.ShouldBe("TradeXpress:N11:ProductVariant:OverrideRequiredForN11Only");

            // İkisi de dolu → kaydedilir.
            node.OverridePrice = 99m;
            node.OverrideStock = 3;
            await _appService.UpdateAsync(created.Id, BuildUpdateDto(dto));
            var header = (await GetSignedHeadersAsync(created.Id)).ShouldHaveSingleItem();
            header.OverridePrice.ShouldBe(99m);
            header.OverrideStock.ShouldBe(3);
        }
    }

    // ── Özellik sayısı üst-sınırı (ERP MaxAttributesPerProduct=5 simetriği, S4 guard) ─────────────────

    [Fact]
    public async Task Creating_with_six_attributes_fails_fast_with_too_many_attributes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD8");

            var exception = await Should.ThrowAsync<BusinessException>(() => CreateChannelProductAsync(channel, product,
                BuildAttribute("Renk", 0, "Red"),
                BuildAttribute("Beden", 1, "Small"),
                BuildAttribute("Kumas", 2, "Cotton"),
                BuildAttribute("Desen", 3, "Plain"),
                BuildAttribute("Yaka", 4, "Round"),
                BuildAttribute("Kol", 5, "Short")));

            exception.Code.ShouldBe("TradeXpress:N11:Product:TooManyAttributes");
        }
    }

    // ── Klon-sonra-ayrış taslağı (BuildDraftAttributesFromErpAsync) ────────────────────────────────────────

    [Fact]
    public async Task Get_without_persisted_attributes_builds_draft_attributes_from_erp_without_writing_to_db()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD6");
            await WithUnitOfWorkAsync(async () =>
            {
                var renk = await _erpAttributeRepository.InsertAsync(
                    new EntityAttribute(companyId, ProductEntityName, product.Id, "Renk", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new EntityAttributeValue(companyId, renk.Id, "Red", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new EntityAttributeValue(companyId, renk.Id, "Blue", 1), autoSave: true);
                var beden = await _erpAttributeRepository.InsertAsync(
                    new EntityAttribute(companyId, ProductEntityName, product.Id, "Beden", 1), autoSave: true);
                await _erpValueRepository.InsertAsync(new EntityAttributeValue(companyId, beden.Id, "Small", 0), autoSave: true);
            });

            // Özellik modu HİÇ aktive edilmemiş kayıt (create'te özellik girilmedi).
            var created = await CreateChannelProductAsync(channel, product);

            var dto = await _appService.GetAsync(created.Id);

            // Taslak: ERP attribute/value'larından üretilir, TÜM Id'ler boş (persist YOK).
            dto.ProductAttributes.Count.ShouldBe(2);
            dto.ProductAttributes.Select(a => a.Name).ShouldBe(new[] { "Renk", "Beden" });
            dto.ProductAttributes.ShouldAllBe(a => a.Id == Guid.Empty);
            dto.ProductAttributes.Single(a => a.Name == "Renk").Values.Select(v => v.Value).ShouldBe(new[] { "Red", "Blue" });
            dto.ProductAttributes.Single(a => a.Name == "Beden").Values.Select(v => v.Value).ShouldBe(new[] { "Small" });
            dto.ProductAttributes.SelectMany(a => a.Values).ShouldAllBe(v => v.Id == Guid.Empty);

            // Salt-okuma DB'ye YAZMADI: eksen tablosu bu kayıt için hâlâ boş.
            (await GetChannelAttributesAsync(created.Id)).ShouldBeEmpty();
            (await GetSignedHeadersAsync(created.Id)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Get_for_erp_product_without_attributes_yields_empty_draft_attributes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD7");
            var created = await CreateChannelProductAsync(channel, product);

            var dto = await _appService.GetAsync(created.Id);

            dto.ProductAttributes.ShouldBeEmpty();   // niteliksiz ürün → kullanıcı isterse elle eksen ekler
            (await GetChannelAttributesAsync(created.Id)).ShouldBeEmpty();
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private async Task<(SalesChannelTrN11 Channel, Product Product)> SeedChannelAndProductAsync(Guid companyId, string productCode)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _channelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{productCode}", $"N11 Kanal {productCode}", "app-key", "app-secret"),
                autoSave: true);
            var product = await _productRepository.InsertAsync(
                new Product(companyId, productCode, $"Urun {productCode}"), autoSave: true);
            return (channel, product);
        });
    }

    private async Task<SalesChannelTrN11ProductDto> CreateChannelProductAsync(
        SalesChannelTrN11 channel, Product product, params SalesChannelTrN11ProductAttributeDto[] channelAttributes)
    {
        return await _appService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryExternalId = "1000846",
            ShipmentTemplateName = "Standart Teslimat",
            ProductAttributes = channelAttributes.ToList(),
        });
    }

    private static SalesChannelTrN11ProductAttributeDto BuildAttribute(string name, int displayOrder, params string[] values)
    {
        return new SalesChannelTrN11ProductAttributeDto
        {
            Name = name,
            DisplayOrder = displayOrder,
            Values = values
                .Select((v, i) => new SalesChannelTrN11ProductAttributeValueDto { Value = v, DisplayOrder = i })
                .ToList(),
        };
    }

    /// <summary>Persist edilmiş eksen/değer grafını Update/Regenerate input DTO'suna çevirir (Id'ler dolu).</summary>
    private static List<SalesChannelTrN11ProductAttributeDto> ToAttributesInput(
        List<SalesChannelTrN11ProductAttribute> channelAttributes,
        Dictionary<Guid, List<SalesChannelTrN11ProductAttributeValue>> valuesByAttribute)
    {
        return channelAttributes.Select(a => new SalesChannelTrN11ProductAttributeDto
        {
            Id = a.Id,
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByAttribute.TryGetValue(a.Id, out var values) ? values : new List<SalesChannelTrN11ProductAttributeValue>())
                .Select(v => new SalesChannelTrN11ProductAttributeValueDto
                {
                    Id = v.Id,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                })
                .ToList(),
        }).ToList();
    }

    private static SalesChannelTrN11ProductUpdateDto BuildUpdateDto(SalesChannelTrN11ProductDto dto)
    {
        return new SalesChannelTrN11ProductUpdateDto
        {
            CategoryExternalId = dto.CategoryExternalId,
            CategoryName = dto.CategoryName,
            Condition = dto.Condition,
            ShipmentTemplateName = dto.ShipmentTemplateName,
            Domestic = dto.Domestic,
            PreparingDay = dto.PreparingDay,
            MaxPurchaseQuantity = dto.MaxPurchaseQuantity,
            CurrencyUnitId = dto.CurrencyUnitId,
            ProductionDate = dto.ProductionDate,
            ExpirationDate = dto.ExpirationDate,
            IsActive = dto.IsActive,
            SellerNote = dto.SellerNote,
            Description = dto.Description,
            GroupItemCode = dto.GroupItemCode,
            GroupAttribute = dto.GroupAttribute,
            ItemName = dto.ItemName,
            CategoryAttributes = dto.CategoryAttributes,
            SpecialInfo = dto.SpecialInfo,
            StockItems = dto.StockItems,
            ProductAttributes = dto.ProductAttributes,
        };
    }

    /// <summary>İmza formatı SNAPSHOT'ı: "{AttributeId}={ValueId}|..." — çiftler AttributeId'ye göre artan (Guid.CompareTo).</summary>
    private static string BuildExpectedSignature(params (Guid AttributeId, Guid ValueId)[] pairs)
    {
        return string.Join('|', pairs.OrderBy(p => p.AttributeId).Select(p => $"{p.AttributeId}={p.ValueId}"));
    }

    private static List<(Guid AttributeId, Guid ValueId)> ParseSignature(string signature)
    {
        return signature
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split('=');
                parts.Length.ShouldBe(2, $"imza çifti '{pair}' 'AttributeId=ValueId' formatında olmalı");
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
            })
            .ToList();
    }

    private async Task<List<SalesChannelTrN11ProductAttribute>> GetChannelAttributesAsync(Guid channelProductId)
    {
        var channelAttributes = await WithUnitOfWorkAsync(async () =>
            await _channelAttributeRepository.GetListAsync(a => a.SalesChannelTrN11ProductId == channelProductId));
        return channelAttributes.OrderBy(a => a.DisplayOrder).ToList();
    }

    private async Task<Dictionary<Guid, List<SalesChannelTrN11ProductAttributeValue>>> GetChannelAttributeValuesAsync(List<Guid> attributeIds)
    {
        var values = await WithUnitOfWorkAsync(async () =>
            await _channelAttributeValueRepository.GetListAsync(v => attributeIds.Contains(v.AttributeId)));
        return values
            .GroupBy(v => v.AttributeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.DisplayOrder).ToList());
    }

    private async Task<List<SalesChannelTrN11ProductStockItem>> GetSignedHeadersAsync(Guid channelProductId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _headerRepository.GetListAsync(h =>
                h.SalesChannelTrN11ProductId == channelProductId && h.CombinationSignature != null));
    }
}
