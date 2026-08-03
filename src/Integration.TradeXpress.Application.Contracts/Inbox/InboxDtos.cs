using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Inbox;

/// <summary>
/// Ortak gelen kutusu panosundaki TEK KART = bir kaynağın özeti. Biçim "ÖZET + DERİNLEMESİNE": kart dikkat
/// bekleyen sayıyı ve son birkaç öğeyi gösterir; kullanıcı karta/öğeye tıklayınca türün KENDİ tam ekranına
/// (<see cref="TargetUrl"/>) gider. Kart hiçbir zaman tam listenin yerini almaz.
///
/// <para>Kartı türün kendi sağlayıcısı (<see cref="IInboxSummaryProvider"/>) doldurur — pano sayfası hiçbir
/// türü tanımaz. Yarın yeni bir tür eklendiğinde pano DEĞİŞMEZ.</para>
/// </summary>
public class InboxCardDto
{
    /// <summary>Kartın kaynak kimliği — <see cref="InboxSourceKey"/> sabitlerinden biri. UI'nın kart-başına
    /// eşleme (ikon/etiket/tercih) yapabildiği kararlı anahtar.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Kart başlığı — LOKALİZE metin. Sağlayıcı kendi kaynağından (<c>IStringLocalizer</c>) çözer;
    /// pano sayfası çeviri anahtarı bilmez (yeni tür eklemek pano sözlüğünü büyütmesin).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Kart ikonunun CSS sınıfı — UI olduğu gibi <c>class</c> niteliğine basar. Ad-hoc sembol/emoji
    /// YASAK (RazorConventionTests): değer ikon sabiti kataloğundan gelir.</summary>
    public string IconCssClass { get; set; } = string.Empty;

    /// <summary>DİKKAT BEKLEYEN öğe sayısı — kartın rozetinde gösterilen sayı. "Bekleyen"in tanımı türe
    /// aittir (teyitte karar bekleyen, soruda cevaplanmamış); pano bu sayıyı yorumlamaz, yalnız gösterir.</summary>
    public int PendingCount { get; set; }

    /// <summary>Kaynaktaki toplam öğe sayısı (kapsam içinde) — bekleyen/toplam bağlamı için.</summary>
    public int TotalCount { get; set; }

    /// <summary>Türün TAM EKRAN rotası (ör. <c>/confirmations</c>). Rota sağlayıcıdan gelir: pano rota
    /// tablosu tutmaz, tür kendi adresini bilir.</summary>
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>Kartta gösterilen son öğeler — en fazla <see cref="InboxConsts.RecentItemCount"/> adet.
    /// Vitrindir, liste değildir.</summary>
    public List<InboxCardItemDto> RecentItems { get; set; } = new();

    public override string ToString()
    {
        return SourceKey;
    }
}

/// <summary>
/// Kart vitrinindeki tek satır. Kaynak-nötr kalması için alanlar KASITLI olarak jeneriktir: pano her türün
/// kendi alan adlarını (karşı kasa / müşteri / gönderen...) bilmek zorunda kalmasın diye çeviriyi sağlayıcı
/// yapar, pano yalnız iki metin + bir zaman + bir bayrak render eder.
/// </summary>
public class InboxCardItemDto
{
    /// <summary>Kaynak kayıttaki öğenin kimliği — tam ekrana derinlemesine gidiş (satır seçme/vurgulama) için.</summary>
    public Guid Id { get; set; }

    /// <summary>Birincil satır metni (ör. soru özeti, teyitte karşı taraf).</summary>
    public string PrimaryText { get; set; } = string.Empty;

    /// <summary>İkincil/bağlam metni (ör. ürün adı, süreç tipi). Yoksa null — UI satırı tek satıra düşürür.</summary>
    public string? SecondaryText { get; set; }

    /// <summary>Öğenin gerçekleşme zamanı — <b>UTC</b> saklanır ve UTC taşınır; kullanıcının yerel saatine
    /// çevirmek UI'nın merkezi dönüşümünün işidir (kayıt=UTC, görüntü=yerel kuralı).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Bu öğe hâlâ dikkat bekliyor mu — vitrinde vurgulamak için (kartın <see cref="InboxCardDto.PendingCount"/>
    /// rozetiyle aynı "bekleyen" tanımı).</summary>
    public bool IsPending { get; set; }

    public override string ToString()
    {
        return $"{PrimaryText} [{(IsPending ? "Pending" : "Done")}]";
    }
}
