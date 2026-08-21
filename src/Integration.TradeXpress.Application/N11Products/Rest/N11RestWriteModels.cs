using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integration.TradeXpress.N11Products;

// ── Zarf ────────────────────────────────────────────────────────────────────────────────────────────
// Üç yazma ucu da AYNI body şeklini kullanır: { "payload": { "integrator": "...", "skus": [ ... ] } }
// (doküman v9.0 §3.4 / §3.5 / §3.6 örnek request'leri). Zarf jenerik: satır tipi uca göre değişir.

/// <summary>N11 REST yazma body'sinin dış zarfı — <c>{"payload": {...}}</c>.</summary>
internal sealed record N11RestEnvelope<TSku>(N11RestPayload<TSku> Payload);

/// <summary>Zarfın içi — entegratör adı + SKU satırları. Alan adları camelCase serileşir (<c>integrator</c>, <c>skus</c>).</summary>
internal sealed record N11RestPayload<TSku>(string Integrator, IReadOnlyList<TSku> Skus);

// ── price-stock-update satırı ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Fiyat/stok güncelleme satırı (<c>POST /ms/product/tasks/price-stock-update</c>).
/// <para><b>Gönderilmeyen alan GÜNCELLENMEZ</b> — doküman: "İstekte mevcut olmayan alanlar için herhangi bir update
/// yapılmayacaktır." Bu yüzden null alanlar JSON'a <b>hiç yazılmaz</b> (<see cref="JsonIgnoreCondition.WhenWritingNull"/>);
/// null'ı açıkça yazmak "bu alanı boşalt" olarak yorumlanma riski taşır.</para>
/// <para><b>Fiyat çifti:</b> <see cref="ListPrice"/> ve <see cref="SalePrice"/> BİRLİKTE gönderilmelidir; yalnız biri
/// doluysa istemci fail-fast eder (N11 uzaktan REJECT ederdi). Yalnız <see cref="Quantity"/> göndermek serbesttir
/// (stok-only güncelleme).</para>
/// <para><b>Küsurat:</b> nokta ayracı + tam 2 hane zorunlu; dönüşümü <see cref="N11RestPrice"/> yapar.</para>
/// </summary>
public sealed record N11RestPriceStock(
    string StockCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ListPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SalePrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrencyType);

// ── product-create satırı ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Ürün yükleme satırı (<c>POST /ms/product/tasks/product-create</c>). <b>REST'te her SKU kendi başına tam bir ürün
/// kaydıdır</b> (title/description/categoryId dahil); varyantlar aynı <see cref="ProductMainId"/> ile gruplanır —
/// SOAP'taki "tek product + stockItems[]" şeklinden yapısal olarak FARKLIDIR.
/// <para><see cref="CategoryId"/> <b>en alt kırılım</b> (yaprak) kategori olmalıdır.
/// <see cref="ShipmentTemplate"/> kargo şablonunun <b>ADI</b>dır (id değil).
/// <see cref="VatRate"/> yalnız 0/1/10/20 olabilir. <see cref="CurrencyType"/> yalnız TL/USD/EUR.</para>
/// <para><b>Hızlı ürün yükleme (Mod 3):</b> <see cref="CatalogId"/> ya da <see cref="Barcode"/> verildiğinde
/// <see cref="Images"/> ve <see cref="Attributes"/> BOŞ olabilir — ama yine de (boş dizi olarak) gönderilir.
/// İkisi de doluysa <see cref="CatalogId"/> önceliklidir. <see cref="Barcode"/> N11 kataloğundaki bir barkodla
/// eşleşirse ürün, attribute'lara <b>bakılmaksızın</b> "satıcı onayı bekliyor" statüsünde açılır.</para>
///
/// <para><b><see cref="Bundle"/> DAİMA false gönderilir</b> (2026-08-05 Hakan kararı). N11 bu alanı
/// GÖNDERİLMEZSE <c>true</c> varsayar — yani sessiz kalmak riskli tarafı seçmek demekti. <c>true</c> iken
/// müşteri aynı ürünün İKİ FARKLI varyantını tek sepete koyabiliyor; bizim varyantlarımız çoğunlukla AYNI
/// maden havuzundan besleniyor (her varyantın satılabilir adedi havuza karşı BAĞIMSIZ hesaplanır), dolayısıyla
/// ikisi birden satılınca stok yetmiyor.
/// <b>Kapsam:</b> istisnasız tüm ürünler — Hakan "default'umuz her zaman false olsun" dedi. (Ben yalnız
/// <c>StockPolicy=Calculated</c> ürünlerle sınırlamayı önermiştim; bağımsız stoklu Fixed/Unlimited üründe
/// birleşme zararsız olurdu. Karar kullanıcının.)
/// <b>⚠ Bu tam çözüm DEĞİL:</b> yalnız AYNI SEPETİ engeller; iki AYRI siparişte aynı çakışma sürer. Kök çare
/// paylaşılan havuzun varyantlara bölüştürülmesi ya da tek-varyant listelemedir.</para>
/// </summary>
public sealed record N11RestProductCreate(
    string Title,
    string Description,
    long CategoryId,
    string CurrencyType,
    string ProductMainId,
    int PreparingDay,
    string ShipmentTemplate,
    string StockCode,
    int Quantity,
    IReadOnlyList<N11RestProductImage> Images,
    IReadOnlyList<N11RestProductAttribute> Attributes,
    decimal SalePrice,
    decimal ListPrice,
    int VatRate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxPurchaseQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CatalogId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Barcode = null,
    bool Bundle = false);

/// <summary>Ürün görseli — URL <b>https</b> olmak zorundadır (N11 kuralı), <c>order</c> görsel sırasıdır.
/// Görsel dosyası maksimum 10 MB (bunu N11 indirirken denetler, istemci denetleyemez).</summary>
public sealed record N11RestProductImage(string Url, int Order);

/// <summary>
/// Ürüne yazılan kategori özelliği — <c>product-create</c> body'sindeki alan adları <b><c>id</c> / <c>valueId</c> /
/// <c>customValue</c></b>'dur.
/// <para><b>Karıştırmayın:</b> <c>isMandatory</c>/<c>isVariant</c>/<c>isSlicer</c>/<c>isCustomValue</c> alanları
/// KATEGORİ ÖZELLİK SORGUSUNUN (<c>GET /cdn/category/{id}/attribute</c>) YANITINA aittir — özelliğin tanımını
/// betimlerler, ürün body'sine yazılmazlar (<c>N11AttributeDef</c> onları zaten okuyor).</para>
/// <para>Kural: kategori servisinden <c>isCustomValue=false</c> gelen özellikte <see cref="ValueId"/> zorunludur;
/// <c>true</c> gelende serbest metin (<see cref="CustomValue"/>) yazılabilir. <c>isVariant=true</c> özellikte aynı
/// ürüne <b>mükerrer değer</b> gönderilirse istek REJECT alır.</para>
/// </summary>
public sealed record N11RestProductAttribute(
    long Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ValueId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomValue);

// ── product-update satırı ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Ürün bilgisi güncelleme satırı (<c>POST /ms/product/tasks/product-update</c>) — fiyat/stok DIŞI alanlar için.
/// <para><b>⚠ SESSİZ NO-OP TUZAĞI:</b> <see cref="ProductMainId"/> yalnız <see cref="DeleteProductMainId"/> = <c>true</c>
/// iken güncellenir; bayrak <c>false</c>/gönderilmemişse N11 <b>hiçbir güncelleme yapmaz ve HATA DA DÖNMEZ</b>.
/// Aynısı <see cref="MaxPurchaseQuantity"/> ↔ <see cref="DeleteMaxPurchaseQuantity"/> çifti için geçerlidir.
/// Bayrağın adı "sil" ama işlevi "değiştirmeye izin ver" — sezgiye aykırıdır. İstemci bu sessiz no-op'u
/// fail-fast'e çevirir (değer var + bayrak yok ⇒ BusinessException).</para>
/// <para><see cref="Status"/>: <c>"Active"</c> = satışta, <c>"Suspended"</c> = satıştan çekildi (ürünü satıştan
/// çekmenin resmî REST yolu). <see cref="PreparingDay"/> 0'dan büyük olmalıdır.</para>
/// <para><see cref="VatRate"/> burada GERÇEKTEN opsiyoneldir (create'te zorunludur — iki uçta farklı!):
/// resmî destek merkezi makalesi 15865 "RestAPI Ürün Bilgileri Güncelleme Servisi" zorunluluk kolonunda
/// "Hayır" diyor (2026-08-03 doğrulaması). RestAPI docx'inin update tablosu "Evet" gösteriyor ama bu HATALI —
/// aynı docx'in kendi örnek isteği de vatRate göndermiyor. Göndermemek mevcut oranı KORUR; yani yanlış
/// girilmiş bir KDV oranı ürünü silmeden bu uçla düzeltilebilir. Verilirse 0/1/10/20 denetlenir.</para>
/// <para>Null alanlar JSON'a yazılmaz — güncellenmek istenmeyen alan hiç gönderilmez.</para>
/// </summary>
public sealed record N11RestProductUpdate(
    string StockCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PreparingDay = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShipmentTemplate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrencyType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DeleteProductMainId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProductMainId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DeleteMaxPurchaseQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxPurchaseQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VatRate = null);

// ── Fiyat biçimi ────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// N11 fiyat biçimi — TEK KAYNAK. Doküman: küsurat <b>nokta</b> ile ayrılmalı ("virgül kullanımı hata alınmasına
/// sebebiyet verecektir") ve noktadan sonra <b>tam 2 hane</b> iletilmelidir ("aksi takdirde isteğiniz REJECT alacaktır").
/// Hem JSON yazımı hem de yerel guard'lar (listPrice ≥ salePrice karşılaştırması) aynı normalize edilmiş değeri
/// kullanır — aksi hâlde 2 haneye yuvarlanınca sıralama değişip N11 tarafında REJECT'e düşebilirdi.
/// </summary>
internal static class N11RestPrice
{
    /// <summary>Fiyatı N11'e gidecek hâline indirger (2 hane, yarımlar sıfırdan uzağa).</summary>
    public static decimal Normalize(decimal value)
    {
        return decimal.Round(value, N11RestConsts.PriceDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Fiyatı N11'in beklediği metne çevirir: InvariantCulture (nokta ayracı) + tam 2 hane.</summary>
    public static string Format(decimal value)
    {
        return Normalize(value).ToString("0.00", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Fiyat alanlarını JSON'a <b>sayı</b> olarak ama tam 2 haneyle yazan dönüştürücü.
/// <para>Neden ham yazım: <c>WriteNumberValue(2000.00m)</c> "2000" üretir (sondaki sıfırlar düşer) ⇒ N11 REJECT.
/// Metne çevirip <c>WriteStringValue</c> kullanmak ise alanı dokümandaki <c>number</c> tipinden çıkarırdı.
/// <c>WriteRawValue</c> ikisini de çözer: JSON sayı token'ı, tam 2 hane.</para>
/// <para><see cref="decimal"/> için kaydedilir; <c>decimal?</c> alanlarda System.Text.Json bunu otomatik sarar,
/// null'lar zaten property üzerindeki <see cref="JsonIgnoreCondition.WhenWritingNull"/> ile hiç yazılmaz.</para>
/// </summary>
internal sealed class N11PriceJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Yanıtlarda fiyat metin olarak da gelebiliyor; iki token tipini de kabul et (okuma yolu yalnız tolerans için).
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(N11RestPrice.Format(value));
    }
}
