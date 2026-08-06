using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.N11Categories;
using Volo.Abp;

namespace Integration.TradeXpress.N11Products.Rest;

/// <summary>
/// SOAP push verisini (<see cref="N11ProductData"/>) REST <c>product-create</c> satırlarına çevirir.
///
/// <para><b>Neden ayrı bir çevirici gerekiyor — iki uç AYNI ŞEYİ farklı modelliyor:</b> SOAP'ta bir ürün
/// gönderilir ve varyantlar onun İÇİNDE <c>stockItems</c> olarak taşınır (tek fiyat, tek attribute seti).
/// REST'te ise hiyerarşi DÜZLEŞTİRİLMİŞTİR: her SKU bağımsız bir ürün satırıdır ve varyantlığı yalnız ortak
/// <c>productMainId</c> kurar. Yani N varyantlı bir listeleme SOAP'ta 1, REST'te N satır demektir; fiyat,
/// stok, görsel ve nitelik her satırda ayrı ayrı yazılır.</para>
///
/// <para><b>Nitelik kimliği:</b> SOAP serbest metin (<c>name</c>/<c>value</c>) kabul ederdi; REST kategori
/// servisinden gelen SAYISAL kimlikleri ister (<c>id</c> + <c>valueId</c>). Çeviri kategori yaprağının kendi
/// tanımından yapılır — uydurma kimlik ÜRETİLMEZ, çözülemeyen nitelikte fail-fast atılır. Ad/değer eşlemesi
/// <c>N11ProductPushValidator</c> ile <b>BİREBİR AYNI</b> kuralı kullanır (tr-TR, harf duyarsız): iki yer
/// ayrışırsa doğrulamadan geçen bir değer çeviride patlar.</para>
///
/// <para><b>Saf sınıf:</b> ağ/DI/DB yok → birim test edilir. Wiring çağıranın işidir.</para>
/// </summary>
public static class N11RestPushMapper
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Push verisini SKU başına bir <c>product-create</c> satırına çevirir.
    /// </summary>
    /// <param name="data">SOAP push planının ürettiği ürün verisi (doğrulanmış, kanonik değerlerle).</param>
    /// <param name="leaf">Kategori yaprağının attribute tanımları — sayısal kimliklerin TEK kaynağı.</param>
    public static IReadOnlyList<N11RestProductCreate> ToCreateRows(N11ProductData data, N11LeafAttributes leaf)
    {
        Check.NotNull(data, nameof(data));
        Check.NotNull(leaf, nameof(leaf));

        if (data.StockItems.Count == 0)
        {
            // SOAP'ta varyantsız ürün mümkündü; REST'te her satır bir SKU olduğundan gönderilecek hiçbir şey kalmaz.
            throw Fail("TradeXpress:N11:Rest:NoStockItemToPush", data.ProductSellerCode);
        }

        var categoryId = ParseCategoryId(data.CategoryId, data.ProductSellerCode);
        var vatRate = RequireVatRate(data.VatRate, data.ProductSellerCode);
        var images = data.Images
            .Select(i => new N11RestProductImage(i.Url, i.Order))
            .ToList();

        // Ürün-seviyesi nitelikler HER satıra yazılır: REST'te satırlar bağımsız ürünlerdir, ortak bir
        // "ürün başlığı" bloğu yoktur. Varyant ekseni satırın kendi niteliğinden gelir ve ürün-seviyesini EZER
        // (aynı attribute id iki kez gönderilirse N11 tanımsız davranır).
        var productLevel = ResolveAttributes(data.Attributes, leaf, data.ProductSellerCode);

        return data.StockItems
            .Select(item => ToCreateRow(data, leaf, item, categoryId, vatRate, images, productLevel))
            .ToList();
    }

    private static N11RestProductCreate ToCreateRow(
        N11ProductData data,
        N11LeafAttributes leaf,
        N11ProductStockItem item,
        long categoryId,
        int vatRate,
        IReadOnlyList<N11RestProductImage> images,
        IReadOnlyList<N11RestProductAttribute> productLevel)
    {
        // Satır fiyatı: varyantın kendi optionPrice'ı varsa o, yoksa ürün taban fiyatı (SOAP zinciriyle aynı).
        var salePrice = item.OptionPrice ?? data.Price;

        var variantLevel = ResolveAttributes(item.Attributes, leaf, item.SellerStockCode);

        return new N11RestProductCreate(
            Title: data.Title,
            Description: data.Description,
            CategoryId: categoryId,
            CurrencyType: MapCurrencyType(data.CurrencyType, item.SellerStockCode),
            // productMainId = varyantları GRUPLAYAN kimlik. Kayıt-bazlı upsert kimliğimiz (SellerCode) tam olarak
            // bu işi görür: aynı listelemenin tüm SKU'ları bir arada kalır, farklı listelemeler karışmaz.
            ProductMainId: data.ProductSellerCode,
            PreparingDay: data.PreparingDay,
            ShipmentTemplate: data.ShipmentTemplate,
            StockCode: item.SellerStockCode,
            Quantity: item.Quantity,
            Images: images,
            Attributes: MergeAttributes(productLevel, variantLevel),
            SalePrice: salePrice,
            // listPrice ≥ salePrice ZORUNLU (aksi REJECT). Ayrı bir liste fiyatı kavramımız yok → eşit gönderilir;
            // doküman bunu açıkça mümkün kılıyor ("aynı değer gönderebilirsiniz").
            ListPrice: salePrice,
            VatRate: vatRate,
            MaxPurchaseQuantity: data.MaxPurchaseQuantity,
            // AÇIKÇA yazılıyor (kaydın varsayılanına güvenilmiyor): gönderilmezse N11 true varsayar ve aynı
            // ürünün iki varyantı tek sepete girebilir — varyantlarımız aynı maden havuzunu paylaştığı için
            // bu doğrudan aşırı satış kapısıdır. Gerekçe + kapsam kararı: N11RestProductCreate.Bundle.
            Bundle: false);
    }

    /// <summary>Ürün-seviyesi + varyant nitelikleri <c>id</c> bazında birleştirir; çakışmada VARYANT kazanır
    /// (varyant ekseni ürün seviyesini ezmelidir). Aynı id iki kez gönderilmez.</summary>
    private static IReadOnlyList<N11RestProductAttribute> MergeAttributes(
        IReadOnlyList<N11RestProductAttribute> productLevel,
        IReadOnlyList<N11RestProductAttribute> variantLevel)
    {
        var merged = new Dictionary<long, N11RestProductAttribute>();
        foreach (var attribute in productLevel)
        {
            merged[attribute.Id] = attribute;
        }

        foreach (var attribute in variantLevel)
        {
            merged[attribute.Id] = attribute;
        }

        return merged.Values.ToList();
    }

    /// <summary>Ad/değer çiftlerini kategori tanımından sayısal kimliğe çevirir.
    ///
    /// <para>Değer listesi olan nitelikte <c>valueId</c> ZORUNLUDUR (doküman: "CustomValue&gt;false ise valueId
    /// yazılmalıdır"). Tanım SOAP fallback'inden geldiyse <c>ValueId</c> null olur — o hâlde REST'e
    /// gönderilebilecek bir kimlik yoktur ve fail-fast atılır: serbest metin göndermek N11'de sessiz redde
    /// ya da yanlış filtrelemeye yol açar.</para></summary>
    private static IReadOnlyList<N11RestProductAttribute> ResolveAttributes(
        IReadOnlyList<N11ProductAttributePair> pairs,
        N11LeafAttributes leaf,
        string stockCodeForError)
    {
        var resolved = new List<N11RestProductAttribute>();

        foreach (var pair in pairs)
        {
            var def = leaf.Attributes.FirstOrDefault(d => NameEquals(d.Name, pair.Name));
            if (def is null)
            {
                throw Fail("TradeXpress:N11:Rest:AttributeNotInCategory", stockCodeForError)
                    .WithData("AttributeName", pair.Name);
            }

            var attributeId = ParseId(def.AttributeId, stockCodeForError, def.Name);

            if (def.IsCustomValue || def.Values.Count == 0)
            {
                resolved.Add(new N11RestProductAttribute(attributeId, ValueId: null, CustomValue: pair.Value));
                continue;
            }

            var match = def.Values.FirstOrDefault(v => NameEquals(v.Value, pair.Value));
            if (match is null)
            {
                throw Fail("TradeXpress:N11:Rest:AttributeValueNotInList", stockCodeForError)
                    .WithData("AttributeName", def.Name)
                    .WithData("AttributeValue", pair.Value);
            }

            if (string.IsNullOrWhiteSpace(match.ValueId))
            {
                throw Fail("TradeXpress:N11:Rest:AttributeValueIdMissing", stockCodeForError)
                    .WithData("AttributeName", def.Name)
                    .WithData("AttributeValue", pair.Value);
            }

            resolved.Add(new N11RestProductAttribute(
                attributeId,
                ValueId: ParseId(match.ValueId, stockCodeForError, def.Name),
                CustomValue: null));
        }

        return resolved;
    }

    // N11 SOAP'ta currencyType SAYIDIR (1=TL), REST'te METİNDİR (TL/USD/EUR) — çeviri burada, tek yerde.
    private static string MapCurrencyType(int currencyType, string stockCodeForError)
    {
        return currencyType switch
        {
            1 => "TL",
            2 => "USD",
            3 => "EUR",
            _ => throw Fail("TradeXpress:N11:Rest:CurrencyTypeInvalid", stockCodeForError)
                .WithData("CurrencyType", currencyType),
        };
    }

    private static long ParseCategoryId(string categoryExternalId, string stockCodeForError)
    {
        if (!long.TryParse(categoryExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Fail("TradeXpress:N11:Rest:CategoryIdInvalid", stockCodeForError)
                .WithData("CategoryId", categoryExternalId);
        }

        return parsed;
    }

    private static long ParseId(string raw, string stockCodeForError, string attributeName)
    {
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Fail("TradeXpress:N11:Rest:AttributeIdInvalid", stockCodeForError)
                .WithData("AttributeName", attributeName)
                .WithData("Value", raw);
        }

        return parsed;
    }

    /// <summary>KDV oranı REST create'te ZORUNLUDUR ve kapalı kümededir. Boşsa push HİÇ denenmez —
    /// uydurma oran yanlış fatura demektir (2026-08-03 Hakan kuralı).</summary>
    private static int RequireVatRate(int? vatRate, string stockCodeForError)
    {
        if (vatRate is not { } rate)
        {
            throw Fail("TradeXpress:N11:Rest:VatRateRequired", stockCodeForError);
        }

        if (!N11ProductConsts.AllowedVatRates.Contains(rate))
        {
            throw Fail("TradeXpress:N11:Rest:VatRateInvalid", stockCodeForError).WithData("VatRate", rate);
        }

        return rate;
    }

    // Türkçe-duyarsız ad/değer karşılaştırması — N11ProductPushValidator.NameEquals ile BİREBİR AYNI kural.
    // Ayrışırlarsa doğrulamadan geçen bir değer burada patlar; o yüzden kural iki yerde de aynı satır.
    private static bool NameEquals(string? left, string? right)
    {
        return string.Compare(left?.Trim(), right?.Trim(), Turkish, CompareOptions.IgnoreCase) == 0;
    }

    private static BusinessException Fail(string code, string stockCode)
    {
        return new BusinessException(code).WithData("StockCode", stockCode);
    }
}
