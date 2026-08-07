using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products.Rest;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 mağazasından İÇE AKTARMA testleri — sahte <c>product-query</c> istemcisiyle (ağ yok, salt-okuma ilkesi)
/// uçtan uca: ilk içe aktarım TAM ZİNCİRİ (şablon Product + varyantlar + kanal kaydı + SKU + StockItem override)
/// üretir, ikinci içe aktarım İDEMPOTENT'tir, ve kurulamayan satırlar SESSİZ GEÇİLMEZ.
///
/// <para><b>En kritik değişmez burada kilitleniyor — UZAK STOK KODU DONDURULUR.</b> Kod bizim üretim kuralımıza
/// ("{VaryantKodu}-{SequenceNo}") çevrilseydi sonraki push var olan SKU'yu güncellemek yerine İKİNCİ bir SKU
/// açardı: kullanıcının mağazasında aynı ürün iki kez listelenirdi ve bu ancak N11 panelinde fark edilirdi.</para>
/// </summary>
public abstract class SalesChannelTrN11ProductImportTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // Agnostik varyant tablosunda Product varyantları bu sahip-adıyla tutulur (production: ProductEntityName).
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly FakeN11ProductQueryClient _queryClient;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _channelProductRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _headerRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrN11ProductImportTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _queryClient = GetRequiredService<FakeN11ProductQueryClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrN11Product, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task First_import_builds_the_whole_chain_from_a_flat_rest_response()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP1");
            SetRemoteRows(
                Row("ALYANS-14A", "ALYANS", "Alyans 14 Ayar", price: 4500m, quantity: 3, n11ProductId: 111),
                Row("ALYANS-18A", "ALYANS", "Alyans 18 Ayar", price: 5900m, quantity: 2, n11ProductId: 112));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            // REST düz satır döner; varyantlığı yalnız ortak productMainId kurar → 2 satır = 1 ürün.
            report.TotalFetchedItems.ShouldBe(2);
            report.TotalRemoteProducts.ShouldBe(1);
            report.CreatedProducts.ShouldBe(1);
            report.CreatedChannelProducts.ShouldBe(1);

            var channelProduct = await SingleChannelProductAsync();
            channelProduct.Skus.Count.ShouldBe(2);

            var product = await WithUnitOfWorkAsync(async () => await _productRepository.GetAsync(channelProduct.ProductId));
            product.Name.ShouldBe("Alyans 14 Ayar");   // casing KORUNUR (TitleCase normalizasyonu yok)

            var variants = await LoadVariantsAsync(product.Id);
            variants.Count.ShouldBe(2);
            variants.Count(v => v.IsMain).ShouldBe(1);   // tekil-main değişmezi
            variants.Select(v => v.StockQuantity).OrderBy(q => q).ShouldBe(new[] { 2, 3 });
        }
    }

    /// <summary>Uzak stok kodu SKU satırına OLDUĞU GİBİ yazılmalı. Bizim üretim kuralımıza çevrilirse
    /// ("{VaryantKodu}-{SequenceNo}") sonraki push N11'de var olan SKU'yu bulamaz ve ikinci bir listeleme açar.</summary>
    [Fact]
    public async Task Imported_sku_keeps_the_remote_stock_code_verbatim()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP2");
            SetRemoteRows(Row("N11-SKU-XYZ", "GRUP-1", "Kolye", price: 1200m, quantity: 1, n11ProductId: 900));

            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var channelProduct = await SingleChannelProductAsync();
            var sku = channelProduct.Skus.ShouldHaveSingleItem();
            sku.SellerStockCode.ShouldBe("N11-SKU-XYZ");

            // NOT: eski negatif iddia ("BuildStockCode çıktısına eşit OLMAMALI") 2026-08-07'de anlamsızlaştı —
            // SequenceNo=1'de üretim kuralı da ÇIPLAK kodu döndürür (ChannelSequenceCode), iki yol bilinçli çakışır.
            // Asıl niyet üstteki pozitif iddiayla korunuyor: kod uzaktan geldiği gibi, DÖNÜŞTÜRÜLMEDEN yazılır.
            sku.N11SkuId.ShouldBe(900);
        }
    }

    /// <summary>Kanal kaydının SellerCode'u uzak productMainId'den alınmalı — üretilmemeli. SellerCode N11'in
    /// upsert kimliğidir (push'ta productMainId olarak gider); uydurulursa push var olan listelemeyi güncellemek
    /// yerine yeni bir listeleme açar.</summary>
    [Fact]
    public async Task Channel_record_adopts_the_remote_product_main_id_as_its_upsert_identity()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP3");
            SetRemoteRows(Row("SKU-A", "UZAK-ANA-KOD", "Bilezik", price: 800m, quantity: 4, n11ProductId: 55));

            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var channelProduct = await SingleChannelProductAsync();
            channelProduct.SellerCode.ShouldBe("UZAK-ANA-KOD");
            channelProduct.N11ProductId.ShouldBe(55);
        }
    }

    [Fact]
    public async Task Second_import_updates_instead_of_duplicating()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP4");
            SetRemoteRows(Row("SKU-B", "GRUP-B", "Yüzük", price: 1000m, quantity: 5, n11ProductId: 77));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            // İkinci turda uzak fiyat/stok değişti — kayıt GÜNCELLENMELİ, çoğalmamalı.
            SetRemoteRows(Row("SKU-B", "GRUP-B", "Yüzük", price: 1350m, quantity: 2, n11ProductId: 77));
            var second = await _appService.ImportFromMarketplaceAsync(channel.Id);

            second.CreatedProducts.ShouldBe(0);
            second.CreatedChannelProducts.ShouldBe(0);
            second.UpdatedChannelProducts.ShouldBe(1);
            second.AddedVariants.ShouldBe(0);

            (await AllChannelProductsAsync()).Count.ShouldBe(1);
            var channelProduct = await SingleChannelProductAsync();
            channelProduct.Skus.Count.ShouldBe(1);

            var header = (await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrN11ProductId == channelProduct.Id))).ShouldHaveSingleItem();
            header.OverridePrice.ShouldBe(1350m);
        }
    }

    /// <summary>Çekirdek (ERP) stok pazaryerinin anlık verisiyle EZİLMEZ — fark kanal override'ına yazılır ve
    /// raporda sayılır (K12 politikası; Trendyol içe aktarımıyla aynı kural).</summary>
    [Fact]
    public async Task Remote_stock_never_overwrites_core_stock_on_a_later_import()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP5");
            SetRemoteRows(Row("SKU-C", "GRUP-C", "Küpe", price: 600m, quantity: 10, n11ProductId: 88));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            SetRemoteRows(Row("SKU-C", "GRUP-C", "Küpe", price: 600m, quantity: 1, n11ProductId: 88));
            var second = await _appService.ImportFromMarketplaceAsync(channel.Id);

            second.StockDifferenceCount.ShouldBe(1);

            var channelProduct = await SingleChannelProductAsync();
            var variants = await LoadVariantsAsync(channelProduct.ProductId);
            variants.ShouldHaveSingleItem().StockQuantity.ShouldBe(10);   // çekirdek KORUNDU

            var header = (await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrN11ProductId == channelProduct.Id))).ShouldHaveSingleItem();
            header.OverrideStock.ShouldBe(1);   // kanal gerçeği override'a yazıldı
        }
    }

    /// <summary>Uzakta doğan yeni SKU mevcut şablona varyant olarak EKLENİR (ekleme-only; ana varyant değişmez).</summary>
    [Fact]
    public async Task New_remote_sku_is_added_to_the_existing_template_as_a_variant()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP6");
            SetRemoteRows(Row("SKU-D1", "GRUP-D", "Set", price: 300m, quantity: 1, n11ProductId: 21));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var mainBefore = (await LoadVariantsAsync((await SingleChannelProductAsync()).ProductId))
                .Single(v => v.IsMain).Id;

            SetRemoteRows(
                Row("SKU-D1", "GRUP-D", "Set", price: 300m, quantity: 1, n11ProductId: 21),
                Row("SKU-D2", "GRUP-D", "Set", price: 350m, quantity: 4, n11ProductId: 22));
            var second = await _appService.ImportFromMarketplaceAsync(channel.Id);

            second.AddedVariants.ShouldBe(1);
            second.AddedStockCodes.ShouldContain("SKU-D2");

            var channelProduct = await SingleChannelProductAsync();
            channelProduct.Skus.Count.ShouldBe(2);

            var variants = await LoadVariantsAsync(channelProduct.ProductId);
            variants.Count.ShouldBe(2);
            variants.Single(v => v.IsMain).Id.ShouldBe(mainBefore);   // ANA VARYANT DEĞİŞMEZ
        }
    }

    /// <summary>Stok kodu olmayan satır satırın kimliğini kaybettirir → atlanır ve RAPORLANIR (sessiz geçilmez).</summary>
    [Fact]
    public async Task Row_without_a_stock_code_is_skipped_and_reported()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP7");
            SetRemoteRows(
                Row(string.Empty, "GRUP-E", "Kimliksiz", price: 100m, quantity: 1, n11ProductId: 31),
                Row("SKU-E", "GRUP-E", "Sağlam", price: 200m, quantity: 1, n11ProductId: 32));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.SkippedRows.ShouldHaveSingleItem().Title.ShouldBe("Kimliksiz");
            report.CreatedChannelProducts.ShouldBe(1);   // sağlam satır yine de içe alındı
        }
    }

    /// <summary>Kategorisiz grup kurulamaz (entity kategori ZORUNLU kılar) → atlanır ve raporlanır. Sahte bir
    /// kategori uydurmak ürünü yanlış yere listelerdi.</summary>
    [Fact]
    public async Task Group_without_a_category_is_skipped_and_reported()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP8");
            SetRemoteRows(Row("SKU-F", "GRUP-F", "Kategorisiz", price: 100m, quantity: 1, n11ProductId: 41, categoryId: null));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.SkippedRows.ShouldHaveSingleItem();
            report.CreatedChannelProducts.ShouldBe(0);
            (await AllChannelProductsAsync()).ShouldBeEmpty();
        }
    }

    /// <summary>KDV uzak yanıtta YOK — uydurulmaz, boş bırakılır ve kullanıcı uyarılır. Sessiz bir "%20 standarttır"
    /// varsayımı kıymetli madende yanlış fatura + satıcıya rücu demektir.</summary>
    [Fact]
    public async Task Vat_rate_is_left_empty_and_the_user_is_warned()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP9");
            SetRemoteRows(Row("SKU-G", "GRUP-G", "Külçe", price: 50000m, quantity: 1, n11ProductId: 51));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            (await SingleChannelProductAsync()).VatRate.ShouldBeNull();
            report.Warnings.ShouldNotBeEmpty();
        }
    }

    /// <summary>Aynı stok kodunun tekrar gelmesi bir sayfalama artefaktıdır (istemci bunu açıkça uyarır) — satır
    /// başına gürültü üretilmez ama toplam SESSİZ de geçilmez.</summary>
    [Fact]
    public async Task Duplicate_stock_codes_collapse_to_one_row_and_are_summarised_as_a_warning()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP10");
            SetRemoteRows(
                Row("SKU-H", "GRUP-H", "Tekrar", price: 100m, quantity: 1, n11ProductId: 61),
                Row("SKU-H", "GRUP-H", "Tekrar", price: 100m, quantity: 1, n11ProductId: 61));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.TotalFetchedItems.ShouldBe(2);
            report.SkippedRows.ShouldBeEmpty();          // satır-başına gürültü YOK
            report.Warnings.Count.ShouldBeGreaterThan(1);  // KDV uyarısı + tekrar özeti
            (await SingleChannelProductAsync()).Skus.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Empty_store_returns_an_empty_report_without_touching_anything()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP11");
            SetRemoteRows();

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.TotalFetchedItems.ShouldBe(0);
            report.TotalRemoteProducts.ShouldBe(0);
            report.CreatedProducts.ShouldBe(0);
            (await AllChannelProductsAsync()).ShouldBeEmpty();
        }
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────────

    private static N11RestProductSummary Row(
        string stockCode,
        string? productMainId,
        string title,
        decimal price,
        int quantity,
        long n11ProductId,
        string? categoryId = "1001")
    {
        return new N11RestProductSummary(
            N11ProductId: n11ProductId,
            ProductMainId: productMainId,
            StockCode: stockCode,
            Title: title,
            SalePrice: price,
            ListPrice: price,
            Quantity: quantity,
            SaleStatus: "On_Sale",
            ProductStatus: "Active",
            CategoryId: categoryId,
            ImageUrls: Array.Empty<string>());
    }

    private void SetRemoteRows(params N11RestProductSummary[] rows)
    {
        _queryClient.Page = new N11RestProductPage(rows, 0, rows.Length == 0 ? 0 : 1, rows.Length);
    }

    private async Task<SalesChannelTrN11> SeedChannelAsync(Guid companyId, string code)
    {
        return await WithUnitOfWorkAsync(async () => await _channelRepository.InsertAsync(
            new SalesChannelTrN11(companyId, $"N11-{code}", $"N11 Kanal {code}", "app-key", "app-secret"),
            autoSave: true));
    }

    private async Task<List<SalesChannelTrN11Product>> AllChannelProductsAsync()
    {
        return await WithUnitOfWorkAsync(async () => await _channelProductRepository.GetListAsync());
    }

    private async Task<SalesChannelTrN11Product> SingleChannelProductAsync()
    {
        return (await AllChannelProductsAsync()).ShouldHaveSingleItem();
    }

    private async Task<List<EntityVariant>> LoadVariantsAsync(Guid productId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == productId));
    }
}
