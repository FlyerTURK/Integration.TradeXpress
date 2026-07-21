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
