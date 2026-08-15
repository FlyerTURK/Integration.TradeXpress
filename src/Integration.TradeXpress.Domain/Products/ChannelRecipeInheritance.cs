using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satırının EMTİA İMZASI — kanal reçetesinin çekirdeği izleyip izlemediği bununla anlaşılır.
///
/// <para><b>Neden yalnız bu alanlar:</b> imza, satırın <i>fiziksel bileşim</i> beyanıdır. Sıra
/// (<c>LineOrder</c>), açıklama ve hesaplanmış tutar (<c>Amount</c>) dışarıda: ilki kozmetik, sonuncusu
/// canlı hesaplanan bir TÜREV (katalog fiyatı değişince kendiliğinden değişir) — onu imzaya koymak,
/// kullanıcı hiçbir şeye dokunmadan "override edilmiş" sonucunu verirdi.</para>
/// </summary>
public readonly record struct RecipeCommoditySignature(
    ProcessType? CommodityProcessType,
    Guid? CommodityId,
    Guid? CommodityVariantId,
    decimal Quantity,
    decimal Factor,
    Guid? ValuationUnitId);

/// <summary>Emtia imzası taşıyan reçete satırı — çekirdek (<see cref="ProductVariantRecipeLine"/>) ve
/// kanal klonları (N11/Trendyol) ortak yüzeyi. Üçü de bu alanları ZATEN taşıyordu; arayüz yalnız tek bir
/// karşılaştırıcının üçüne birden hizmet edebilmesi için eklendi (üç ayrı kopya = üç ayrı sapma).</summary>
public interface IRecipeCommodityLine
{
    ProcessType? CommodityProcessType { get; }

    Guid? CommodityId { get; }

    Guid? CommodityVariantId { get; }

    decimal Quantity { get; }

    decimal Factor { get; }

    Guid? ValuationUnitId { get; }

    /// <summary>Dolu ise satır bir YAN MALİYETTİR (komisyon · kargo · paketleme) — kanalın kendi malı.</summary>
    SideCostKind? SideCostKind { get; }
}

/// <summary>
/// KANAL REÇETESİ ÇEKİRDEĞİ İZLİYOR MU — devir/override kararının TEK yeri (2026-08-11 Hakan tasarımı).
///
/// <para><b>Model:</b> çekirdek varyant reçetesi OTORİTEDİR; kanal reçetesi ondan türer ve yalnız
/// <i>override</i> hakkı vardır. Otoritenin çekirdekte olması tercih değil ZORUNLULUKTUR: stok zinciri
/// (ters-endeks → <c>SellableStockCalculator</c>) ve sipariş rezervasyonu yalnız çekirdek reçeteyi okur.
/// Emtia sadece kanal reçetesinde yaşasaydı stok zinciri onu HİÇ görmez, ürün hiç düşmez ve sessizce
/// aşırı satış üretirdi.</para>
///
/// <para><b>Neden KALICI BAYRAK yok</b> (Hakan, 2026-08-11): "override edildi" bilgisi ayrı bir kolonda
/// tutulsaydı, o kolonun kendisi yalan söyleyebilen ikinci bir durum olurdu (bu projede tam bu desen
/// defalarca sessiz sapmayla bitti). Cevabı VERİNİN KENDİSİ veriyor: imzalar aynıysa devralınmıştır,
/// farklıysa kullanıcı dokunmuştur. Bayrak yok, migration yok, bakılacak ikinci gerçek yok.</para>
///
/// <para><b>YAN MALİYETLER KARŞILAŞTIRMAYA GİRMEZ</b> (Hakan'ın ısrarla ayırdığı nokta): paketleme ve
/// kargo her kanalda meşru şekilde farklıdır; komisyon zaten kanalın kategorisinden gelir. Bunları
/// bileşim farkı sanmak, her kanalı kalıcı olarak "override edilmiş" ilan eder ve devir mekanizması hiç
/// çalışmazdı. Ayrım <c>ComponentType</c> üzerinden DEĞİL <c>SideCostKind</c> üzerinden yapılır: çekirdek
/// İŞÇİLİK satırları <c>ComponentType = Service</c>'tir ama fiziksel bileşimin parçasıdır ve devralınmalıdır;
/// onları <c>SideCostKind == null</c> koşulu doğru tarafta tutar.</para>
/// </summary>
public static class ChannelRecipeInheritance
{
    /// <summary>
    /// Kanal reçetesi çekirdeği İZLİYOR mu (devralınmış mı)?
    ///
    /// <para><c>true</c> → kullanıcı kanalda bileşime dokunmamış; çekirdek değişince kanal TAZELENEBİLİR.
    /// <c>false</c> → override var; kanala DOKUNULMAZ.</para>
    ///
    /// <para><b>Boş çekirdek + boş kanal = devralınmış</b> sayılır: henüz sınıflandırılmamış ürün, üzerine
    /// yazılacak bir kullanıcı kararı taşımaz — devir mekanizmasının asıl hedefi tam da bu durumdur.</para>
    /// </summary>
    public static bool IsInherited(
        IEnumerable<IRecipeCommodityLine> coreLines,
        IEnumerable<IRecipeCommodityLine> channelLines)
    {
        return IsInherited(SnapshotOf(coreLines), channelLines);
    }

    /// <summary>
    /// Aynı karar, çekirdek tarafı ÖNCEDEN ALINMIŞ İMZA FOTOĞRAFI olarak — <see cref="SnapshotOf"/> ile.
    ///
    /// <para><b>Neden gerekli:</b> "kayıt-öncesi çekirdek" tek UoW içinde entity REFERANSI olarak tutulamaz —
    /// EF kimlik haritası aynı satırı aynı instance'la döndürür ve yerinde güncelleme o instance'ı mutasyona
    /// uğratır; "eski" liste sessizce yeni değerleri gösterir, kıyas hep "aynı" der ve tazeleme HİÇ çalışmaz.
    /// İmzalar değer tipi (<see cref="RecipeCommoditySignature"/> record struct) olduğundan fotoğraf gerçekten
    /// donuktur.</para>
    /// </summary>
    public static bool IsInherited(
        IReadOnlyList<RecipeCommoditySignature> coreSignatures,
        IEnumerable<IRecipeCommodityLine> channelLines)
    {
        var core = CountBySignature(coreSignatures);
        var channel = CountBySignature(SnapshotOf(channelLines));

        if (core.Count != channel.Count)
        {
            return false;
        }

        // ÇOKLUK KORUNUR: aynı emtiadan iki satır (ör. iki farklı gramajda aynı maden) meşrudur ve
        // birini silmek override'dır. Küme (set) karşılaştırması bunu göremezdi.
        foreach (var pair in core)
        {
            if (!channel.TryGetValue(pair.Key, out var count) || count != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Devralınabilir satırların DONUK imza fotoğrafı (yan maliyetler elenmiş) — entity mutasyona
    /// uğrasa da değişmez; kayıt-öncesi durum bununla saklanır.</summary>
    public static IReadOnlyList<RecipeCommoditySignature> SnapshotOf(IEnumerable<IRecipeCommodityLine> lines)
    {
        return InheritableLines(lines).Select(SignatureOf).ToList();
    }

    /// <summary>Devralınabilir satırlar — yan maliyetler ELENİR (gerekçe sınıf özetinde).</summary>
    public static IEnumerable<IRecipeCommodityLine> InheritableLines(IEnumerable<IRecipeCommodityLine> lines)
    {
        return lines.Where(line => line.SideCostKind is null);
    }

    /// <summary>Satırın emtia imzası (yalnız <see cref="SnapshotOf"/> ve karşılaştırma içinde kullanılır).</summary>
    private static RecipeCommoditySignature SignatureOf(IRecipeCommodityLine line)
    {
        return new RecipeCommoditySignature(
            line.CommodityProcessType,
            line.CommodityId,
            line.CommodityVariantId,
            line.Quantity,
            line.Factor,
            line.ValuationUnitId);
    }

    /// <summary>İmza ÇOKLUĞU (imza → adet).</summary>
    private static Dictionary<RecipeCommoditySignature, int> CountBySignature(IEnumerable<RecipeCommoditySignature> signatures)
    {
        var counts = new Dictionary<RecipeCommoditySignature, int>();
        foreach (var signature in signatures)
        {
            counts.TryGetValue(signature, out var current);
            counts[signature] = current + 1;
        }

        return counts;
    }
}
