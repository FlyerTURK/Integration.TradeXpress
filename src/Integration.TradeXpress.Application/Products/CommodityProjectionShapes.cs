using System.Collections.Generic;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir emtia ailesinin PROJEKSİYON ŞEKLİ — köprünün (emtia ⇄ ürün) hangi kolları çalıştıracağını söyler.
///
/// <para><b>Neden veri, kod değil:</b> "hangi aile varyant taşır, hangisi medya taşır" bilgisi bugüne dek
/// hiçbir yerde yazılı değildi ve koddan defalarca yeniden çıkarıldı (CLAUDE.md §6 "EMTİA AİLELERİ VARYANT
/// AÇISINDAN ÜÇ KATEGORİDİR" maddesi tam bu yüzden yazıldı: aynı gün ÜÇ kez türetildi ve bir turda yanlış
/// gruplandı). Tek bir tabloya bağlanınca hem projeksiyon hem konvansiyon testi AYNI cevabı okur.</para>
///
/// <para><b><see cref="VariantMediaContext"/> yalnız varyant taşıyan ailede doludur.</b> Varyantsız ailede
/// (Hurda · Vadeli · Taş · Hizmet) varyant bağlamı bir yer tutucu bile değildir: o ailede varyant KAYDI
/// yoktur, dolayısıyla bağlanacak medya da yoktur.</para>
/// </summary>
/// <param name="EntityName">Agnostik sahip adı — hem varyant grafının (<c>EntityVariant.EntityName</c>) hem
/// kayıt-geneli medyanın anahtarı. İkisi bu kod tabanında AYNI kavramdır (bkz. <c>CommodityAgnosticGraph</c>).</param>
/// <param name="CarriesVariantGraph">Aile nitelik + varyant grafı taşır mı (① tam varyantlı ve ② uzantısız
/// varyantlı aileler <c>true</c>; ③ varyantsız aileler <c>false</c>).</param>
/// <param name="RecordMediaContext">Kayıt-geneli medya bağlamı; <c>null</c> = bu aile medya taşımaz.</param>
/// <param name="VariantMediaContext">Varyant-farkı medya bağlamı; <c>null</c> = varyant medyası yok.</param>
public sealed record CommodityProjectionShape(
    string EntityName,
    bool CarriesVariantGraph,
    string? RecordMediaContext,
    string? VariantMediaContext);

/// <summary>
/// YEDİ emtia ailesinin projeksiyon şekli — TEK KAYNAK.
///
/// <para><b>Kapsam kasıtlı olarak dar:</b> bu tablo "aile nedir" sorusunun genel cevabı DEĞİL, yalnız
/// köprünün ihtiyacıdır (varyant kolu + medya bağlamları). Genel bir aile kayıt defteri açmak, bugün
/// ihtiyaç olmayan alanları da davet ederdi (YAGNI).</para>
///
/// <para><b>Bilinmeyen aile SESSİZ GEÇİLMEZ:</b> <see cref="Of"/> tanımadığı aileye varsayılan üretmez,
/// fail-fast eder. Varsayılan üretmek, sekizinci bir aile eklendiğinde onu "varyantsız + medyasız" diye
/// sessizce yanlış tarafa düşürürdü.</para>
///
/// <para><b>İLERİ YÖN DE BURADAN TÜRER (2026-08-20 birleştirmesi):</b> köprünün ürün→emtia yönü
/// <see cref="ProductProjectionShape"/> enum'unu kullanır ve o enum artık bu tablodan HESAPLANIR
/// (<see cref="ForwardShapeOf"/>). Önceden iki yön aynı üç-kategori sınıflandırmasını AYRI AYRI beyan
/// ediyordu: tutarlıydılar ama sekizinci bir aile eklendiğinde birinin güncellenip diğerinin unutulması tam
/// olarak bu projede defalarca yaşanan desendir — sapma da sessizdir (yanlış şekil istisna fırlatmaz, yalnız
/// form eksik açılır). Artık aile kategorisini DEĞİŞTİRMEK için tek bir satır vardır.</para>
/// </summary>
public static class CommodityProjectionShapes
{
    /// <summary>Köprünün tanıdığı emtia aileleri — konvansiyon testi <c>CommodityProjectionShapeTests</c> bu
    /// listeyi gezip her birinin şeklini doğrular.</summary>
    public static IReadOnlyList<ProcessType> Families { get; } = new[]
    {
        ProcessType.Metal,
        ProcessType.Scrap,
        ProcessType.Future,
        ProcessType.Jewelry,
        ProcessType.Stone,
        ProcessType.Good,
        ProcessType.Service,
    };

    /// <summary>Ailenin ÜRÜN → EMTİA (ileri yön) projeksiyon şekli — bu tablodan TÜRETİLİR, ayrıca beyan
    /// EDİLMEZ: varyant grafı taşıyan aile tam graf, yalnız kayıt-geneli medyası olan aile medya, ikisi de
    /// olmayan aile yalnız kimlik taşır. Tanınmayan ailede <see cref="Of"/> fail-fast eder.</summary>
    public static ProductProjectionShape ForwardShapeOf(ProcessType family)
    {
        var shape = Of(family);
        if (shape.CarriesVariantGraph)
        {
            return ProductProjectionShape.FullGraph;
        }

        return shape.RecordMediaContext is null
            ? ProductProjectionShape.Identity
            : ProductProjectionShape.RecordMedia;
    }

    /// <summary>Ailenin projeksiyon şekli; tanınmayan aile için fail-fast.</summary>
    public static CommodityProjectionShape Of(ProcessType family)
    {
        switch (family)
        {
            case ProcessType.Metal:
                return MetalShape;

            case ProcessType.Scrap:
                return ScrapShape;

            case ProcessType.Future:
                return FutureShape;

            case ProcessType.Jewelry:
                return JewelryShape;

            case ProcessType.Stone:
                return StoneShape;

            case ProcessType.Good:
                return GoodShape;

            case ProcessType.Service:
                return ServiceShape;
        }

        throw new BusinessException("TradeXpress:Commodity:UnknownProjectionFamily")
            .WithData("Family", family);
    }

    // ── Aile katalogları (private, en altta) ────────────────────────────────────────────────────────
    //
    // ① TAM VARYANTLI (Metal · Good) ve ② UZANTISIZ VARYANTLI (Jewelry): nitelik + varyant grafı TAŞINIR;
    //    aileye özel alanların varyantta mı entity'de mi yaşadığı köprüyü ilgilendirmez — teknik alanların
    //    hiçbiri zaten taşınmaz (CLAUDE.md "ürün müşteriye bakar, emtia tekniğe bakar").
    //
    // ③ VARYANTSIZ (Scrap · Future · Stone · Service): varyant kolu HİÇ çalışmaz. Bu bir eksiklik değil
    //    TASARIMDIR ("vadeli varyant barındırmaz" · "her taşın parmak izi ayrıdır" · stoklanmayan hizmetin
    //    varyantı olmaz). Taş, varyantsız olmasına RAĞMEN kayıt-geneli medya taşır — ikisi ayrı sorulardır.

    private static readonly CommodityProjectionShape MetalShape = new(
        EntityName: MediaEntityNames.Metal,
        CarriesVariantGraph: true,
        RecordMediaContext: MediaEntityNames.Metal,
        VariantMediaContext: MediaEntityNames.MetalVariant);

    private static readonly CommodityProjectionShape JewelryShape = new(
        EntityName: MediaEntityNames.Jewelry,
        CarriesVariantGraph: true,
        RecordMediaContext: MediaEntityNames.Jewelry,
        VariantMediaContext: MediaEntityNames.JewelryVariant);

    private static readonly CommodityProjectionShape GoodShape = new(
        EntityName: MediaEntityNames.Good,
        CarriesVariantGraph: true,
        RecordMediaContext: MediaEntityNames.Good,
        VariantMediaContext: MediaEntityNames.GoodVariant);

    private static readonly CommodityProjectionShape StoneShape = new(
        EntityName: MediaEntityNames.Stone,
        CarriesVariantGraph: false,
        RecordMediaContext: MediaEntityNames.Stone,
        VariantMediaContext: null);

    private static readonly CommodityProjectionShape ScrapShape = new(
        EntityName: "Scrap",
        CarriesVariantGraph: false,
        RecordMediaContext: null,
        VariantMediaContext: null);

    private static readonly CommodityProjectionShape FutureShape = new(
        EntityName: "Future",
        CarriesVariantGraph: false,
        RecordMediaContext: null,
        VariantMediaContext: null);

    private static readonly CommodityProjectionShape ServiceShape = new(
        EntityName: "Service",
        CarriesVariantGraph: false,
        RecordMediaContext: null,
        VariantMediaContext: null);
}
