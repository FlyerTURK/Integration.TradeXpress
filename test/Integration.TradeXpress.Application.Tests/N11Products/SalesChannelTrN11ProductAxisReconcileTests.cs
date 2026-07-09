using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// KARAKTERİZASYON ağı (S1, 2026-07-09) — N11 axis-reconcile mekaniğinin MEVCUT davranışını kilitler
/// (SynchronizeAttributeAxisVariantsAsync + klon-sonra-ayrış taslağı; public AppService yüzeyinden, gerçek
/// Sqlite repository'leriyle — EfCore concrete: EfCoreSalesChannelTrN11ProductAxisReconcileTests). S2-S4
/// paylaşılan çekirdeğe taşıma sırasında imza formatı/koruma/silme/eşleştirme davranışı değişirse KIRMIZI olur.
/// </summary>
public abstract class SalesChannelTrN11ProductAxisReconcileTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly ProductVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<ProductVariant, Guid> _erpVariantRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeAxis, Guid> _axisRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeAxisValue, Guid> _axisValueRepository;
    private readonly IRepository<SalesChannelTrN11ProductVariant, Guid> _headerRepository;
    private readonly IRepository<SalesChannelTrN11ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrN11ProductAxisReconcileTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _erpSynchronizer = GetRequiredService<ProductVariantSynchronizer>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _erpAttributeRepository = GetRequiredService<IRepository<ProductAttribute, Guid>>();
        _erpValueRepository = GetRequiredService<IRepository<ProductAttributeValue, Guid>>();
        _erpVariantRepository = GetRequiredService<IRepository<ProductVariant, Guid>>();
        _axisRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductAttributeAxis, Guid>>();
        _axisValueRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductAttributeAxisValue, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductVariant, Guid>>();
        _recipeLineRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductVariantRecipeLine, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── Kartezyen + CombinationSignature formatı ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_with_two_axes_generates_cartesian_rows_with_axis_sorted_id_signatures()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD1");
            var created = await CreateChannelProductAsync(channel, product,
                Axis("Renk", 0, "Red", "Blue"),
                Axis("Beden", 1, "Small", "Medium"));

            // Eksen grafı persist edildi (2 eksen × 2 değer).
            var axes = await GetAxesAsync(created.Id);
            axes.Select(a => a.Name).ShouldBe(new[] { "Renk", "Beden" });
            var valuesByAxis = await GetAxisValuesAsync(axes.Select(a => a.Id).ToList());
            valuesByAxis.Values.SelectMany(v => v).Count().ShouldBe(4);

            // 2×2 kartezyen → 4 imzalı StockItem satırı.
            var headers = await GetSignedHeadersAsync(created.Id);
            headers.Count.ShouldBe(4);

            // İmza SNAPSHOT'ı: "{AxisId}={ValueId}|..." — çiftler AxisId'ye göre ARTAN sıralı (Guid.CompareTo).
            // S3/S4 formatı bozarsa (ör. Name tabanlı imza, farklı ayraç, sırasız) burada KIRMIZI.
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var renk = axes.Single(a => a.Name == "Renk");
            var beden = axes.Single(a => a.Name == "Beden");
            foreach (var renkValue in valuesByAxis[renk.Id])
            {
                foreach (var bedenValue in valuesByAxis[beden.Id])
                {
                    expected.Add(BuildExpectedSignature((renk.Id, renkValue.Id), (beden.Id, bedenValue.Id)));
                }
            }

            headers.Select(h => h.CombinationSignature!).ShouldBe(expected, ignoreOrder: true);

            foreach (var header in headers)
            {
                var pairs = ParseSignature(header.CombinationSignature!);
                pairs.Count.ShouldBe(2);
                pairs[0].AxisId.CompareTo(pairs[1].AxisId).ShouldBeLessThan(0);   // AxisId artan sıralı

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
                Axis("Renk", 0, "Red", "Blue"),
                Axis("Beden", 1, "Small", "Medium"));

            // Kullanıcı emeği: 4 satıra override yaz (N11-only → fiyat + stok İKİSİ de zorunlu).
            var dto = await _appService.GetAsync(created.Id);
            foreach (var node in dto.Variants)
            {
                node.OverridePrice = 150m;
                node.OverrideStock = 7;
                node.Margin = 20m;
            }

            await _appService.UpdateAsync(created.Id, BuildUpdateDto(dto));
            var before = await GetSignedHeadersAsync(created.Id);
            before.Count.ShouldBe(4);

            // Beden eksenine "Large" eklenir → yalnız 2 EKSİK kombinasyon insert edilir.
            var axesInput = ToAxesInput(await GetAxesAsync(created.Id), await GetAxisValuesAsync((await GetAxesAsync(created.Id)).Select(a => a.Id).ToList()));
            axesInput.Single(a => a.Name == "Beden").Values.Add(new SalesChannelTrN11ProductAttributeAxisValueDto
            {
                Value = "Large",
                DisplayOrder = 2,
            });
            await _appService.RegenerateVariantsAsync(created.Id, axesInput);

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
    public async Task Removed_axis_value_deletes_orphan_rows_and_cascades_their_recipe_lines()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD3");
            var created = await CreateChannelProductAsync(channel, product,
                Axis("Renk", 0, "Red", "Blue"),
                Axis("Beden", 1, "Small", "Medium"));

            var headers = await GetSignedHeadersAsync(created.Id);
            var axes = await GetAxesAsync(created.Id);
            var valuesByAxis = await GetAxisValuesAsync(axes.Select(a => a.Id).ToList());
            var renk = axes.Single(a => a.Name == "Renk");
            var blue = valuesByAxis[renk.Id].Single(v => v.Value == "Blue");

            // "Blue" içeren bir satıra kanal reçete satırı iliştir (cascade'in kanıt nesnesi).
            var blueHeader = headers.Single(h =>
                ParseSignature(h.CombinationSignature!).Any(p => p.ValueId == blue.Id)
                && ParseSignature(h.CombinationSignature!).Any(p => p.ValueId == valuesByAxis[axes.Single(a => a.Name == "Beden").Id].Single(v => v.Value == "Small").Id));
            var recipeLineId = await WithUnitOfWorkAsync(async () =>
            {
                var line = await _recipeLineRepository.InsertAsync(
                    new SalesChannelTrN11ProductVariantRecipeLine(
                        companyId, created.Id, blueHeader.Id, RecipeComponentType.CatalogCommodity, 0),
                    autoSave: true);
                return line.Id;
            });

            // "Blue" değeri silinir → Blue'lu 2 kombinasyon artık üretilemez.
            var axesInput = ToAxesInput(axes, valuesByAxis);
            axesInput.Single(a => a.Name == "Renk").Values.Single(v => v.Value == "Blue").IsDeleted = true;
            await _appService.RegenerateVariantsAsync(created.Id, axesInput);

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

            // ERP tarafı: Renk[Red,Blue] → synchronizer 2 ERP varyantı + bağlarını üretir.
            await WithUnitOfWorkAsync(async () =>
            {
                var attribute = await _erpAttributeRepository.InsertAsync(
                    new ProductAttribute(companyId, product.Id, "Renk", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new ProductAttributeValue(companyId, attribute.Id, "Red", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new ProductAttributeValue(companyId, attribute.Id, "Blue", 1), autoSave: true);
                await _erpSynchronizer.SynchronizeAsync(product);
            });
            var erpVariants = await WithUnitOfWorkAsync(async () =>
                await _erpVariantRepository.GetListAsync(v => v.ProductId == product.Id));
            var erpRed = erpVariants.Single(v => v.Code == "RED");
            var erpBlue = erpVariants.Single(v => v.Code == "BLUE");

            // N11 tarafı: aynı eksen adı + değerler ("red" küçük harf → normalize eşleşme TRIM+UPPER)
            // + ERP'de OLMAYAN "Green".
            var created = await CreateChannelProductAsync(channel, product,
                Axis("Renk", 0, "red", "Blue", "Green"));

            var headers = await GetSignedHeadersAsync(created.Id);
            headers.Count.ShouldBe(3);

            var axes = await GetAxesAsync(created.Id);
            var values = (await GetAxisValuesAsync(axes.Select(a => a.Id).ToList())).Values.Single();
            SalesChannelTrN11ProductVariant HeaderOf(string value)
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
            var created = await CreateChannelProductAsync(channel, product, Axis("Renk", 0, "Red"));

            var dto = await _appService.GetAsync(created.Id);
            var node = dto.Variants.ShouldHaveSingleItem();
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

    // ── Klon-sonra-ayrış taslağı (BuildDraftAxesFromErpAsync) ────────────────────────────────────────

    [Fact]
    public async Task Get_without_persisted_axes_builds_draft_axes_from_erp_without_writing_to_db()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD6");
            await WithUnitOfWorkAsync(async () =>
            {
                var renk = await _erpAttributeRepository.InsertAsync(
                    new ProductAttribute(companyId, product.Id, "Renk", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new ProductAttributeValue(companyId, renk.Id, "Red", 0), autoSave: true);
                await _erpValueRepository.InsertAsync(new ProductAttributeValue(companyId, renk.Id, "Blue", 1), autoSave: true);
                var beden = await _erpAttributeRepository.InsertAsync(
                    new ProductAttribute(companyId, product.Id, "Beden", 1), autoSave: true);
                await _erpValueRepository.InsertAsync(new ProductAttributeValue(companyId, beden.Id, "Small", 0), autoSave: true);
            });

            // Axis HİÇ aktive edilmemiş kayıt (create'te eksen girilmedi).
            var created = await CreateChannelProductAsync(channel, product);

            var dto = await _appService.GetAsync(created.Id);

            // Taslak: ERP attribute/value'larından üretilir, TÜM Id'ler boş (persist YOK).
            dto.AttributeAxes.Count.ShouldBe(2);
            dto.AttributeAxes.Select(a => a.Name).ShouldBe(new[] { "Renk", "Beden" });
            dto.AttributeAxes.ShouldAllBe(a => a.Id == Guid.Empty);
            dto.AttributeAxes.Single(a => a.Name == "Renk").Values.Select(v => v.Value).ShouldBe(new[] { "Red", "Blue" });
            dto.AttributeAxes.Single(a => a.Name == "Beden").Values.Select(v => v.Value).ShouldBe(new[] { "Small" });
            dto.AttributeAxes.SelectMany(a => a.Values).ShouldAllBe(v => v.Id == Guid.Empty);

            // Salt-okuma DB'ye YAZMADI: eksen tablosu bu kayıt için hâlâ boş.
            (await GetAxesAsync(created.Id)).ShouldBeEmpty();
            (await GetSignedHeadersAsync(created.Id)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Get_for_erp_product_without_attributes_yields_empty_draft_axes()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "AXPROD7");
            var created = await CreateChannelProductAsync(channel, product);

            var dto = await _appService.GetAsync(created.Id);

            dto.AttributeAxes.ShouldBeEmpty();   // niteliksiz ürün → kullanıcı isterse elle eksen ekler
            (await GetAxesAsync(created.Id)).ShouldBeEmpty();
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
        SalesChannelTrN11 channel, Product product, params SalesChannelTrN11ProductAttributeAxisDto[] axes)
    {
        return await _appService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryExternalId = "1000846",
            ShipmentTemplateName = "Standart Teslimat",
            AttributeAxes = axes.ToList(),
        });
    }

    private static SalesChannelTrN11ProductAttributeAxisDto Axis(string name, int displayOrder, params string[] values)
    {
        return new SalesChannelTrN11ProductAttributeAxisDto
        {
            Name = name,
            DisplayOrder = displayOrder,
            Values = values
                .Select((v, i) => new SalesChannelTrN11ProductAttributeAxisValueDto { Value = v, DisplayOrder = i })
                .ToList(),
        };
    }

    /// <summary>Persist edilmiş eksen/değer grafını Update/Regenerate input DTO'suna çevirir (Id'ler dolu).</summary>
    private static List<SalesChannelTrN11ProductAttributeAxisDto> ToAxesInput(
        List<SalesChannelTrN11ProductAttributeAxis> axes,
        Dictionary<Guid, List<SalesChannelTrN11ProductAttributeAxisValue>> valuesByAxis)
    {
        return axes.Select(a => new SalesChannelTrN11ProductAttributeAxisDto
        {
            Id = a.Id,
            Name = a.Name,
            DisplayOrder = a.DisplayOrder,
            Values = (valuesByAxis.TryGetValue(a.Id, out var values) ? values : new List<SalesChannelTrN11ProductAttributeAxisValue>())
                .Select(v => new SalesChannelTrN11ProductAttributeAxisValueDto
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
            UnitType = dto.UnitType,
            UnitWeight = dto.UnitWeight,
            IsActive = dto.IsActive,
            SellerNote = dto.SellerNote,
            Description = dto.Description,
            GroupItemCode = dto.GroupItemCode,
            GroupAttribute = dto.GroupAttribute,
            ItemName = dto.ItemName,
            Attributes = dto.Attributes,
            SpecialInfo = dto.SpecialInfo,
            Variants = dto.Variants,
            AttributeAxes = dto.AttributeAxes,
        };
    }

    /// <summary>İmza formatı SNAPSHOT'ı: "{AxisId}={ValueId}|..." — çiftler AxisId'ye göre artan (Guid.CompareTo).</summary>
    private static string BuildExpectedSignature(params (Guid AxisId, Guid ValueId)[] pairs)
    {
        return string.Join('|', pairs.OrderBy(p => p.AxisId).Select(p => $"{p.AxisId}={p.ValueId}"));
    }

    private static List<(Guid AxisId, Guid ValueId)> ParseSignature(string signature)
    {
        return signature
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split('=');
                parts.Length.ShouldBe(2, $"imza çifti '{pair}' 'AxisId=ValueId' formatında olmalı");
                return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
            })
            .ToList();
    }

    private async Task<List<SalesChannelTrN11ProductAttributeAxis>> GetAxesAsync(Guid channelProductId)
    {
        var axes = await WithUnitOfWorkAsync(async () =>
            await _axisRepository.GetListAsync(a => a.SalesChannelTrN11ProductId == channelProductId));
        return axes.OrderBy(a => a.DisplayOrder).ToList();
    }

    private async Task<Dictionary<Guid, List<SalesChannelTrN11ProductAttributeAxisValue>>> GetAxisValuesAsync(List<Guid> axisIds)
    {
        var values = await WithUnitOfWorkAsync(async () =>
            await _axisValueRepository.GetListAsync(v => axisIds.Contains(v.AxisId)));
        return values
            .GroupBy(v => v.AxisId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.DisplayOrder).ToList());
    }

    private async Task<List<SalesChannelTrN11ProductVariant>> GetSignedHeadersAsync(Guid channelProductId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _headerRepository.GetListAsync(h =>
                h.SalesChannelTrN11ProductId == channelProductId && h.CombinationSignature != null));
    }
}
