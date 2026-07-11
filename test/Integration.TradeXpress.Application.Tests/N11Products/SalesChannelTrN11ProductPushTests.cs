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
/// N11 push plan üretimi (J3, 2026-07-09) — axis-modu aktifken SaveProduct stockItems'ının kombinasyon
/// (StockItem) satırlarından kurulduğunu kilitler: ERP-backed satırlar legacy davranışla (ERP kimlik/kod/nitelik),
/// N11-only satırlar StockItem.Id kimliği + Override fiyat/stok + kanal Attribute/Value adlarıyla push'a girer;
/// fiyatsız N11-only satır fail-fast. Ağ yok — <see cref="FakeN11ProductClient"/> push verisini yakalar.
/// </summary>
public abstract class SalesChannelTrN11ProductPushTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly ProductVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<ProductVariant, Guid> _erpVariantRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _headerRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly FakeN11ProductClient _fakeClient;

    protected SalesChannelTrN11ProductPushTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _erpSynchronizer = GetRequiredService<ProductVariantSynchronizer>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _erpAttributeRepository = GetRequiredService<IRepository<ProductAttribute, Guid>>();
        _erpValueRepository = GetRequiredService<IRepository<ProductAttributeValue, Guid>>();
        _erpVariantRepository = GetRequiredService<IRepository<ProductVariant, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _fakeClient = GetRequiredService<FakeN11ProductClient>();
    }

    // ── Axis-modu: 2 ERP-backed + 1 N11-only kombinasyon push'a girer ────────────────────────────────

    [Fact]
    public async Task Push_with_axis_mode_includes_erp_backed_and_n11_only_combinations_as_stock_items()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PUSHP1", greenPrice: 150m, greenStock: 5);

            await _appService.PushToN11Async(created.Id);

            var data = _fakeClient.LastSavedProduct.ShouldNotBeNull();
            data.StockItems.Count.ShouldBe(3);

            // ERP-backed satırlar legacy davranışla: dondurulacak kod "{VaryantKodu}-{SequenceNo}", fiyat/stok ERP'den.
            var red = data.StockItems.Single(s => s.SellerStockCode == "RED-1");
            red.OptionPrice.ShouldBe(100m);
            red.Quantity.ShouldBe(10);
            red.Attributes.ShouldContain(a => a.Name == "Renk" && a.Value == "Red");
            data.StockItems.ShouldContain(s => s.SellerStockCode == "BLUE-1");

            // N11-only satır: kod kombinasyon değer adlarından ("GREEN-1"), fiyat/stok Override'dan,
            // nitelikler KANAL Attribute.Name/AttributeValue.Value'larından çözülür.
            var green = data.StockItems.Single(s => s.SellerStockCode == "GREEN-1");
            green.OptionPrice.ShouldBe(150m);
            green.Quantity.ShouldBe(5);
            var greenAttribute = green.Attributes.ShouldHaveSingleItem();
            greenAttribute.Name.ShouldBe("Renk");
            greenAttribute.Value.ShouldBe("Green");

            // Base fiyat ilk (ana) ERP-backed adayın efektif fiyatı — N11-only listeyi bozmaz.
            data.Price.ShouldBe(100m);
        }
    }

    [Fact]
    public async Task Push_records_sku_row_with_stock_item_id_for_n11_only_combination()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PUSHP2", greenPrice: 150m, greenStock: 5);

            var pushed = await _appService.PushToN11Async(created.Id);

            // SKU kimlik sistemi: N11-only satırın Sku.ProductVariantId'si StockItem.Id taşır (kombinasyon kimliği).
            var greenHeader = (await WithUnitOfWorkAsync(async () =>
                    await _headerRepository.GetListAsync(h => h.SalesChannelTrN11ProductId == created.Id)))
                .Single(h => h.ProductVariantId is null);
            pushed.Skus.Count.ShouldBe(3);
            var greenSku = pushed.Skus.Single(s => s.SellerStockCode == "GREEN-1");
            greenSku.ProductVariantId.ShouldBe(greenHeader.Id);
            greenSku.LastSentQuantity.ShouldBe(5);
            greenSku.LastSentOptionPrice.ShouldBe(150m);
        }
    }

    // ── Fiyatsız N11-only satır: N11'e gitmeden fail-fast ────────────────────────────────────────────

    [Fact]
    public async Task Push_with_unpriced_n11_only_combination_fails_fast_before_reaching_n11()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // Override'sız N11-only satır (reconcile taze üretti; kullanıcı fiyat/stok girmedi).
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PUSHP3", greenPrice: null, greenStock: null);

            var exception = await Should.ThrowAsync<BusinessException>(() => _appService.PushToN11Async(created.Id));

            exception.Code.ShouldBe("TradeXpress:N11:StockItem:PriceMissingForPush");
            _fakeClient.SavedProducts.ShouldBeEmpty();   // N11'e HİÇ ulaşmadı
        }
    }

    // ── Legacy (özellik modu pasif): ERP-doğrudan yol regresyonsuz ───────────────────────────────────

    [Fact]
    public async Task Push_without_channel_attributes_uses_erp_variants_directly()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (channel, product) = await SeedChannelAndProductAsync(companyId, "PUSHP4");
            await SeedErpVariantsAsync(companyId, product, ("Red", 100m, 10), ("Blue", 100m, 20));

            var created = await CreateChannelProductAsync(channel, product);

            await _appService.PushToN11Async(created.Id);

            var data = _fakeClient.LastSavedProduct.ShouldNotBeNull();
            data.StockItems.Count.ShouldBe(2);
            data.StockItems.Select(s => s.SellerStockCode).ShouldBe(new[] { "RED-1", "BLUE-1" }, ignoreOrder: true);
            data.StockItems.Single(s => s.SellerStockCode == "BLUE-1").Quantity.ShouldBe(20);
        }
    }

    // ── Push önizlemesi: N11-only satır kaynak rozetiyle listeye girer ───────────────────────────────

    [Fact]
    public async Task Preview_with_axis_mode_lists_n11_only_row_with_override_price_and_channel_labels()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "PUSHP5", greenPrice: 150m, greenStock: 5);

            var preview = await _appService.GetPushPreviewAsync(created.Id);

            preview.Variants.Count.ShouldBe(3);
            var green = preview.Variants.Single(v => !v.IsErpBacked);
            green.Code.ShouldBe("GREEN");   // önizleme HAM kodu gösterir (legacy ERP satırı "RED" gibi) — "-{SequenceNo}" soneki push planında eklenir
            green.SalePrice.ShouldBe(150m);
            green.StockQuantity.ShouldBe(5);
            green.Options.ShouldBe("Renk: Green");
            preview.Variants.Count(v => v.IsErpBacked).ShouldBe(2);
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Axis-modlu senaryo çekirdeği: ERP Renk[Red,Blue] (fiyatlı/stoklu varyantlar) + kanal Renk[Red,Blue,Green]
    /// → reconcile 2 ERP-backed + 1 N11-only (Green) kombinasyon üretir; Green'e istenirse override yazılır.</summary>
    private async Task<SalesChannelTrN11ProductDto> SeedAxisProductWithN11OnlyRowAsync(
        Guid companyId, string productCode, decimal? greenPrice, int? greenStock)
    {
        var (channel, product) = await SeedChannelAndProductAsync(companyId, productCode);
        await SeedErpVariantsAsync(companyId, product, ("Red", 100m, 10), ("Blue", 100m, 20));

        var created = await CreateChannelProductAsync(channel, product, BuildAttribute("Renk", 0, "Red", "Blue", "Green"));

        if (greenPrice is not null || greenStock is not null)
        {
            var dto = await _appService.GetAsync(created.Id);
            var greenNode = dto.StockItems.Single(n => n.ProductVariantId is null);
            greenNode.OverridePrice = greenPrice;
            greenNode.OverrideStock = greenStock;
            await _appService.UpdateAsync(created.Id, BuildUpdateDto(dto));
        }

        return created;
    }

    private async Task<(SalesChannelTrN11 Channel, Product Product)> SeedChannelAndProductAsync(Guid companyId, string productCode)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _channelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{productCode}", $"N11 Kanal {productCode}", "app-key", "app-secret"),
                autoSave: true);
            var product = new Product(companyId, productCode, $"Urun {productCode}");
            // Push en az bir URL kaynaklı görsel ister (ImagesRequired guard'ı) — dış link, blob sağlayıcı gerekmez.
            product.SetImages(new[]
            {
                new ProductImage(ProductImageSourceType.Url, "https://example.com/product.jpg", null, null, 0, true),
            });
            await _productRepository.InsertAsync(product, autoSave: true);
            return (channel, product);
        });
    }

    /// <summary>ERP tarafını kurar: Renk ekseni + verilen değerler → synchronizer varyantları üretir; her varyanta
    /// fiyat/stok yazılır (push filtresi aktif + fiyatlı varyant ister).</summary>
    private async Task SeedErpVariantsAsync(Guid companyId, Product product, params (string Value, decimal Price, int Stock)[] values)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var attribute = await _erpAttributeRepository.InsertAsync(
                new ProductAttribute(companyId, product.Id, "Renk", 0), autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _erpValueRepository.InsertAsync(
                    new ProductAttributeValue(companyId, attribute.Id, values[i].Value, i), autoSave: true);
            }

            await _erpSynchronizer.SynchronizeAsync(product);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var variants = await _erpVariantRepository.GetListAsync(v => v.ProductId == product.Id);
            foreach (var (value, price, stock) in values)
            {
                var variant = variants.Single(v => v.Code == value.ToUpperInvariant());
                variant.SetSalePrice(price, null);
                variant.SetStock(stock);
                await _erpVariantRepository.UpdateAsync(variant, autoSave: true);
            }
        });
    }

    private async Task<SalesChannelTrN11ProductDto> CreateChannelProductAsync(
        SalesChannelTrN11 channel, Product product, params SalesChannelTrN11ProductAttributeDto[] channelAttributes)
    {
        return await _appService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryExternalId = FakeN11CategoryClient.DefaultCategoryExternalId,
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
}
