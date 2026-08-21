using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL HAFİF FİYAT/STOK BODY'Sİ — <see cref="TrendyolProductClient.BuildPriceInventoryBody"/>.
///
/// <para><b>Neden body seviyesinde pinliyoruz:</b> bu dilim saf taşıma katmanı ve tek gerçek riski body'nin
/// ŞEKLİ. Yanlış alan adı ya da yanlış atlama, Trendyol'dan HTTP 200 dönerken bile yanlış stoğu/fiyatı yazar —
/// hiçbir istisna çıkmaz, log temiz görünür, yalnız pazaryerindeki sayı yanlıştır. HTTP katmanı (URL, auth,
/// User-Agent) burada KOŞMUYOR: <c>BaseUrl</c> sabit ve Trendyol için mock sunucu yok; o boşluk go-live smoke'una
/// ve ayrı bir mock dilimine bırakıldı (bilinçli, kayıtlı).</para>
///
/// <para><b>Testin kalbi T2 + T12 ikilisidir:</b> <c>null</c> = "bu alana dokunma" (anahtar hiç yazılmaz),
/// <c>0</c> = "sıfırla" (yazılır). İkisi karışırsa ya stok sessizce sıfırlanır ya da satışı durdurma yolu
/// (adet-0) yutulur. Tabanda ortak <c>JsonSerializerOptions</c> olmadığı için atlama ELLE yapılıyor —
/// yani bu ayrım kütüphane garantisi değil, bizim kodumuzun sorumluluğu.</para>
/// </summary>
public class TrendyolPriceInventoryBodyTests
{
    private static TrendyolPriceInventoryItem Row(
        string barcode, int? qty = 5, decimal? list = 120m, decimal? sale = 100m)
    {
        return new TrendyolPriceInventoryItem(barcode, qty, list, sale);
    }

    private static JsonElement FirstItem(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("items")[0].Clone();
    }

    /// <summary>T1 — zarf ve alan adları BİREBİR. Alan adı Trendyol'un sözleşmesidir; "quantity" yerine "stock"
    /// yazmak sessizce hiçbir şey güncellemez.</summary>
    [Fact]
    public void Body_writes_items_envelope_with_exact_field_names()
    {
        var body = TrendyolProductClient.BuildPriceInventoryBody(new[] { Row("BC-1", 7, 150m, 130m) });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "items" });

        var item = doc.RootElement.GetProperty("items")[0];
        item.EnumerateObject().Select(p => p.Name)
            .ShouldBe(new[] { "barcode", "quantity", "listPrice", "salePrice" });
        item.GetProperty("barcode").GetString().ShouldBe("BC-1");
        item.GetProperty("quantity").GetInt32().ShouldBe(7);
        item.GetProperty("listPrice").GetDecimal().ShouldBe(150m);
        item.GetProperty("salePrice").GetDecimal().ShouldBe(130m);
    }

    /// <summary>T2 — <c>null</c> alan JSON'a <b>hiç yazılmaz</b> (null olarak da yazılmaz). Trendyol tarafında
    /// açıkça gönderilen bir alan "bunu ez" demektir; null yazmak uzaktaki doğru değeri silebilirdi.</summary>
    [Fact]
    public void Null_fields_are_omitted_not_written_as_null()
    {
        var body = TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-2", null, 120m, 100m) });

        var item = FirstItem(body);
        item.TryGetProperty("quantity", out _).ShouldBeFalse();
        body.ShouldNotContain("null");
    }

    /// <summary>T3 — kısmi güncelleme iki yönlü çalışır: yalnız fiyat ya da yalnız stok.</summary>
    [Fact]
    public void Partial_rows_carry_only_the_fields_that_were_set()
    {
        var priceOnly = FirstItem(TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-3", null, 120m, 100m) }));
        priceOnly.TryGetProperty("quantity", out _).ShouldBeFalse();
        priceOnly.GetProperty("listPrice").GetDecimal().ShouldBe(120m);

        var stockOnly = FirstItem(TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-4", 9, null, null) }));
        stockOnly.GetProperty("quantity").GetInt32().ShouldBe(9);
        stockOnly.TryGetProperty("listPrice", out _).ShouldBeFalse();
        stockOnly.TryGetProperty("salePrice", out _).ShouldBeFalse();
    }

    /// <summary>T12 — <c>0</c> adet YAZILIR. Bu, "0 kurulabilir varyant → satışı durdur" kararının taşıyıcısıdır;
    /// atlanırsa stok bitmiş ürün pazaryerinde satılmaya devam eder.</summary>
    [Fact]
    public void Zero_quantity_is_written_not_omitted()
    {
        var item = FirstItem(TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-5", 0, null, null) }));

        item.GetProperty("quantity").GetInt32().ShouldBe(0);
    }

    /// <summary>T4 — boş liste sessizce POST edilmez ("gönderdim" deyip hiçbir şey yapmamak en sinsi başarıdır).</summary>
    [Fact]
    public void Empty_items_fails_fast()
    {
        Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(new TrendyolPriceInventoryItem[0]))
            .Code.ShouldBe("TradeXpress:Trendyol:Product:EmptyItems");
    }

    /// <summary>T5 — 1000 satır tavanı aşılırsa SESSİZ kırpma yok, dostane hata var. Kırpsaydık gönderilmeyen
    /// satırlar "gönderildi" sayılır ve LastSent* zinciri yalan söylerdi.</summary>
    [Fact]
    public void Item_count_above_the_limit_fails_fast()
    {
        var rows = Enumerable.Range(0, 1001).Select(i => Row($"BC-{i}")).ToList();

        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(rows));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:TooManyItems");
        ex.Data["max"].ShouldBe(1000);
        ex.Data["count"].ShouldBe(1001);
    }

    /// <summary>T6 — aynı istekte tekrarlanan barkodda hangi satırın kazandığı TANIMSIZ → reddedilir.</summary>
    [Fact]
    public void Duplicate_barcode_fails_fast()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[] { Row("BC-DUP"), Row("BC-DUP", 9) }));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:DuplicateBarcode");
        ex.Data["barcode"].ShouldBe("BC-DUP");
    }

    [Fact]
    public void Blank_barcode_fails_fast_with_its_row_index()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[] { Row("BC-OK"), Row("   ") }));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:BarcodeRequired");
        ex.Data["index"].ShouldBe(1);
    }

    /// <summary>T7 — dört alanı da boş satır kotayı ve 15 dk penceresini boşa harcar; çağıranın hatasıdır.</summary>
    [Fact]
    public void Row_with_all_null_fields_fails_fast()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-6", null, null, null) }));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:NothingToUpdate");
        ex.Data["barcode"].ShouldBe("BC-6");
    }

    /// <summary>T8 — fiyat ÇİFT gönderilir. Tek fiyatla gidilirse Trendyol'un "listPrice &gt;= salePrice" kuralı
    /// UZAKTAKİ eski değere karşı işletilir; sonuç bizim göremediğimiz bir red olur.</summary>
    [Theory]
    [InlineData(120, null)]
    [InlineData(null, 100)]
    public void A_single_price_field_fails_fast(int? list, int? sale)
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[] { new TrendyolPriceInventoryItem("BC-7", 3, list, sale) }));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:PriceFieldsMustBePaired");
    }

    /// <summary>T9 — liste fiyatı satış fiyatının altına düşemez; EŞİTLİK serbesttir (indirimsiz ürün).</summary>
    [Fact]
    public void List_price_below_sale_price_fails_but_equal_prices_pass()
    {
        Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
                new[] { Row("BC-8", 3, list: 90m, sale: 100m) }))
            .Code.ShouldBe("TradeXpress:Trendyol:Product:ListPriceBelowSalePrice");

        Should.NotThrow(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[] { Row("BC-9", 3, list: 100m, sale: 100m) }));
    }

    [Fact]
    public void Negative_quantity_and_negative_price_fail_fast()
    {
        Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
                new[] { Row("BC-10", qty: -1) }))
            .Code.ShouldBe("TradeXpress:Trendyol:Product:QuantityOutOfRange");

        Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
                new[] { Row("BC-11", list: -5m, sale: -5m) }))
            .Code.ShouldBe("TradeXpress:Trendyol:Product:PriceNegative");
    }

    /// <summary>T11 — TEL TUZAĞI: bozuk satır listenin SONUNA konur. Hata o satıra aitse, önündeki geçerli satır
    /// tüm guard'lardan geçmiş demektir → guard'ların ilk satırda erken dönüp kalanları atlamadığı kanıtlanır.
    /// Bu iddia olmadan "hepsi geçti" ile "yalnız ilki bakıldı" ayırt edilemezdi.</summary>
    [Fact]
    public void Guards_run_for_every_row_not_just_the_first()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.BuildPriceInventoryBody(
            new[]
            {
                Row("BC-GOOD"),
                Row("BC-BAD", list: 10m, sale: 999m),
            }));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:ListPriceBelowSalePrice");
        ex.Data["barcode"].ShouldBe("BC-BAD");
    }

    /// <summary>Çok satırlı body SIRAYI korur — satır sırası kaybolursa hata ayıklamada "hangi satır" sorusu
    /// cevapsız kalır (ve ileride item-bazlı statü eşleştirmesi buna dayanacak).</summary>
    [Fact]
    public void Rows_keep_their_order()
    {
        var body = TrendyolProductClient.BuildPriceInventoryBody(
            new[] { Row("BC-A"), Row("BC-B"), Row("BC-C") });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("barcode").GetString())
            .ShouldBe(new[] { "BC-A", "BC-B", "BC-C" });
    }
}
