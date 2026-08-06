using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
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
/// N11 push plan üretimi (J3, 2026-07-09) — axis-modu aktifken SaveProduct stockItems'ının kombinasyon
/// (StockItem) satırlarından kurulduğunu kilitler: ERP-backed satırlar legacy davranışla (ERP kimlik/kod/nitelik),
/// N11-only satırlar StockItem.Id kimliği + Override fiyat/stok + kanal Attribute/Value adlarıyla push'a girer;
/// fiyatsız N11-only satır fail-fast. Ağ yok — <see cref="FakeN11ProductClient"/> push verisini yakalar.
/// </summary>
public abstract class SalesChannelTrN11ProductPushTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // Agnostik varyant tablosunda Product varyantları bu sahip-adıyla tutulur (production: ProductEntityName).
    private const string ProductEntityName = "Product";

    protected readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly EntityVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<EntityVariant, Guid> _erpVariantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _headerRepository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    protected readonly ICurrentCompany _currentCompany;
    // Push artik REST ten gidiyor (SOAP urun uclari N11 tarafinda kapatildi) → iddialar product-create
    // satirlari uzerinde. Yapisal fark: SOAP tek urun + icinde stockItems, REST her SKU icin AYRI satir.
    protected readonly FakeN11ProductRestClient _restClient;

    protected SalesChannelTrN11ProductPushTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _erpSynchronizer = GetRequiredService<EntityVariantSynchronizer>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _erpAttributeRepository = GetRequiredService<IRepository<EntityAttribute, Guid>>();
        _erpValueRepository = GetRequiredService<IRepository<EntityAttributeValue, Guid>>();
        _erpVariantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _variantDetailRepository = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _restClient = GetRequiredService<FakeN11ProductRestClient>();
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

            var rows = _restClient.LastCreatedRows;
            rows.Count.ShouldBe(3);

            // Tüm satırlar AYNI productMainId altında — REST'te varyantlığı kuran TEK mekanizma budur.
            rows.Select(r => r.ProductMainId).Distinct().Count().ShouldBe(1);

            // ERP-backed satırlar legacy davranışla: dondurulacak kod "{VaryantKodu}-{SequenceNo}", fiyat/stok ERP'den.
            // REST'te varyantın optionPrice'ı SATIRIN KENDİ salePrice'ı olur (ürün-seviyesi tek fiyat yok).
            var red = rows.Single(r => r.StockCode == "RED-1");
            red.SalePrice.ShouldBe(100m);
            red.Quantity.ShouldBe(10);
            // Nitelik artık ad/değer değil KATEGORİ KİMLİĞİ taşır. Sahte yaprakta "Renk" attributeId=1 ve
            // isCustomValue=true (değer listesi yok) → serbest metin customValue'ya yazılır.
            red.Attributes.ShouldContain(a => a.Id == 1 && a.CustomValue == "Red");
            rows.ShouldContain(r => r.StockCode == "BLUE-1");

            // N11-only satır: kod kombinasyon değer adlarından ("GREEN-1"), fiyat/stok Override'dan,
            // nitelikler KANAL Attribute.Name/AttributeValue.Value'larından çözülür.
            var green = rows.Single(r => r.StockCode == "GREEN-1");
            green.SalePrice.ShouldBe(150m);
            green.Quantity.ShouldBe(5);
            var greenAttribute = green.Attributes.ShouldHaveSingleItem();
            greenAttribute.Id.ShouldBe(1);
            greenAttribute.CustomValue.ShouldBe("Green");

            // NOT: SOAP'ta ürün-seviyesi tek bir Price vardı ve ilk adayın fiyatıydı. REST'te böyle bir alan YOK —
            // her satır kendi fiyatını taşır (yukarıda satır satır doğrulandı).
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
            _restClient.CreatedBatches.ShouldBeEmpty();   // N11'e HİÇ ulaşmadı
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

            var rows = _restClient.LastCreatedRows;
            rows.Count.ShouldBe(2);
            rows.Select(r => r.StockCode).ShouldBe(new[] { "RED-1", "BLUE-1" }, ignoreOrder: true);
            rows.Single(r => r.StockCode == "BLUE-1").Quantity.ShouldBe(20);
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
    protected async Task<SalesChannelTrN11ProductDto> SeedAxisProductWithN11OnlyRowAsync(
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
            await _productRepository.InsertAsync(product, autoSave: true);

            // Push en az bir görsel ister (ImagesRequired guard'ı). Kaynak merkezi DAM: kütüphane kaydı + ürün
            // bağlamına link. Bu testlerin konusu görsel DEĞİL varyant/SKU eşlemesi — tek kapak görseli yeter.
            var media = await _mediaRepository.InsertAsync(
                new Media(
                    companyId,
                    MediaType.Image,
                    blobName: Guid.NewGuid().ToString("N"),
                    fileName: $"{productCode}.jpg",
                    contentType: "image/jpeg",
                    size: 1024,
                    contentHash: Guid.NewGuid().ToString("N")),
                autoSave: true);

            await _linkRepository.InsertAsync(
                new EntityMediaLink(
                    companyId, MediaEntityNames.Product, product.Id, media.Id, displayOrder: 0, isDefault: true, isActive: true),
                autoSave: true);

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
                new EntityAttribute(companyId, ProductEntityName, product.Id, "Renk", 0), autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _erpValueRepository.InsertAsync(
                    new EntityAttributeValue(companyId, attribute.Id, values[i].Value, i), autoSave: true);
            }

            await _erpSynchronizer.SynchronizeAsync(ProductEntityName, product.Id, companyId, product.Name);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var variants = await _erpVariantRepository.GetListAsync(
                v => v.EntityName == ProductEntityName && v.EntityId == product.Id);
            foreach (var (value, price, stock) in values)
            {
                var variant = variants.Single(v => v.Code == value.ToUpperInvariant());
                variant.SetStock(stock);   // stok agnostik EntityVariant'ta kalır
                await _erpVariantRepository.UpdateAsync(variant, autoSave: true);

                // Satış fiyatı Product uzantısı ProductVariantDetail'de (1:1, EntityVariantId) — synchronizer detay
                // üretmez, testin fiyat kurulumu buradan yazılır (production LoadVariantSalePricesAsync ile aynı yol).
                var detail = new ProductVariantDetail(companyId, variant.Id);
                detail.SetSalePrice(price, null);

                // PUSH KAPISI (2026-08-05): varyant push aday listesine ancak İNSAN onayıyla girer
                // (ProductSaleStatus.Ready + damgası güncel). Bu fixture'ın premisi "varyantlar onaylıdır" —
                // testlerin konusu doğrulama akışı DEĞİL, push planı. Premis burada AÇIKÇA ilan edilir;
                // aksi halde tüm push testleri "aday yok" diye düşerdi ve sebebi görünmezdi.
                // Tohum anında reçete satırı yok → boş-reçete damgası.
                detail.MarkVerified(
                    RecipeVerificationStamp.EmptyRecipe, DateTime.UtcNow, verifiedBy: null);

                await _variantDetailRepository.InsertAsync(detail, autoSave: true);
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
            // REST push KDV oranını ZORUNLU kılıyor (create'te "Evet"); boşsa mapper fail-fast eder.
            VatRate = 20,
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

    protected static SalesChannelTrN11ProductUpdateDto BuildUpdateDto(SalesChannelTrN11ProductDto dto)
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
            // KDV oranı güncellemede de TAŞINMALI: aktarılmazsa alan null'a düşer ve REST push fail-fast eder
            // (create'te zorunlu). Bunu unutmak "kaydettim, sonra push patladı" tablosunu üretirdi.
            VatRate = dto.VatRate,
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
