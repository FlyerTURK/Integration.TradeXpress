using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="TrendyolProductClient.ParseSellerProductsPage"/> + <see cref="TrendyolProductClient.GroupByProductMainId"/>
/// birim testleri — örnek listeleme JSON'u → record'lar (saf parse; ağ/DI yok). Alan adları Trendyol V2 listeleme
/// yanıtına göre (pimCategoryId/brand/productContentId defansif okunur).
/// </summary>
public class TrendyolProductClientParseTests
{
    private const string SamplePayload = """
    {
      "totalElements": 3,
      "totalPages": 1,
      "page": 0,
      "size": 200,
      "content": [
        {
          "id": 111,
          "approved": true,
          "onSale": true,
          "barcode": "BR-RED-1",
          "title": "iPhone 15 Kılıf Deri",
          "description": "Gerçek deri kılıf — el yapımı.",
          "productMainId": "MAIN-1",
          "pimCategoryId": 411,
          "categoryName": "Telefon Kılıfı",
          "brandId": 82,
          "brand": "MarkaX",
          "quantity": 7,
          "listPrice": 1500.50,
          "salePrice": 1299.90,
          "vatRate": 20,
          "dimensionalWeight": 0.5,
          "deliveryDuration": 2,
          "stockCode": "STK-RED-1",
          "productContentId": 987654,
          "images": [ { "url": "https://cdn.example.com/1.jpg" }, { "url": "https://cdn.example.com/2.jpg" } ],
          "attributes": [
            { "attributeId": 47, "attributeName": "Renk", "attributeValueId": 686234, "attributeValue": "Kırmızı" },
            { "attributeId": 338, "attributeName": "Materyal", "customAttributeValue": "Deri" }
          ]
        },
        {
          "barcode": "BR-BLUE-1",
          "title": "iPhone 15 Kılıf Deri",
          "productMainId": "MAIN-1",
          "quantity": 3,
          "salePrice": 1349.90,
          "stockCode": "STK-BLUE-1",
          "approved": false
        },
        {
          "title": "Barkodsuz kalem — BOŞ barcode ile taşınır (import atla+raporla yapar)",
          "quantity": 1
        }
      ]
    }
    """;

    [Fact]
    public void Parse_reads_page_envelope_and_items_and_keeps_barcodeless_rows_with_empty_barcode()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, SamplePayload);

        page.TotalPages.ShouldBe(1);
        page.TotalElements.ShouldBe(3);
        page.Items.Count.ShouldBe(3);   // barkodsuz üçüncü kalem BOŞ barcode ile taşınır — import raporlar (sessiz kayıp yok)
        page.Items[2].Variants.Single().Barcode.ShouldBe(string.Empty);

        var first = page.Items[0];
        first.Title.ShouldBe("iPhone 15 Kılıf Deri");
        first.Description.ShouldBe("Gerçek deri kılıf — el yapımı.");
        first.ProductMainId.ShouldBe("MAIN-1");
        first.CategoryId.ShouldBe("411");           // pimCategoryId sayısal → string'e indirgenir
        first.CategoryName.ShouldBe("Telefon Kılıfı");
        first.BrandId.ShouldBe("82");
        first.BrandName.ShouldBe("MarkaX");
        first.VatRate.ShouldBe(20);
        first.DimensionalWeight.ShouldBe(0.5m);
        first.DeliveryDuration.ShouldBe(2);
        first.ImageUrls.ShouldBe(new[] { "https://cdn.example.com/1.jpg", "https://cdn.example.com/2.jpg" });

        var variant = first.Variants.ShouldHaveSingleItem();
        variant.Barcode.ShouldBe("BR-RED-1");
        variant.StockCode.ShouldBe("STK-RED-1");
        variant.Quantity.ShouldBe(7);
        variant.ListPrice.ShouldBe(1500.50m);
        variant.SalePrice.ShouldBe(1299.90m);
        variant.ProductContentId.ShouldBe(987654);
        variant.Approved.ShouldBe(true);
        variant.OnSale.ShouldBe(true);

        variant.Attributes.Count.ShouldBe(2);
        variant.Attributes[0].AttributeId.ShouldBe(47);
        variant.Attributes[0].AttributeValueId.ShouldBe(686234);
        variant.Attributes[0].AttributeValue.ShouldBe("Kırmızı");
        variant.Attributes[1].AttributeId.ShouldBe(338);
        variant.Attributes[1].AttributeValueId.ShouldBeNull();
        variant.Attributes[1].CustomValue.ShouldBe("Deri");

        // Eksik alanlar null'a düşer (fail değil).
        var second = page.Items[1];
        second.Variants.Single().ListPrice.ShouldBeNull();
        second.Variants.Single().OnSale.ShouldBeNull();
        second.Variants.Single().Approved.ShouldBe(false);
        second.CategoryId.ShouldBeNull();
    }

    [Fact]
    public void GroupByProductMainId_merges_items_of_same_main_id_into_one_product()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, SamplePayload);

        var grouped = TrendyolProductClient.GroupByProductMainId(page.Items);

        grouped.Count.ShouldBe(2);                      // MAIN-1 grubu + barkodsuz (mainId'siz) kendi başına kalem
        var product = grouped[0];                       // iki kalem aynı MAIN-1 grubunda
        product.ProductMainId.ShouldBe("MAIN-1");
        product.Variants.Select(v => v.Barcode).ShouldBe(new[] { "BR-RED-1", "BR-BLUE-1" });
        product.ImageUrls.Count.ShouldBe(2);            // havuz = ortak alanlar gibi İLK kalemden
    }

    // ── KALEM BAŞINA GÖRSEL ─────────────────────────────────────────────────────────────────────────
    // Trendyol "images" dizisini content[] öğesi (barkod = varyant) başına döndürüyor. Görseller yalnız ürün
    // havuzuna yazıldığı sürece hangi fotoğrafın hangi varyanta ait olduğu kayboluyordu ve gruplamada
    // 2..N'inci kalemin fotoğrafları hiçbir yere ulaşmıyordu (havuz "ilk kalemden" alınır).
    // Çözüm kalemi varyantına bağlamak; havuzu birleşime çevirmek DEĞİL — varyant-farkı fotoğrafı kayıt
    // geneli medya bağlamına ait değildir (CLAUDE.md §6). Birleşime ihtiyaç duyan tek yer kanal CDN adres
    // damgasıdır (RemoteImageMediaIds) ve o birleşimi CollectAllImageUrls verir (aşağıdaki son test).

    private const string VariantImagePayload = """
    {
      "totalElements": 3, "totalPages": 1, "page": 0, "size": 200,
      "content": [
        {
          "barcode": "BR-RED", "title": "Bileklik", "productMainId": "MAIN-V", "quantity": 2,
          "images": [ { "url": "https://cdn.example.com/red.jpg" }, { "url": "https://cdn.example.com/ortak.jpg" } ]
        },
        {
          "barcode": "BR-BLUE", "title": "Bileklik", "productMainId": "MAIN-V", "quantity": 4,
          "images": [ { "url": "https://cdn.example.com/blue.jpg" }, { "url": "https://cdn.example.com/ORTAK.jpg" } ]
        },
        {
          "barcode": "BR-NOIMG", "title": "Görselsiz", "productMainId": "MAIN-N", "quantity": 1
        }
      ]
    }
    """;

    [Fact]
    public void Each_item_carries_its_own_images_down_to_its_variant()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, VariantImagePayload);

        // Aynı liste iki yere birden yazılır: kalemin varyantı (varyant bağlamının kaynağı) + ürün havuzu.
        var red = page.Items[0];
        red.Variants.Single().ImageUrls.ShouldBe(new[] { "https://cdn.example.com/red.jpg", "https://cdn.example.com/ortak.jpg" });
        red.ImageUrls.ShouldBe(red.Variants.Single().ImageUrls);

        page.Items[1].Variants.Single().ImageUrls.ShouldBe(new[] { "https://cdn.example.com/blue.jpg", "https://cdn.example.com/ORTAK.jpg" });
    }

    [Fact]
    public void Grouping_keeps_every_variants_own_images_while_the_pool_stays_first_item()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, VariantImagePayload);

        var grouped = TrendyolProductClient.GroupByProductMainId(page.Items);

        var product = grouped[0];
        product.ProductMainId.ShouldBe("MAIN-V");

        // (1) Kalem↔görsel eşleşmesi gruplamayı SAĞ GEÇER — varyant bağlamına inecek veri budur.
        product.Variants[0].ImageUrls.ShouldBe(new[] { "https://cdn.example.com/red.jpg", "https://cdn.example.com/ortak.jpg" });
        product.Variants[1].ImageUrls.ShouldBe(new[] { "https://cdn.example.com/blue.jpg", "https://cdn.example.com/ORTAK.jpg" });

        // (2) Havuz İLK kalemde kalır: kayıt geneli medya bağlamına inen küme budur ve mavi varyantın
        // fotoğrafı oraya AİT DEĞİLDİR (kendi bağlamında duruyor — bir üstteki assert).
        product.ImageUrls.ShouldBe(new[] { "https://cdn.example.com/red.jpg", "https://cdn.example.com/ortak.jpg" });
    }

    [Fact]
    public void CollectAllImageUrls_unions_the_pool_with_every_variant_set()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, VariantImagePayload);
        var product = TrendyolProductClient.GroupByProductMainId(page.Items)[0];

        // Kanal CDN adres damgası (RemoteImageMediaIds) bu kümeyi yazar: aday medya listesi (ürün + TÜM varyant
        // medyası) ile aynı kapsamda olmalı, yoksa push yeniden-kullanım dalı eksik adres gönderip PushHistory'yi yanlış yazar.
        // Aynı adres iki kalemde de geçtiği için tekilleşir (büyük/küçük harf farkı aynı adrestir) ve sıra
        // korunur — ilk görsel vitrindir.
        TrendyolProductClient.CollectAllImageUrls(product).ShouldBe(new[]
        {
            "https://cdn.example.com/red.jpg",
            "https://cdn.example.com/ortak.jpg",
            "https://cdn.example.com/blue.jpg"
        });
    }

    [Fact]
    public void An_item_without_images_yields_an_empty_list_rather_than_null()
    {
        // "Görsel yok" hâli BOŞ LİSTE ile temsil edilir: null olsaydı her tüketici null kontrolü yapmak zorunda
        // kalırdı ve o kontrol bir gün unutulurdu (import görsel yazma yolunda NRE).
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, VariantImagePayload);

        var variant = page.Items[2].Variants.Single();
        variant.ImageUrls.ShouldNotBeNull();
        variant.ImageUrls.ShouldBeEmpty();
        page.Items[2].ImageUrls.ShouldBeEmpty();

        // Parametre hiç verilmeden kurulan kalem de aynı sözleşmeyi taşır (mevcut çağrı yerleri değişmedi).
        new TrendyolRemoteVariant(
            Barcode: "BR-X",
            StockCode: null,
            Quantity: 0,
            ListPrice: null,
            SalePrice: null,
            ProductContentId: null,
            Approved: null,
            OnSale: null,
            Attributes: Array.Empty<TrendyolRemoteAttribute>())
            .ImageUrls.ShouldBeEmpty();
    }

    // ── PAZARYERİ ENGEL BEYANI ──────────────────────────────────────────────────────────────────────
    // Bu bayraklar yanıtta HEP vardı ve hiç okunmuyordu. Bedeli sessizdi: karalisteye alınmış bir kalem
    // bizde "onaylı + satışta" görünüyor, gönderim karşı tarafta reddediliyor ve sebebi hiçbir ekranda
    // yer almıyordu. Canlı ölçüm teorik olmadığını gösterdi — bir grubun 19 kaleminin TAMAMI karalistedeydi.

    private const string BlockedPayload = """
    {
      "totalElements": 2, "totalPages": 1, "page": 0, "size": 200,
      "content": [
        {
          "barcode": "BR-BLOCKED", "title": "Karalistelik", "productMainId": "MAIN-B", "quantity": 0,
          "approved": true, "onSale": true,
          "archived": false,
          "locked": true, "lockReason": "UNSUPPLIED_PRODUCT",
          "blacklisted": true, "blacklistReason": "Orijinallik Şüphesine İlişkin Belgeleri Yüklememe",
          "rejected": true, "rejectReasonDetails": [ { "reason": "Görsel yetersiz" }, "Marka eşleşmiyor" ],
          "hasActiveCampaign": true,
          "productUrl": "https://www.trendyol.com/x-p-742004605?merchantId=312014",
          "createDateTime": 1690904714000,
          "lastUpdateDate": 1770636349000
        },
        {
          "barcode": "BR-CLEAN", "title": "Temiz kalem", "productMainId": "MAIN-C", "quantity": 5,
          "approved": true, "onSale": true
        }
      ]
    }
    """;

    [Fact]
    public void Parse_reads_the_marketplace_obstacle_flags_and_their_reasons()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, BlockedPayload);

        var flags = page.Items[0].Variants.Single().Flags.ShouldNotBeNull();
        flags.Blacklisted.ShouldBe(true);
        flags.BlacklistReason.ShouldBe("Orijinallik Şüphesine İlişkin Belgeleri Yüklememe");
        flags.Locked.ShouldBe(true);
        flags.LockReason.ShouldBe("UNSUPPLIED_PRODUCT");
        flags.HasActiveCampaign.ShouldBe(true);
        flags.ProductUrl.ShouldBe("https://www.trendyol.com/x-p-742004605?merchantId=312014");
    }

    [Fact]
    public void Reject_reasons_are_joined_rather_than_truncated_to_the_first_one()
    {
        // Trendyol kimi kayıtta {reason:...} nesnesi, kimi kayıtta düz metin döndürüyor — ikisi de kabul
        // edilir. İlk gerekçeyi alıp kalanını atmak, "neden reddedildi" sorusuna EKSİK cevap olurdu.
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, BlockedPayload);

        page.Items[0].Variants.Single().Flags!.RejectReason.ShouldBe("Görsel yetersiz · Marka eşleşmiyor");
    }

    [Fact]
    public void An_unreported_flag_stays_null_rather_than_becoming_a_no_obstacle_claim()
    {
        // ÜÇ DURUMLU: null = "pazaryeri bildirmedi", false = "engel yok" BEYANI. İkisini birleştirmek,
        // bildirilmemiş bir engeli "engel yok" diye kaydetmek olurdu.
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, BlockedPayload);

        var clean = page.Items[1].Variants.Single().Flags.ShouldNotBeNull();
        clean.Blacklisted.ShouldBeNull();
        clean.Locked.ShouldBeNull();
        clean.HasActiveCampaign.ShouldBeNull();

        // Aynı yanıtta AÇIKÇA false bildirilen alan false kalır — null'a düşmez.
        page.Items[0].Variants.Single().Flags!.Archived.ShouldBe(false);
    }

    [Fact]
    public void Epoch_millisecond_timestamps_are_read_as_utc()
    {
        // Trendyol epoch MİLİSANİYE gönderiyor; kayıt UTC'dir (CLAUDE.md §6: kayıt=UTC, görüntü=yerel).
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, BlockedPayload);

        var flags = page.Items[0].Variants.Single().Flags!;
        flags.CreatedAtUtc.ShouldBe(new DateTime(2023, 8, 1, 15, 45, 14, DateTimeKind.Utc));
        flags.UpdatedAtUtc!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        flags.UpdatedAtUtc.Value.ShouldBeGreaterThan(flags.CreatedAtUtc!.Value);
    }

    // Bozuk body SESSİZCE boş sayfa dönmez (o ve sonraki sayfaların kalemleri raporsuz kaybolurdu) —
    // dostane hatayla import durur; upsert-only olduğundan yeniden deneme güvenlidir.
    [Fact]
    public void Parse_of_malformed_payload_throws_friendly_error_instead_of_silent_partial_result()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.ParseSellerProductsPage(3, 50, "{ bozuk json"));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:ListParseFailed");
        ex.Data["page"].ShouldBe(3);
    }
}
