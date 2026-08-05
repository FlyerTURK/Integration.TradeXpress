using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
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
    // Agnostik varyant tablosunda Product varyantları bu sahip-adıyla tutulur (production: ProductEntityName).
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly FakeTrendyolProductClient _fakeClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _headerRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrTrendyolProductImportTests()
    {
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _fakeClient = GetRequiredService<FakeTrendyolProductClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _variantDetailRepository = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItem, Guid>>();
        _currencyUnitRepository = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _entityMedia = GetRequiredService<IEntityMediaAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── Varyant satış-fiyatı artık EntityVariant'ta DEĞİL, Product uzantısı ProductVariantDetail'de (1:1,
    // EntityVariantId). Testler fiyatı buradan okur/yazar (production LoadVariantSalePricesAsync ile aynı yol). ──

    private async Task<ProductVariantDetail> GetVariantDetailAsync(Guid entityVariantId)
    {
        return await WithUnitOfWorkAsync(async () =>
            (await _variantDetailRepository.GetListAsync(d => d.EntityVariantId == entityVariantId)).Single());
    }

    private async Task SetVariantSalePriceAsync(Guid entityVariantId, decimal? price, Guid? currencyUnitId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var detail = (await _variantDetailRepository.GetListAsync(d => d.EntityVariantId == entityVariantId)).Single();
            detail.SetSalePrice(price, currencyUnitId);
            await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
            return true;
        });
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

            // Görseller DAM'a import edilir (legacy URL-kaynağı 2026-07-31'de emekli). Test ortamında uzak URL
            // erişilemez → indirme ATLANIR ama import KIRILMAZ (dayanıklılık kuralı) — ürün medyasız kalır.
            var mediaLinks = await _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id);
            mediaLinks.ShouldBeEmpty();

            // Varyantlar: kalem başına bir tane; barcode ticari kimliğe yazıldı; İLK kalem MAIN.
            var variants = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
            variants.Count.ShouldBe(2);
            var red = variants.Single(v => v.Barcode == "BR-RED-1");
            var blue = variants.Single(v => v.Barcode == "BR-BLUE-1");
            red.IsMain.ShouldBeTrue();
            blue.IsMain.ShouldBeFalse();
            red.Name.ShouldBe("iPhone 15 Deri Kılıf");   // varyant adında da TitleCase EZMEDİ (SetName normalizeTitle:false)
            (await GetVariantDetailAsync(red.Id)).SalePrice.ShouldBe(1299.90m);   // fiyat ProductVariantDetail'de
            red.StockQuantity.ShouldBe(7);
            (await GetVariantDetailAsync(blue.Id)).SalePrice.ShouldBe(1349.90m);
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

            // StockItem override: uzak fiyat kanal katmanına yazıldı (kullanıcı onaylı yön). STOK — K12 politikası:
            // varyant BU importta doğdu → çekirdek remote'la tohumlandı → fark yok → OverrideStock NULL kalır
            // (gürültü üretilmez; null = ERP StockQuantity devralınır) ve fark sayacı 0'dır.
            report.StockDifferenceCount.ShouldBe(0);
            var headers = await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id));
            headers.Count.ShouldBe(2);
            var redHeader = headers.Single(h => h.ProductVariantId == red.Id);
            redHeader.OverridePrice.ShouldBe(1299.90m);
            redHeader.OverrideStock.ShouldBeNull();
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

            // K12 politikası: çekirdek varyant ZATEN VARDI (update yolu) → remote stok (9) çekirdeği (5) EZMEZ,
            // fark kanal override'ına yazılır + fark sayacıyla görünür kılınır (sessiz geçilmez).
            second.StockDifferenceCount.ShouldBe(1);

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

            // Çekirdek stok İLK import tohumunda kaldı — sonraki import EZMEDİ (son-import-kazanır kapandı).
            var coreVariant = (await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.CompanyId == companyId))).ShouldHaveSingleItem();
            coreVariant.StockQuantity.ShouldBe(5);
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

                var variant = (await _variantRepository.GetListAsync(
                    v => v.EntityName == ProductEntityName && v.EntityId == product.Id)).Single();
                var detail = (await _variantDetailRepository.GetListAsync(d => d.EntityVariantId == variant.Id)).Single();
                detail.SetSalePrice(999m, null);   // fiyat artık ProductVariantDetail'de
                await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
                return true;
            });

            // Uzakta başlık/fiyat değişti — ikinci import şablonu/varyantı EZMEMELİ (yalnız kanal grafı).
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-3", barcode: "BR-Y-1", stockCode: "STK-Y-1", title: "Uzakta Değişen Başlık",
                quantity: 4, salePrice: 150m, listPrice: null, contentId: 5, approved: null));
            var second = await _appService.ImportFromMarketplaceAsync(channel.Id);
            second.StockDifferenceCount.ShouldBe(1);   // K12: çekirdek 2 vs remote 4 → fark sayaçta görünür

            var after = await WithUnitOfWorkAsync(async () => await _productRepository.GetAsync(product.Id));
            after.Name.ShouldBe("Kullanıcı Ürün Adı");   // şablon korunur

            var variantAfter = (await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id))).ShouldHaveSingleItem();
            (await GetVariantDetailAsync(variantAfter.Id)).SalePrice.ShouldBe(999m);   // ERP varyant fiyatı korunur

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
                var v1 = new EntityVariant(companyId, ProductEntityName, productA.Id, "VARA", "Varyant A");
                v1.SetBarcode("BR-UNIQUE-1");
                await _variantRepository.InsertAsync(v1, autoSave: true);
                return true;
            });

            Exception? caught = null;
            try
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var v2 = new EntityVariant(companyId, ProductEntityName, productB.Id, "VARB", "Varyant B");
                    v2.SetBarcode("BR-UNIQUE-1");
                    await _variantRepository.InsertAsync(v2, autoSave: true);
                    return true;
                });
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            caught.ShouldNotBeNull();   // filtered unique index (TenantId, CompanyId, Barcode) duplikeyi DB'de reddetti

            // Barcode'suz (NULL) satırlar filtreye takılmaz — ikinci null-barcode varyant serbest.
            await WithUnitOfWorkAsync(async () =>
            {
                var v3 = new EntityVariant(companyId, ProductEntityName, productB.Id, "VARC", "Varyant C");
                await _variantRepository.InsertAsync(v3, autoSave: true);
                var v4 = new EntityVariant(companyId, ProductEntityName, productB.Id, "VARD", "Varyant D");
                await _variantRepository.InsertAsync(v4, autoSave: true);
                return true;
            });
        }
    }

    /// <summary>
    /// SİLİNMİŞ varyantın barkodu SERBEST kalmalı — yeniden kullanılabilmeli.
    ///
    /// <para><b>Yakaladığı hata:</b> indeks filtresinde <c>IsDeleted = 0</c> yoktu, yani soft-delete edilmiş satır
    /// barkodu SÜRESİZ işgal ediyordu. İçe aktarımın barkod araması ise soft-delete filtresine tabi olduğundan o
    /// satırı GÖREMİYOR, barkodu boş sanıp INSERT deniyor ve ham unique ihlaliyle TÜM içe aktarımı düşürüyordu.
    /// İndeks ile arama kapsamının ayrışması tam olarak buydu.</para>
    ///
    /// <para>Pratik sonucu: ürünlerini silip Trendyol'dan yeniden çekmek imkânsızdı.</para>
    /// </summary>
    [Fact]
    public async Task Barcode_of_a_deleted_variant_becomes_reusable()
    {
        var companyId = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(Guid.NewGuid()))
        using (_currentCompany.Change(companyId))
        {
            var product = await WithUnitOfWorkAsync(async () =>
                await _productRepository.InsertAsync(new Product(companyId, "REUSE", "Urun"), autoSave: true));

            var variantId = await WithUnitOfWorkAsync(async () =>
            {
                var v = new EntityVariant(companyId, ProductEntityName, product.Id, "VAR1", "Varyant 1");
                v.SetBarcode("BR-REUSE-1");
                await _variantRepository.InsertAsync(v, autoSave: true);
                return v.Id;
            });

            // Soft-delete (uygulamanın silme yolu da bunu yapar).
            await WithUnitOfWorkAsync(async () =>
            {
                await _variantRepository.DeleteAsync(variantId, autoSave: true);
                return true;
            });

            // AYNI barkodla yeni varyant — indeks filtresi IsDeleted'ı dışladığı için ARTIK SERBEST.
            await WithUnitOfWorkAsync(async () =>
            {
                var v2 = new EntityVariant(companyId, ProductEntityName, product.Id, "VAR2", "Varyant 2");
                v2.SetBarcode("BR-REUSE-1");
                await _variantRepository.InsertAsync(v2, autoSave: true);
                return true;
            });

            var live = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.CompanyId == companyId && v.Barcode == "BR-REUSE-1"));
            live.ShouldHaveSingleItem().Code.ShouldBe("VAR2");
        }
    }

    // ── Tenant-içi çapraz-şirket AYNI BARKOD: artık ÇAKIŞMA DEĞİL, ikisi de içe aktarabilir ──────────

    /// <summary>
    /// Aynı tenant altındaki İKİ FARKLI ŞİRKET, her biri kendi Trendyol kanalıyla AYNI barkodlu malı içe
    /// aktarabilmelidir.
    ///
    /// <para><b>Bu test 2026-08-04'te TERS ÇEVRİLDİ.</b> Öncesinde adı
    /// <c>Barcode_owned_by_another_company_in_same_tenant_is_skipped_and_reported</c> idi ve kalemin ATLANDIĞINI
    /// doğruluyordu — çünkü unique index <c>(TenantId, Barcode)</c> ile tenant genelindeydi ve ikinci şirketin
    /// insert'i ham DB ihlaline yol açıyordu. Bu bir tasarım kararı değil, sahiplik modeliyle çelişen bir
    /// KISITTI: CLAUDE.md §6 emtia kataloglarını ve Product'ı per-company tanımlıyor.</para>
    ///
    /// <para><b>Gereksinim değişti (Hakan):</b> "Bir tenantta birden çok şirket kurup, o şirketler üzerinden aynı
    /// tenant içerisinde farklı Trendyol satış kanalı oluşturup aynı barkodlu ürünü satmayı planlıyorum."
    /// İndeks <c>(TenantId, CompanyId, Barcode)</c>'a daraltıldı; testin iddiası da yeni kuralı çiviliyor.
    /// Bu bir assertion GEVŞETMESİ değildir — eski test bir gereksinimi kodluyordu, gereksinim değişti.</para>
    /// </summary>
    [Fact]
    public async Task Same_barcode_in_two_companies_of_one_tenant_imports_for_both()
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
                    var variant = new EntityVariant(companyA, ProductEntityName, product.Id, "VARA", "Varyant A");
                    variant.SetBarcode("BR-SHARED-1");
                    await _variantRepository.InsertAsync(variant, autoSave: true);
                    return true;
                });
            }

            // Şirket B: aynı tenant'ta AYNI barkodu kendi Trendyol kanalından içe aktarır — ATLANMAMALI.
            using (_currentCompany.Change(companyB))
            {
                var channel = await SeedChannelAsync(companyB, "IMP6");
                _fakeClient.RemoteItems.Clear();
                _fakeClient.RemoteItems.Add(BuildRemoteItem(
                    mainId: "MAIN-6", barcode: "BR-SHARED-1", stockCode: "STK-S-1", title: "Paylasilan Kalem",
                    quantity: 1, salePrice: 10m, listPrice: null, contentId: 1, approved: null));
                _fakeClient.RemoteItems.Add(BuildRemoteItem(
                    mainId: "MAIN-7", barcode: "BR-FREE-1", stockCode: "STK-F-1", title: "Serbest Kalem",
                    quantity: 2, salePrice: 20m, listPrice: null, contentId: 2, approved: null));

                var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

                report.SkippedRows.ShouldBeEmpty();          // hiçbir kalem elenmedi
                report.CreatedProducts.ShouldBe(2);          // İKİ kalem de şablon üretti
                report.CreatedChannelProducts.ShouldBe(2);

                var products = await WithUnitOfWorkAsync(async () =>
                    await _productRepository.GetListAsync(p => p.CompanyId == companyB));
                products.Select(p => p.Code).OrderBy(c => c).ShouldBe(new[] { "STK-F-1", "STK-S-1" });
            }

            // Şirket A'nın kaydı DOKUNULMADAN duruyor — iki şirket aynı barkodu bağımsız taşıyor.
            var companyAVariants = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.CompanyId == companyA && v.Barcode == "BR-SHARED-1"));
            companyAVariants.ShouldHaveSingleItem();
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
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.CompanyId == companyId))).ShouldHaveSingleItem();
            (await GetVariantDetailAsync(variant.Id)).SalePrice.ShouldBeNull();   // negatif uzak fiyat upsert guard'ıyla AYNI şekilde süzüldü
            variant.StockQuantity.ShouldBe(0);

            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.CategoryId.ShouldBeNull();       // 32 sınırını aşan uzak kategori id → NULL (sentinel "0" kalktı; raporlu)
            record.Skus.ShouldHaveSingleItem().StockCode.Length.ShouldBe(100);   // taşan stockCode kırpıldı
            report.UnmatchedCategories.ShouldNotBeEmpty();
        }
    }

    // ── Kod-çakışan kardeş varyantlar: İLK kuruluş TÜM renkleri doğurur (canlı vaka "Velvet Ruj") ────

    [Fact]
    public async Task First_import_creates_all_suffixed_variants_for_stockcode_sharing_siblings()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP8");
            _fakeClient.RemoteItems.Clear();

            // Canlı vaka: renk kalemleri productMainId'SİZ + AYNI stockCode ile gelir. Eski davranış: her kalem
            // ayrı ürün sayılır, ilk kalem şablonu kurar, kalanlar stockCode fallback'iyle AYNI kanal kaydına düşüp
            // "şablonda varyant yok" diye atlanırdı (sessizce tek varyant). Yeni davranış: stockCode birleştirmesi
            // hepsini TEK şablonun kardeş varyantları yapar; kod çakışması son-ekle ("-2", "-3") ayrışır.
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: null, barcode: "BR-LIP-1", stockCode: "207040879", title: "Velvet Ruj",
                quantity: 5, salePrice: 199.90m, listPrice: null, contentId: 801, approved: true));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: null, barcode: "BR-LIP-2", stockCode: "207040879", title: "Velvet Ruj",
                quantity: 3, salePrice: 199.90m, listPrice: null, contentId: 802, approved: true));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: null, barcode: "BR-LIP-3", stockCode: "207040879", title: "Velvet Ruj",
                quantity: 8, salePrice: 209.90m, listPrice: null, contentId: 803, approved: true));

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.TotalFetchedItems.ShouldBe(3);
            report.TotalRemoteProducts.ShouldBe(1);   // stockCode birleştirmesi: 3 kalem = 1 ürün
            report.CreatedProducts.ShouldBe(1);
            report.CreatedChannelProducts.ShouldBe(1);
            report.SkippedRows.ShouldBeEmpty();       // "şablonda varyant yok" satırı YOK — kardeşler kuruldu

            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            product.Code.ShouldBe("207040879");

            var variants = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
            variants.Count.ShouldBe(3);
            variants.Select(v => v.Code).OrderBy(c => c)
                .ShouldBe(new[] { "207040879", "207040879-2", "207040879-3" });
            variants.Count(v => v.IsMain).ShouldBe(1);
            variants.Single(v => v.Barcode == "BR-LIP-1").IsMain.ShouldBeTrue();   // İLK kalem main

            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.Skus.Count.ShouldBe(3);
        }
    }

    // ── Eksik varyant tamamlama IMPORT'A GÖMÜLÜ (2026-07-11): import yalnız EKLER — mevcut şablon/varyant
    // ALANLARI GÜNCELLENMEZ, ana varyant değişmez; ikinci import 0 ekler (idempotent). ──────────────

    [Fact]
    public async Task Import_adds_missing_variants_to_existing_template_and_preserves_existing_fields()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP9");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-9", barcode: "BR-C-1", stockCode: "STK-C", title: "Velvet Ruj",
                quantity: 5, salePrice: 100m, listPrice: null, contentId: 901, approved: true));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();

            // Kullanıcı emeği: mevcut varyantın fiyatını düzenledi — sonraki import DOKUNMAMALI (ekleme-only).
            await WithUnitOfWorkAsync(async () =>
            {
                var variant = (await _variantRepository.GetListAsync(
                    v => v.EntityName == ProductEntityName && v.EntityId == product.Id)).Single();
                var detail = (await _variantDetailRepository.GetListAsync(d => d.EntityVariantId == variant.Id)).Single();
                detail.SetSalePrice(777m, null);   // fiyat ProductVariantDetail'de
                await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
                return true;
            });

            // Uzakta 2 YENİ renk kalemi belirdi (aynı grup, AYNI stockCode → kod çakışması son-ekle çözülmeli);
            // mevcut kalemin uzak fiyatı da değişti — mevcut ERP varyant ALANLARINA yansıtılmaz (yalnız kanal
            // override katmanı tazelenir; kullanıcı onaylı yön — Second_import kilidiyle aynı).
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-9", barcode: "BR-C-1", stockCode: "STK-C", title: "Velvet Ruj",
                quantity: 9, salePrice: 150m, listPrice: null, contentId: 901, approved: true));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-9", barcode: "BR-C-2", stockCode: "STK-C", title: "Velvet Ruj",
                quantity: 3, salePrice: 120m, listPrice: null, contentId: 902, approved: true));
            _fakeClient.RemoteItems.Add(BuildRemoteItem(
                mainId: "MAIN-9", barcode: "BR-C-3", stockCode: "STK-C", title: "Velvet Ruj",
                quantity: 4, salePrice: 130m, listPrice: null, contentId: 903, approved: true));

            var result = await _appService.ImportFromMarketplaceAsync(channel.Id);

            result.AddedVariants.ShouldBe(2);
            result.AddedBarcodes.OrderBy(b => b).ShouldBe(new[] { "BR-C-2", "BR-C-3" });
            result.SkippedRows.ShouldBeEmpty();          // 'VariantMissingOnTemplate' skip nedeni KALKTI
            result.CreatedProducts.ShouldBe(0);          // şablon yeniden üretilmedi
            result.UpdatedChannelProducts.ShouldBe(1);
            result.StockDifferenceCount.ShouldBe(1);     // yalnız MEVCUT varyantta fark (5→9); yeni eklenenler tohumlandı

            var variants = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
            variants.Count.ShouldBe(3);

            var original = variants.Single(v => v.Barcode == "BR-C-1");
            original.IsMain.ShouldBeTrue();        // ANA VARYANT DEĞİŞMEDİ
            (await GetVariantDetailAsync(original.Id)).SalePrice.ShouldBe(777m);   // kullanıcı-düzenlenmiş alan AYNEN korunur
            original.StockQuantity.ShouldBe(5);    // uzak stok değişimi mevcut ERP varyantına YANSITILMAZ

            var added = variants.Single(v => v.Barcode == "BR-C-2");
            added.IsMain.ShouldBeFalse();          // yeni eklenen main OLMAZ
            (await GetVariantDetailAsync(added.Id)).SalePrice.ShouldBe(120m);
            added.StockQuantity.ShouldBe(3);
            added.Code.ShouldStartWith("STK-C-");  // kod çakışması son-ekle ("-2"/"-3") çözüldü

            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.Skus.Count.ShouldBe(3);

            // StockItem zinciri: eklenenlerin başlığı kuruldu; mevcut başlığın override'ı IMPORT semantiğiyle
            // TAZELENDİ (uzak fiyat kanal katmanına yazılır — ERP varyantı değil; Second_import kilidiyle tutarlı).
            // STOK — K12: mevcut varyantta fark (5→9) → OverrideStock=9; BU importta doğan varyantta fark yok → NULL.
            var headers = await WithUnitOfWorkAsync(async () =>
                await _headerRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id));
            headers.Count.ShouldBe(3);
            headers.Single(h => h.ProductVariantId == original.Id).OverridePrice.ShouldBe(150m);   // kanal katmanı tazelendi
            headers.Single(h => h.ProductVariantId == original.Id).OverrideStock.ShouldBe(9);
            headers.Single(h => h.ProductVariantId == added.Id).OverridePrice.ShouldBe(120m);
            headers.Single(h => h.ProductVariantId == added.Id).OverrideStock.ShouldBeNull();

            // Üçüncü geçiş İDEMPOTENT: 0 ekleme, varyant sayısı sabit, ana varyant aynı. Stok farkı (5 vs 9)
            // SÜRDÜKÇE her import'ta yeniden raporlanır (fark görünür kalır — sessiz geçilmez).
            var third = await _appService.ImportFromMarketplaceAsync(channel.Id);
            third.AddedVariants.ShouldBe(0);
            third.AddedBarcodes.ShouldBeEmpty();
            third.StockDifferenceCount.ShouldBe(1);
            var variantsAfter = await WithUnitOfWorkAsync(async () =>
                await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
            variantsAfter.Count.ShouldBe(3);
            variantsAfter.Single(v => v.IsMain).Barcode.ShouldBe("BR-C-1");
        }
    }

    // ── Gevşek kategori (Trendyol_CategoryOptional): kategorisiz uzak kayıt NULL kategoriyle yazılır + raporlanır ──

    [Fact]
    public async Task Import_without_category_writes_null_and_reports()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP11");
            _fakeClient.RemoteItems.Clear();
            var item = BuildRemoteItem(
                mainId: "MAIN-11", barcode: "BR-NC-1", stockCode: "STK-NC-1", title: "Kategorisiz Kalem",
                quantity: 2, salePrice: 50m, listPrice: null, contentId: 1101, approved: null);
            _fakeClient.RemoteItems.Add(item with { CategoryId = null, CategoryName = null });

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            // Ürün ATLANMAZ: kanal kaydı kategorisiz (NULL) yazılır; eksik kategori raporda görünür (sessiz geçilmez).
            report.CreatedChannelProducts.ShouldBe(1);
            report.UnmatchedCategories.ShouldNotBeEmpty();

            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            record.CategoryId.ShouldBeNull();      // sentinel "0" YOK — kategori boş kalır (kullanıcı sonradan seçer)
            record.CategoryName.ShouldBeNull();
            record.BrandId.ShouldBe("82");         // marka sentineli/akışı DEĞİŞMEDİ
        }
    }

    // ── Gevşek kategori: kategorisiz kanal ürünü PUSH'ta dostane fail-fast (Trendyol şemasında kategori zorunlu) ──

    [Fact]
    public async Task Push_without_category_fails_fast_with_friendly_error()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IMP12");
            var product = await WithUnitOfWorkAsync(async () =>
                await _productRepository.InsertAsync(new Product(companyId, "PUSHCAT", "Kategorisiz Push Urunu"), autoSave: true));

            var dto = await _appService.CreateAsync(new SalesChannelTrTrendyolProductCreateDto
            {
                ProductId = product.Id,
                SalesChannelId = channel.Id,
                CategoryId = null,                 // kategori OPSİYONEL — kayıt açılabilir
                BrandId = "82",
            });

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToTrendyolAsync(dto.Id));
            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:CategoryRequired");
        }
    }

    // ── TRY para birimi HOST kaydından, TENANT bağlamında çözülür (filtre-kapalı okuma — regresyon kilidi) ──

    [Fact]
    public async Task Import_resolves_try_currency_from_host_record_in_tenant_context()
    {
        var companyId = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(Guid.NewGuid()))
        {
            // TRY tipik kurulumda HOST kaydıdır (CurrencyUnit host‖tenant çapraz katalog) — tenant data-filter'ı
            // host satırını gizleyince fiyatlar para-birimsiz düşüyordu (canlıda yaşandı, 2026-07-11). Kilit:
            // tenant bağlamındaki import host TRY'sini bulmalı, uyarı üretmemeli.
            Guid hostTryId;
            using (currentTenant.Change(null))
            {
                // Bul-ya-da-oluştur: paylaşılan test DB'sinde başka bir test host TRY'yi kurmuş olabilir —
                // unique index (TenantId, Code) + deterministik kimlik doğrulaması için tek satır garanti edilir.
                hostTryId = (await WithUnitOfWorkAsync(async () =>
                {
                    var existing = (await _currencyUnitRepository.GetListAsync(c => c.Code == "TRY")).FirstOrDefault();
                    return existing
                        ?? await _currencyUnitRepository.InsertAsync(new CurrencyUnit("TRY", "Türk Lirası"), autoSave: true);
                })).Id;
            }

            using (_currentCompany.Change(companyId))
            {
                var channel = await SeedChannelAsync(companyId, "IMP10");
                _fakeClient.RemoteItems.Clear();
                _fakeClient.RemoteItems.Add(BuildRemoteItem(
                    mainId: "MAIN-10", barcode: "BR-TRY-1", stockCode: "STK-TRY-1", title: "Fiyatlı Kalem",
                    quantity: 2, salePrice: 250m, listPrice: null, contentId: 1001, approved: true));

                var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

                report.Warnings.ShouldBeEmpty();   // TryCurrencyMissing uyarısı YOK — host kaydı çözüldü

                var variant = (await WithUnitOfWorkAsync(async () =>
                    await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.CompanyId == companyId))).ShouldHaveSingleItem();
                var detail = await GetVariantDetailAsync(variant.Id);
                detail.SalePrice.ShouldBe(250m);
                detail.SalePriceCurrencyUnitId.ShouldBe(hostTryId);
            }
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
