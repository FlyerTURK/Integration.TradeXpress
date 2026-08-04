using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.N11Products.Rest;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// SOAP push verisi → REST <c>product-create</c> satırları çevirisi.
///
/// <para><b>Neden bu testler kritik:</b> iki uç aynı ürünü FARKLI modelliyor — SOAP'ta tek ürün + içinde
/// varyantlar, REST'te her SKU bağımsız satır. Çeviri yanlışsa N11'e giden gövde sessizce bozulur:
/// varyantlar tek satıra çöker, fiyat yanlış satıra yazılır ya da nitelik kimliği uydurulur. Hiçbiri
/// derleme hatası vermez.</para>
/// </summary>
public class N11RestPushMapperTests
{
    // ── Sabit test verisi ────────────────────────────────────────────────────────────────────────

    private static N11LeafAttributes Leaf()
    {
        return new N11LeafAttributes(
            ExternalId: "1219203",
            Name: "Altın Bilezik",
            Attributes: new List<N11AttributeDef>
            {
                // Değer listeli (valueId ZORUNLU)
                new("101", "Ayar", IsMandatory: true, IsVariant: false, IsCustomValue: false, Priority: 1,
                    Values: new List<N11AttributeValue> { new("9011", "14 Ayar"), new("9022", "22 Ayar") }),
                // Varyant ekseni, değer listeli
                new("202", "Renk", IsMandatory: true, IsVariant: true, IsCustomValue: false, Priority: 2,
                    Values: new List<N11AttributeValue> { new("7001", "Sarı"), new("7002", "Beyaz") }),
                // Serbest metin (customValue)
                new("303", "Marka", IsMandatory: false, IsVariant: false, IsCustomValue: true, Priority: 3,
                    Values: new List<N11AttributeValue>()),
            });
    }

    private static N11ProductData Data(
        IReadOnlyList<N11ProductStockItem>? stockItems = null,
        int? vatRate = 20,
        int currencyType = 1,
        string categoryId = "1219203")
    {
        return new N11ProductData(
            ProductSellerCode: "BLZ-14-1",
            Title: "14 Ayar Altın Bilezik",
            Description: "Açıklama",
            Domestic: true,
            CategoryId: categoryId,
            Price: 25000m,
            CurrencyType: currencyType,
            ProductCondition: 1,
            PreparingDay: 3,
            ShipmentTemplate: "STANDART",
            MaxPurchaseQuantity: 2,
            VatRate: vatRate,
            Images: new List<N11ProductImage> { new("https://cdn/1.jpg", 1), new("https://cdn/2.jpg", 2) },
            Attributes: new List<N11ProductAttributePair>
            {
                new("Ayar", "14 Ayar"),
                new("Marka", "Kendi Markam"),
            },
            StockItems: stockItems ?? new List<N11ProductStockItem>
            {
                new("BLZ-14-SARI-1", 5, 25000m, new List<N11ProductAttributePair> { new("Renk", "Sarı") }, null, null, null),
                new("BLZ-14-BEYAZ-1", 3, 27000m, new List<N11ProductAttributePair> { new("Renk", "Beyaz") }, null, null, null),
            },
            SpecialInfo: new List<N11ProductSpecialInfo>(),
            Discount: null,
            SellerNote: null,
            ProductionDate: null,
            ExpirationDate: null,
            GroupItemCode: null,
            GroupAttribute: null,
            ItemName: null);
    }

    // ── Düzleştirme ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Each_stock_item_becomes_its_own_rest_row()
    {
        // REST'in TEMEL farkı: hiyerarşi düzleşir. 2 varyant → 2 bağımsız satır.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows.Count.ShouldBe(2);
        rows.Select(r => r.StockCode).ShouldBe(new[] { "BLZ-14-SARI-1", "BLZ-14-BEYAZ-1" });
    }

    [Fact]
    public void All_rows_share_the_same_product_main_id_so_n11_groups_them()
    {
        // productMainId REST'teki TEK varyant mekanizması — ayrışırsa N11 bunları ayrı ürünler sanır.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows.Select(r => r.ProductMainId).Distinct().ShouldHaveSingleItem().ShouldBe("BLZ-14-1");
    }

    [Fact]
    public void Row_price_prefers_the_variant_option_price()
    {
        // SOAP'ta optionPrice varyantın fiyatıydı; REST'te satırın KENDİ salePrice'ı olur.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows[0].SalePrice.ShouldBe(25000m);
        rows[1].SalePrice.ShouldBe(27000m);
    }

    [Fact]
    public void Row_falls_back_to_product_price_when_variant_has_none()
    {
        var rows = N11RestPushMapper.ToCreateRows(
            Data(new List<N11ProductStockItem>
            {
                new("BLZ-TEK-1", 1, null, new List<N11ProductAttributePair> { new("Renk", "Sarı") }, null, null, null),
            }),
            Leaf());

        rows.ShouldHaveSingleItem().SalePrice.ShouldBe(25000m);
    }

    [Fact]
    public void List_price_is_never_below_sale_price()
    {
        // N11: "listPrice, salePrice'dan yüksek olmalıdır. Aksi takdirde isteğiniz REJECT alacaktır."
        // Ayrı bir liste fiyatı kavramımız yok → eşit gönderilir (doküman buna açıkça izin veriyor).
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows.ShouldAllBe(r => r.ListPrice >= r.SalePrice);
    }

    // ── Nitelik kimliği çözümü ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Listed_attribute_values_are_sent_as_value_ids_not_free_text()
    {
        // REST'in SOAP'tan en sert farkı: serbest metin kabul edilmez, kategori kimliği istenir.
        var row = N11RestPushMapper.ToCreateRows(Data(), Leaf())[0];

        var ayar = row.Attributes.Single(a => a.Id == 101);
        ayar.ValueId.ShouldBe(9011);
        ayar.CustomValue.ShouldBeNull();
    }

    [Fact]
    public void Custom_value_attributes_are_sent_as_free_text()
    {
        var row = N11RestPushMapper.ToCreateRows(Data(), Leaf())[0];

        var marka = row.Attributes.Single(a => a.Id == 303);
        marka.CustomValue.ShouldBe("Kendi Markam");
        marka.ValueId.ShouldBeNull();
    }

    [Fact]
    public void Variant_axis_is_written_per_row_so_n11_can_tell_them_apart()
    {
        // Her satır KENDİ renk değerini taşımalı; aynı değer iki satıra yazılırsa varyantlar ayırt edilemez.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows[0].Attributes.Single(a => a.Id == 202).ValueId.ShouldBe(7001);   // Sarı
        rows[1].Attributes.Single(a => a.Id == 202).ValueId.ShouldBe(7002);   // Beyaz
    }

    [Fact]
    public void Product_level_attributes_are_repeated_on_every_row()
    {
        // REST'te satırlar bağımsız ürünlerdir — ortak bir "ürün başlığı" bloğu yok, her satır kendi setini taşır.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows.ShouldAllBe(r => r.Attributes.Any(a => a.Id == 101));   // Ayar
        rows.ShouldAllBe(r => r.Attributes.Any(a => a.Id == 303));   // Marka
    }

    [Fact]
    public void Same_attribute_is_never_sent_twice_and_variant_wins()
    {
        // Ürün seviyesinde de "Renk" verilmişse varyantın değeri EZMELİ; iki kez göndermek N11'de tanımsız.
        var data = Data() with
        {
            Attributes = new List<N11ProductAttributePair>
            {
                new("Ayar", "14 Ayar"),
                new("Renk", "Beyaz"),   // ürün seviyesinde YANLIŞ değer — varyant ezmeli
            },
        };

        var row = N11RestPushMapper.ToCreateRows(data, Leaf())[0];

        row.Attributes.Count(a => a.Id == 202).ShouldBe(1);
        row.Attributes.Single(a => a.Id == 202).ValueId.ShouldBe(7001);   // varyantın Sarı'sı kazandı
    }

    [Fact]
    public void Attribute_matching_is_turkish_case_insensitive_like_the_validator()
    {
        // Doğrulayıcı "beden"="Beden" sayıyor; çevirici ayrışırsa doğrulamadan GEÇEN değer burada patlardı.
        var data = Data() with
        {
            Attributes = new List<N11ProductAttributePair> { new("AYAR", "14 ayar") },
        };

        var row = N11RestPushMapper.ToCreateRows(data, Leaf())[0];

        row.Attributes.Single(a => a.Id == 101).ValueId.ShouldBe(9011);
    }

    [Fact]
    public void Missing_value_id_fails_fast_instead_of_sending_free_text()
    {
        // SOAP fallback'inden gelen tanımda ValueId null olur. Serbest metne düşmek N11'de sessiz redde ya da
        // ürünün filtrelerde görünmemesine yol açardı — göndermek yerine patlıyoruz.
        var leaf = new N11LeafAttributes("1219203", "Altın Bilezik", new List<N11AttributeDef>
        {
            new("101", "Ayar", true, false, false, 1, new List<N11AttributeValue> { new(null, "14 Ayar") }),
        });
        var data = Data() with
        {
            Attributes = new List<N11ProductAttributePair> { new("Ayar", "14 Ayar") },
            StockItems = new List<N11ProductStockItem>
            {
                new("BLZ-1", 1, null, new List<N11ProductAttributePair>(), null, null, null),
            },
        };

        var ex = Should.Throw<BusinessException>(() => N11RestPushMapper.ToCreateRows(data, leaf));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:AttributeValueIdMissing");
    }

    // ── Zorunlu alan guard'ları ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Missing_vat_rate_blocks_the_push_entirely()
    {
        // Kıymetli madende %0, işçilikte %20 — oran tahmin EDİLMEZ (2026-08-04 Hakan kuralı).
        var ex = Should.Throw<BusinessException>(() => N11RestPushMapper.ToCreateRows(Data(vatRate: null), Leaf()));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:VatRateRequired");
    }

    [Fact]
    public void Vat_rate_outside_the_closed_set_is_rejected()
    {
        var ex = Should.Throw<BusinessException>(() => N11RestPushMapper.ToCreateRows(Data(vatRate: 18), Leaf()));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:VatRateInvalid");
    }

    [Theory]
    [InlineData(1, "TL")]
    [InlineData(2, "USD")]
    [InlineData(3, "EUR")]
    public void Soap_numeric_currency_becomes_rest_text(int soapCode, string expected)
    {
        // SOAP currencyType SAYI (1=TL), REST METİN (TL/USD/EUR) — çevrilmezse istek reddedilir.
        var rows = N11RestPushMapper.ToCreateRows(Data(currencyType: soapCode), Leaf());

        rows.ShouldAllBe(r => r.CurrencyType == expected);
    }

    [Fact]
    public void Unknown_currency_code_fails_fast()
    {
        var ex = Should.Throw<BusinessException>(() => N11RestPushMapper.ToCreateRows(Data(currencyType: 9), Leaf()));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:CurrencyTypeInvalid");
    }

    [Fact]
    public void Non_numeric_category_id_fails_fast()
    {
        var ex = Should.Throw<BusinessException>(
            () => N11RestPushMapper.ToCreateRows(Data(categoryId: "kategori-yok"), Leaf()));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:CategoryIdInvalid");
    }

    [Fact]
    public void Push_without_any_stock_item_is_rejected()
    {
        // SOAP'ta varyantsız ürün mümkündü; REST'te her satır bir SKU → gönderilecek hiçbir şey kalmaz.
        var ex = Should.Throw<BusinessException>(
            () => N11RestPushMapper.ToCreateRows(Data(new List<N11ProductStockItem>()), Leaf()));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:NoStockItemToPush");
    }

    [Fact]
    public void Images_are_carried_to_every_row_with_their_order()
    {
        // Görsel ürün seviyesinde tutuluyor ama REST'te her satır kendi listesini taşır.
        var rows = N11RestPushMapper.ToCreateRows(Data(), Leaf());

        rows.ShouldAllBe(r => r.Images.Count == 2);
        rows[0].Images[0].Url.ShouldBe("https://cdn/1.jpg");
        rows[0].Images[0].Order.ShouldBe(1);
    }
}
