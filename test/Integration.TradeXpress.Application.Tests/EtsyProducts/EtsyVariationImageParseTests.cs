using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy VARYASYON FOTOĞRAFI okuma yolunun birim testleri (ağ YOK — ham body → parser).
///
/// <para><b>Neden bu üç şey çivileniyor:</b> varyasyon fotoğrafı eşleştirmesinin tamamı KİMLİĞE dayanır
/// (<c>property_id</c>/<c>value_id</c>/<c>image_id</c>). Kimliklerden biri sessizce okunmazsa fotoğraf hiçbir
/// varyanta bağlanmaz ve bu HATASIZ, LOGSUZ bir kayıptır — kullanıcı yalnız "kırmızının fotoğrafı yok" diye
/// görür. Parser bu yüzden hem body şeklinde (iki olası kap adı) hem de kimlik taşımada pinlenir.</para>
/// </summary>
public class EtsyVariationImageParseTests
{
    // ── ① variation_images body'si ──────────────────────────────────────────────────────────────────

    /// <summary>Hakan'ın doğruladığı v3 spec'inin body'si (<c>variation_images[]</c>) birebir okunur.</summary>
    [Fact]
    public void Reads_the_variation_images_body()
    {
        const string payload = """
        {
          "variation_images": [
            { "property_id": 200, "value_id": 71, "image_id": 11 },
            { "property_id": 200, "value_id": 72, "image_id": 12 }
          ]
        }
        """;

        var images = EtsyProductClient.ParseVariationImages(payload);

        images.Count.ShouldBe(2);
        images[0].PropertyId.ShouldBe(200);
        images[0].ValueId.ShouldBe(71);
        images[0].ImageId.ShouldBe(11);
        images[1].ValueId.ShouldBe(72);
        images[1].ImageId.ShouldBe(12);
    }

    /// <summary>Etsy'nin genel liste sözleşmesi (<c>results[]</c>) da kabul edilir: hangi kabın geldiği canlı
    /// doğrulanmadı ve tek ada bağlanmak, yanlış tahminde varyasyon fotoğraflarını "hiç yok" göstermek olurdu.</summary>
    [Fact]
    public void Reads_the_generic_results_body_too()
    {
        const string payload = """
        { "count": 1, "results": [ { "property_id": 513, "value_id": 1213, "image_id": 55 } ] }
        """;

        var images = EtsyProductClient.ParseVariationImages(payload);

        var single = images.ShouldHaveSingleItem();
        single.PropertyId.ShouldBe(513);
        single.ValueId.ShouldBe(1213);
        single.ImageId.ShouldBe(55);
    }

    /// <summary>Kimliği eksik/sıfır olan bağ ELENİR — eksik kimlikle eşleştirme, fotoğrafı yanlış varyanta bağlama
    /// riskidir; bağlamamak geri alınabilirdir.</summary>
    [Fact]
    public void Drops_entries_with_missing_identifiers()
    {
        const string payload = """
        {
          "variation_images": [
            { "property_id": 200, "value_id": 71 },
            { "property_id": 200, "image_id": 11 },
            { "property_id": 0, "value_id": 71, "image_id": 11 },
            { "property_id": 200, "value_id": 71, "image_id": 11 }
          ]
        }
        """;

        EtsyProductClient.ParseVariationImages(payload).ShouldHaveSingleItem().ImageId.ShouldBe(11);
    }

    /// <summary>Bağ dizisi hiç yoksa BOŞ liste (uç 404/boş dönebilir — varyasyon fotoğrafı olmayan listeleme
    /// normaldir). Bozuk body ise sessizce boş dönmez: kardeş parser'larla aynı politika, dostane hata.</summary>
    [Fact]
    public void Empty_body_is_empty_and_broken_body_fails_fast()
    {
        EtsyProductClient.ParseVariationImages("{ \"count\": 0 }").ShouldBeEmpty();
        Should.Throw<BusinessException>(() => EtsyProductClient.ParseVariationImages("{ bozuk"));
    }

    // ── ② Görsel KİMLİĞİ + URL birlikte taşınır ─────────────────────────────────────────────────────

    /// <summary>Listeleme sayfası görselleri artık KİMLİKLİ okunur (<c>listing_image_id</c>) ve düz URL görünümü
    /// (<c>ImageUrls</c>) aynı setten TÜRETİLİR — ikisi ayrı alanlar olsaydı fallback yolunun kopyalaması
    /// (<c>with</c>) birini bayat bırakabilirdi. Offering property'leri de kimliklerini taşır.</summary>
    [Fact]
    public void Carries_image_identity_and_property_identity_together()
    {
        const string payload = """
        {
          "count": 1,
          "results": [
            {
              "listing_id": 9001,
              "title": "Deri Kilif",
              "images": [
                { "listing_image_id": 11, "url_fullxfull": "https://cdn.example.com/kirmizi.jpg" },
                { "listing_image_id": 12, "url_fullxfull": "https://cdn.example.com/mavi.jpg" }
              ],
              "inventory": {
                "products": [
                  {
                    "product_id": 4001,
                    "sku": "SKU-RED-S",
                    "property_values": [
                      { "property_id": 200, "property_name": "Renk", "values": ["Kirmizi"], "value_ids": [71] }
                    ],
                    "offerings": [ { "quantity": 3, "is_enabled": true } ]
                  }
                ]
              }
            }
          ]
        }
        """;

        var (items, count) = EtsyProductClient.ParseListingsPage(payload);

        count.ShouldBe(1);
        var listing = items.ShouldHaveSingleItem();
        listing.Images.Count.ShouldBe(2);
        listing.Images[0].ImageId.ShouldBe(11);
        listing.Images[0].Url.ShouldBe("https://cdn.example.com/kirmizi.jpg");

        // Düz URL görünümü kimlikli setle AYNI sırayı/ içeriği taşır (türetilmiş, ayrı alan değil).
        listing.ImageUrls.ShouldBe(listing.Images.Select(i => i.Url).ToList());

        var property = listing.Offerings.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem();
        property.Name.ShouldBe("Renk");
        property.Value.ShouldBe("Kirmizi");
        property.PropertyId.ShouldBe(200);
        property.ValueId.ShouldBe(71);
    }

    /// <summary>Kimliksiz görsel yine de GALERİYE iner (yalnız varyasyon eşleşmesine katılamaz): kimliği zorunlu
    /// tutmak, kimlik döndürmeyen bir yanıtta ürün galerisini tamamen boşaltırdı. Kimliği okunamayan property de
    /// null taşır — metin eşleşmesi bozulmaz.</summary>
    [Fact]
    public void Identity_is_optional_enrichment_not_a_gate()
    {
        const string payload = """
        {
          "count": 1,
          "results": [
            {
              "listing_id": 9002,
              "title": "Kimliksiz",
              "images": [ { "url_fullxfull": "https://cdn.example.com/kimliksiz.jpg" } ],
              "inventory": {
                "products": [
                  {
                    "product_id": 4002,
                    "property_values": [ { "property_name": "Renk", "values": ["Mavi"] } ],
                    "offerings": [ { "quantity": 1 } ]
                  }
                ]
              }
            }
          ]
        }
        """;

        var listing = EtsyProductClient.ParseListingsPage(payload).Items.ShouldHaveSingleItem();

        var image = listing.Images.ShouldHaveSingleItem();
        image.ImageId.ShouldBe(0);
        image.Url.ShouldBe("https://cdn.example.com/kimliksiz.jpg");
        listing.ImageUrls.ShouldHaveSingleItem().ShouldBe("https://cdn.example.com/kimliksiz.jpg");

        var property = listing.Offerings.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem();
        property.Value.ShouldBe("Mavi");
        property.PropertyId.ShouldBeNull();
        property.ValueId.ShouldBeNull();
    }
}
