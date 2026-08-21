using System.Collections.Generic;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>Etsy pazaryerinden içe aktarma SONUÇ RAPORU — sessiz geçilmez: toplam çekilen offering / listeleme
/// sayıları + üretilen şablon / kanal kaydı sayıları + atlanan satırlar (nedenli) + import-geneli uyarılar ekranda
/// gösterilir (Trendyol <c>TrendyolImportResultDto</c> ikizi; Etsy delta'sıyla — kategori-eşleşme kavramı YOK).</summary>
public class EtsyImportResultDto
{
    /// <summary>Etsy'den çekilen toplam OFFERING (inventory product) sayısı — tüm listelemelerin varyant kalemleri.</summary>
    public int TotalFetchedOfferings { get; set; }

    /// <summary>Çekilen uzak LİSTELEME (listing) sayısı.</summary>
    public int TotalRemoteListings { get; set; }

    /// <summary>Bu import'ta üretilen YENİ şablon Product sayısı.</summary>
    public int CreatedProducts { get; set; }

    /// <summary>Bu import'ta üretilen YENİ kanal ürünü (SalesChannelEtsyProduct) sayısı.</summary>
    public int CreatedChannelProducts { get; set; }

    /// <summary>Mevcut olup GÜNCELLENEN kanal ürünü sayısı (idempotent ikinci geçiş — EtsyListingId ile bulundu).</summary>
    public int UpdatedChannelProducts { get; set; }

    /// <summary>Bu import'ta üretilen toplam varyant (EntityVariant) sayısı — yeni şablonların offering setleri.</summary>
    public int CreatedVariants { get; set; }

    /// <summary>Uzak stoğu core (ERP) stoktan FARKLI olan offering sayısı (K12 stok politikası, 2026-07-23):
    /// sonraki importlar core StockQuantity'yi EZMEZ — remote değer kanal OverrideStock'una yazılır (kanal
    /// gerçeği) + satır-bazında LogWarning. 0 = tüm offering'ler core stokla uyumlu (override gürültüsü üretilmedi).</summary>
    public int StockDifferenceCount { get; set; }

    /// <summary>Görsel SINIRI (<c>ProductConsts.MaxImageCount</c>) dolduğu için hiç bağlanmayan pazaryeri görseli
    /// sayısı. Mevcut (kullanıcı) bağlarını korumak uğruna ödenen bedeldir ve SESSİZ GEÇİLMEZ: sıfırdan büyükse
    /// rapora ayrıca bir uyarı satırı düşer — aksi hâlde kullanıcı "import başarılı" görüp fotoğrafın neden
    /// gelmediğini hiçbir yerde bulamazdı (yalnız server-log'da kalırdı).</summary>
    public int SkippedImages { get; set; }

    /// <summary>Hangi ERP varyantına ait olduğu ÇÖZÜLEMEDİĞİ için indirilmeyen varyasyon fotoğrafı sayısı: Etsy
    /// fotoğrafı (eksen, değer) kimliğine bağlar; kimlik okunamadıysa ya da o değeri taşıyan offering bulunamadıysa
    /// görsel varyanta BAĞLANMAZ (metin eşleşmesine düşülmez — yanlış varyanta bağlanmış bir fotoğraf, hiç
    /// bağlanmamış olandan çok daha zor fark edilir). Sessiz geçilmez: sıfırdan büyükse rapora KENDİ uyarı satırı
    /// düşer ("sınıra takıldı" ile aynı sayaçta toplanmaz — farklı sorun, farklı çözüm).</summary>
    public int UnmappedVariationImages { get; set; }

    /// <summary>Atlanan satırlar + nedenleri (LOKALİZE) — offering'siz/geçersiz listeleme kalemleri.</summary>
    public List<EtsyImportIssueDto> SkippedRows { get; set; } = new();

    /// <summary>Import-geneli uyarılar (LOKALİZE) — kalem-bazlı olmayan riskli fallback'ler (ör. shop para birimi
    /// çözülemedi → fiyatlar para-birimsiz yazıldı). Sessiz geçilmez.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Import'ta atlanan tek satır — kimlik ipuçları + lokalize neden.</summary>
public class EtsyImportIssueDto
{
    /// <summary>Uzak listeleme kimliği (varsa; kimlik ipucu).</summary>
    public long? ListingId { get; set; }

    /// <summary>Listeleme başlığı (varsa; kimlik ipucu).</summary>
    public string? Title { get; set; }

    public string Reason { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{Title ?? ListingId?.ToString()}: {Reason}";
    }
}
