using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Pazaryerinden İÇE AKTARMA testleri (Trendyol_ProductSync) — sahte client'la (ağ yok, READ-ONLY ilke) uçtan uca:
/// ilk import TAM ZİNCİRİ (şablon Product + varyantlar + kanal grafı + StockItem override + Sku) üretir; ikinci
/// import İDEMPOTENT'tir (dublike yok, yalnız kanal grafı güncellenir); kullanıcı-düzenlenmiş şablon alanı EZİLMEZ;
/// eşleşmeyen kategori/geçersiz kalem RAPORLANIR (sessiz geçilmez); ProductVariant.Barcode filtered unique index'i
/// duplike barkodu DB seviyesinde reddeder.
/// </summary>
public abstract class SalesChannelTrTrendyolProductImportTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly FakeTrendyolProductClient _fakeClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _headerRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrTrendyolProductImportTests()
    {
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _fakeClient = GetRequiredService<FakeTrendyolProductClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<ProductVariant, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItem, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── İlk import: tam zincir ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task First_import_creates_template_product_variants_and_channel_graph()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP1");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-1", barcode: "BR-RED-1", stockCode: "stk red 1", title: "iPhone 15 Deri Kılıf",
                quantity: 7, salePrice: 1299.90m, listPrice: 1500.50m, contentId: 987001, approved: true));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-1", barcode: "BR-BLUE-1", stockCode: "STK-BLUE-1", title: "iPhone 15 Deri Kılıf",
                quantity: 3, salePrice: 1349.90m, listPrice: null, contentId: 987002, approved: true));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.TotalFetchedItems.ShouldBe(2);
            report.TotalRemoteProducts.ShouldBe(1);
            report.CreatedProducts.ShouldBe(1);
            report.CreatedChannelProducts.ShouldBe(1);
            report.UpdatedChannelProducts.ShouldBe(0);
            report.SkippedRows.ShouldBeEmpty();
            report.UnmatchedCategories.ShouldNotBeEmpty();   // yerel kategori ağacı boş → 411 eşleşmedi (raporlanır)

            // Şablon Product: Code stockCode'dan normalize (UPPER, boşluk korunur), Name CASING KORUNMUŞ başlık.
            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            product.Code.ShouldBe("STK RED 1");
            product.Name.ShouldBe("iPhone 15 Deri Kılıf");   // TitleCase EZMEDİ (SetName normalizeTitle:false yolu)
            product.Images.Count.ShouldBe(1);
            product.Images[0].Url.ShouldBe("https://cdn.example.com/img-BR-RED-1.jpg");

            // Varyantlar: kalem başına bir tane; barcode ticari kimliğe yazıldı; İLK kalem MAIN.
            var variants = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.ProductId == product.Id));
            variants.Count.ShouldBe(2);
            var red = variants.Single(v => v.Barcode == "BR-RED-1");
            var blue = variants.Single(v => v.Barcode == "BR-BLUE-1");
            red.IsMain.ShouldBeTrue();
            blue.IsMain.ShouldBeFalse();
            red.Name.ShouldBe("iPhone 15 Deri Kılıf");   // varyant adında da TitleCase EZMEDİ (SetName normalizeTitle:false)
            red.SalePrice.ShouldBe(1299.90m);
            red.StockQuantity.ShouldBe(7);
            blue.SalePrice.ShouldBe(1349.90m);
            blue.StockQuantity.ShouldBe(3);

            // Kanal kaydı: RemoteProductMainId (Trendyol anahtarı) + bizim ProductMainId'imiz AYRI üretildi;
            // kategori HAM yazıldı; Sku'lar remote barcode'la (frozen) + contentId'yle işlendi.
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.RemoteProductMainId.ShouldBe("MAIN-1");
            record.ProductMainId.ShouldBe($"{product.Code}-1");
            record.CategoryId.ShouldBe("411");
            record.BrandId.ShouldBe("82");
            record.VatRate.ShouldBe(20);
            record.RemoteApproved.ShouldBe(true);
            record.ListPrice.ShouldBe(1500.50m);
            record.Skus.Count.ShouldBe(2);
            record.Skus.Single(s => s.Barcode == "BR-RED-1").RemoteContentId.ShouldBe(987001);
            record.Skus.Single(s => s.Barcode == "BR-RED-1").ProductVariantId.ShouldBe(red.Id);
            record.Attributes.ShouldContain(a => a.AttributeId == 47 && a.AttributeValueId == 686234);

            // StockItem override: uzak fiyat/stok kanal katmanına yazıldı (kullanıcı onaylı yön).
            var headers = await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id));
            headers.Count.ShouldBe(2);
            var redHeader = headers.Single(h => h.ProductVariantId == red.Id);
            redHeader.OverridePrice.ShouldBe(1299.90m);
            redHeader.OverrideStock.ShouldBe(7);
        }
    }

    // ── İdempotency: ikinci import dublike üretmez, kanal grafını günceller ──────────────────────────

    [Fact]
    public async Task Second_import_updates_channel_graph_without_duplicating_products_or_records()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP2");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-2", barcode: "BR-X-1", stockCode: "STK-X-1", title: "Altın Kolye 14 Ayar",
                quantity: 5, salePrice: 4200m, listPrice: 4500m, contentId: 1, approved: true));

            var first = await _appService.ImportFromMarketplaceAsync(channel.Id);
            first.CreatedProducts.ShouldBe(1);
            first.CreatedChannelProducts.ShouldBe(1);

            // Uzak fiyat/stok değişti → ikinci geçiş yalnız kanal grafını tazelemeli.
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-2", barcode: "BR-X-1", stockCode: "STK-X-1", title: "Altın Kolye 14 Ayar",
                quantity: 9, salePrice: 4650m, listPrice: 4900m, contentId: 1, approved: true));

            var second = await _appService.ImportFromMarketplaceAsync(channel.Id);

            second.CreatedProducts.ShouldBe(0);
            second.CreatedChannelProducts.ShouldBe(0);
            second.UpdatedChannelProducts.ShouldBe(1);

            (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).Count.ShouldBe(1);
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.Skus.Count.ShouldBe(1);   // aynı barcode → aynı Sku satırı (dublike yok)
            record.ListPrice.ShouldBe(4900m);

            var header = (await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id))).ShouldHaveSingleItem();
            header.OverridePrice.ShouldBe(4650m);
            header.OverrideStock.ShouldBe(9);
        }
    }

    // ── Kullanıcı emeği korunur: şablon alanları ikinci import'ta EZİLMEZ ────────────────────────────

    [Fact]
    public async Task Second_import_does_not_overwrite_user_edited_template_fields()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP3");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-3", barcode: "BR-Y-1", stockCode: "STK-Y-1", title: "Orijinal Başlık",
                quantity: 2, salePrice: 100m, listPrice: null, contentId: 5, approved: null));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            // Kullanıcı şablonu düzenledi (ad + varyant fiyatı).
            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _productRepository.GetAsync(product.Id);
                tracked.SetName("Kullanıcı Ürün Adı", normalizeTitle: false);
                await _productRepository.UpdateAsync(tracked, autoSave: true);

                var variant = (await _variantRepository.GetListAsync(v => v.ProductId == product.Id)).Single();
                variant.SetSalePrice(999m, null);
                await _variantRepository.UpdateAsync(variant, autoSave: true);
                return true;
            });

            // Uzakta başlık/fiyat değişti — ikinci import şablonu/varyantı EZMEMELİ (yalnız kanal grafı).
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-3", barcode: "BR-Y-1", stockCode: "STK-Y-1", title: "Uzakta Değişen Başlık",
                quantity: 4, salePrice: 150m, listPrice: null, contentId: 5, approved: null));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var after = await WithUnitOfWorkAsync(async () => await _productRepository.GetAsync(product.Id));
            after.Name.ShouldBe("Kullanıcı Ürün Adı");   // şablon korunur

            var variantAfter = (await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.ProductId == product.Id))).ShouldHaveSingleItem();
            variantAfter.SalePrice.ShouldBe(999m);       // ERP varyant fiyatı korunur

            // Uzak fiyat kanal katmanına gitti (OverridePrice) — kullanıcı onaylı yön.
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            var header = (await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id))).ShouldHaveSingleItem();
            header.OverridePrice.ShouldBe(150m);
            header.OverrideStock.ShouldBe(4);
        }
    }

    // ── Rapor: geçersiz/duplike kalemler sessiz geçilmez ─────────────────────────────────────────────

    [Fact]
    public async Task Invalid_and_duplicate_barcodes_are_skipped_and_reported()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP4");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-4", barcode: "BR-OK-1", stockCode: "STK-OK-1", title: "Geçerli Kalem",
                quantity: 1, salePrice: 10m, listPrice: null, contentId: 1, approved: null));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-4", barcode: "BR-OK-1", stockCode: "STK-OK-2", title: "Duplike Barkod",
                quantity: 1, salePrice: 10m, listPrice: null, contentId: 2, approved: null));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-5", barcode: new string('X', 100), stockCode: "STK-LONG", title: "Uzun Barkod",
                quantity: 1, salePrice: 10m, listPrice: null, contentId: 3, approved: null));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.SkippedRows.Count.ShouldBe(2);
            report.SkippedRows.ShouldContain(s => s.Barcode == "BR-OK-1" && s.StockCode == "STK-OK-2");
            report.SkippedRows.ShouldContain(s => s.StockCode == "STK-LONG");
            report.CreatedChannelProducts.ShouldBe(1);   // geçerli kalem yine de işlendi
        }
    }

    // ── Barcode filtered unique index (idempotent upsert'in bel kemiği) ─────────────────────────────

    // NOT: test TENANT bağlamında koşar — SQLite (test DB) unique index'te NULL'ları SQL-standardına göre AYRI
    // sayar (NULL TenantId'li iki satır çakışmaz), SQL Server (prod) ise NULL'u değer sayıp host tarafını da
    // engeller. Tenant'lı satırda davranış iki sağlayıcıda AYNI → kilit oraya kurulur.
    // NOT-2 (kabul edilmiş sağlayıcı farkı): barcode karşılaştırmasının CASE davranışı da sağlayıcıya bağlı —
    // SQL Server (CI collation) 'br-1'='BR-1' sayar, SQLite (BINARY) saymaz. Tekdüzeleştirme ya sargable olmayan
    // UPPER() sorgusu ya da normalize-barcode kolonu (migration) ister; ikisi de bu dilimde bilinçli ertelendi.
    [Fact]
    public async Task Duplicate_variant_barcode_in_same_tenant_is_rejected_by_unique_index()
    {
        var companyId = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(Guid.NewGuid()))
        using (_currentCompany.Change(companyId))
        {
            var (productA, productB) = await WithUnitOfWorkAsync(async () =>
            {
                var a = await _productRepository.InsertAsync(new Product(companyId, "BARCODEA", "Urun A"), autoSave: true);
                var b = await _productRepository.InsertAsync(new Product(companyId, "BARCODEB", "Urun B"), autoSave: true);
                return (a, b);
            });

            await WithUnitOfWorkAsync(async () =>
            {
                var v1 = new ProductVariant(companyId, productA.Id, "VARA", "Varyant A");
                v1.SetTradeIdentifiers("BR-UNIQUE-1", null, null, null);
                await _variantRepository.InsertAsync(v1, autoSave: true);
                return true;
            });

            Exception? caught = null;
            try
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var v2 = new ProductVariant(companyId, productB.Id, "VARB", "Varyant B");
                    v2.SetTradeIdentifiers("BR-UNIQUE-1", null, null, null);
                    await _variantRepository.InsertAsync(v2, autoSave: true);
                    return true;
                });
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            caught.ShouldNotBeNull();   // filtered unique index (TenantId, Barcode) duplikeyi DB'de reddetti

            // Barcode'suz (NULL) satırlar filtreye takılmaz — ikinci null-barcode varyant serbest.
            await WithUnitOfWorkAsync(async () =>
            {
                var v3 = new ProductVariant(companyId, productB.Id, "VARC", "Varyant C");
                await _variantRepository.InsertAsync(v3, autoSave: true);
                var v4 = new ProductVariant(companyId, productB.Id, "VARD", "Varyant D");
                await _variantRepository.InsertAsync(v4, autoSave: true);
                return true;
            });
        }
    }

    // ── Tenant-içi çapraz-şirket barcode çakışması: import ÇÖKMEZ, kalem atla+raporla ────────────────

    [Fact]
    public async Task Barcode_owned_by_another_company_in_same_tenant_is_skipped_and_reported()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(Guid.NewGuid()))
        {
            // Şirket A: 'BR-SHARED-1' barkodlu varyant sahibi.
            using (_currentCompany.Change(companyA))
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var product = await _productRepository.InsertAsync(new Product(companyA, "OWNEDA", "Urun A"), autoSave: true);
                    var variant = new ProductVariant(companyA, product.Id, "VARA", "Varyant A");
                    variant.SetTradeIdentifiers("BR-SHARED-1", null, null, null);
                    await _variantRepository.InsertAsync(variant, autoSave: true);
                    return true;
                });
            }

            // Şirket B: aynı tenant'ta aynı barkodu Trendyol'dan import eder — unique index (TenantId, Barcode)
            // ihlaliyle çökmek YERİNE kalem atlanıp raporlanmalı; grubun diğer kalemi normal işlenmeli.
            using (_currentCompany.Change(companyB))
            {
                var channel = await SeedChannelAsync(companyB, "IMP6");
                _fakeClient.RemoteItems.Clear();
                _fakeClient.RemoteItems.Add(BuildRemoteItem(
                    mainId: "MAIN-6", barcode: "BR-SHARED-1", stockCode: "STK-S-1", title: "Çakışan Kalem",
                    quantity: 1, salePrice: 10m, listPrice: null, contentId: 1, approved: null));
                _fakeClient.RemoteItems.Add(BuildRemoteItem(
                    mainId: "MAIN-7", barcode: "BR-FREE-1", stockCode: "STK-F-1", title: "Serbest Kalem",
                    quantity: 2, salePrice: 20m, listPrice: null, contentId: 2, approved: null));

                var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

                report.SkippedRows.ShouldContain(s => s.Barcode == "BR-SHARED-1");
                report.CreatedProducts.ShouldBe(1);          // yalnız serbest kalem şablon üretti
                report.CreatedChannelProducts.ShouldBe(1);

                var products = await WithUnitOfWorkAsync(async () =>
                    await _productRepository.GetListAsync(p => p.CompanyId == companyB));
                products.ShouldHaveSingleItem().Code.ShouldBe("STK-F-1");
            }
        }
    }

    // ── Anomalili uzak kalem (negatif fiyat / taşan stockCode / taşan kategori id) importu DÜŞÜRMEZ ──

    [Fact]
    public async Task Anomalous_remote_fields_do_not_abort_import()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP7");
            _fakeClient.RemoteItems.Clear();
            var anomalous = BuildRemoteItem(
                mainId: "MAIN-8", barcode: "BR-ANOM-1", stockCode: new string('S', 120), title: "Anomalili Kalem",
                quantity: -3, salePrice: -5m, listPrice: null, contentId: 9, approved: null);
            _fakeClient.RemoteItems.Add(anomalous with { CategoryId = new string('9', 40) });

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            // Kalem İŞLENDİ (atlanmadı): negatif fiyat null'a, negatif stok 0'a süzüldü; taşan alanlar onarıldı.
            report.CreatedProducts.ShouldBe(1);
            report.CreatedChannelProducts.ShouldBe(1);

            var variant = (await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.CompanyId == companyId))).ShouldHaveSingleItem();
            variant.SalePrice.ShouldBeNull();       // negatif uzak fiyat upsert guard'ıyla AYNI şekilde süzüldü
            variant.StockQuantity.ShouldBe(0);

            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.CategoryId.ShouldBe("0");        // 32 sınırını aşan uzak kategori id → sentinel (raporlu)
            record.Skus.ShouldHaveSingleItem().StockCode.Length.ShouldBe(100);   // taşan stockCode kırpıldı
            report.UnmatchedCategories.ShouldNotBeEmpty();
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private async Task<SalesChannelTrTrendyol> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol Kanal {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true));
    }

    /// <summary>DÜZ uzak kalem kurar (parse çıktısının aynısı — tek varyant taşır; gruplama import içinde).</summary>
    private static TrendyolRemoteProduct BuildRemoteItem(
        string? mainId, string barcode, string? stockCode, string title,
        int quantity, decimal? salePrice, decimal? listPrice, long contentId, bool? approved)
    {
        return new TrendyolRemoteProduct(
            ProductMainId: mainId,
            Title: title,
            Description: "İçe aktarma testi için yeterince uzun açıklama metni.",
            CategoryId: "411",
            CategoryName: "Telefon Kılıfı",
            BrandId: "82",
            BrandName: "MarkaX",
            VatRate: 20,
            DimensionalWeight: 0.5m,
            DeliveryDuration: 2,
            ImageUrls: new List<string> { $"https://cdn.example.com/img-{barcode}.jpg" },
            Variants: new List<TrendyolRemoteVariant>
            {
                new(
                    Barcode: barcode,
                    StockCode: stockCode,
                    Quantity: quantity,
                    ListPrice: listPrice,
                    SalePrice: salePrice,
                    ProductContentId: contentId,
                    Approved: approved,
                    OnSale: true,
                    Attributes: new List<TrendyolRemoteAttribute>
                    {
                        new(47, "Renk", 686234, "Kırmızı", null),
                    }),
            });
    }
}
