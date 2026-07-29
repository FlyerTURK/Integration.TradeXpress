using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11'in listeleme API'sinde VERMEDİĞİ 9 mega üst kategori (satıcı panelinden keşif, 2026-07-06 research) + 79
/// top-level'ın hangi meganın altında olduğu (kullanıcı onaylı eşleme, 2026-07-07). API <c>/cdn/categories</c> 79'u
/// <c>parentId=null</c> döndürür; bu SENTETİK katman breadcrumb'a üst seviye kazandırır (ör.
/// <c>Mücevher &amp; Saat &gt; Yatırımlık Altın &amp; Gümüş &gt; Külçe Altın</c>). Ürün YALNIZ yaprağa listelenir →
/// mega id hiçbir zaman N11'e gönderilmez (sunum/gezinme katmanı).
/// </summary>
public static class N11MegaCategories
{
    /// <summary>9 mega — sentetik ExternalId (N11'in numerik id'leriyle çakışmaz) + görünen ad. Yeni kökler
    /// (ParentExternalId=null, IsLeaf=false).</summary>
    public static readonly IReadOnlyList<(string ExternalId, string Name)> Megas = new[]
    {
        ("MEGA-MODA", "Moda"),
        ("MEGA-ELEKTRONIK", "Elektronik"),
        ("MEGA-EV", "Ev & Yaşam"),
        ("MEGA-BEBEK", "Anne & Bebek"),
        ("MEGA-KOZMETIK", "Kozmetik & Kişisel Bakım"),
        ("MEGA-MUCEVHER", "Mücevher & Saat"),
        ("MEGA-SPOR", "Spor & Outdoor"),
        ("MEGA-KITAP", "Kitap, Müzik, Film, Oyun"),
        ("MEGA-OTOMOTIV", "Otomotiv & Motosiklet"),
    };

    /// <summary>79 top-level API kategori id'si → mega id (kullanıcı onaylı, 2026-07-07). Eşlenmemiş bir top kalırsa
    /// (N11 yeni top eklerse) o kategori kök olarak kalır — grouper log'lar, sessiz düşmez.</summary>
    public static readonly IReadOnlyDictionary<string, string> TopToMega = new Dictionary<string, string>
    {
        // ── Moda ──
        ["1000145"] = "MEGA-MODA",       // Hamile Giyim
        ["1001770"] = "MEGA-MODA",       // Ayakkabı & Çanta
        ["1001873"] = "MEGA-MODA",       // Erkek Giyim & Aksesuar
        ["1001935"] = "MEGA-MODA",       // Kadın Giyim & Aksesuar
        ["1002032"] = "MEGA-MODA",       // Çocuk Giyim & Aksesuar
        ["1002663"] = "MEGA-MODA",       // Aksesuar
        ["1002717"] = "MEGA-MODA",       // Bijuteri Takılar
        ["1191215"] = "MEGA-MODA",       // Takı Aksesuarları
        ["1218200"] = "MEGA-MODA",       // Güneş Gözlüğü

        // ── Elektronik ──
        ["1000210"] = "MEGA-ELEKTRONIK", // Bilgisayar
        ["1000427"] = "MEGA-ELEKTRONIK", // Fotoğraf & Kamera
        ["1000472"] = "MEGA-ELEKTRONIK", // Telefon & Aksesuarları
        ["1000514"] = "MEGA-ELEKTRONIK", // Televizyon & Ses Sistemleri
        ["1088100"] = "MEGA-ELEKTRONIK", // Dijital Kodlar & Ürünler
        ["1181258"] = "MEGA-ELEKTRONIK", // Video Oyun & Konsol

        // ── Ev & Yaşam ──
        ["1000001"] = "MEGA-EV",         // Banyo & Tuvalet
        ["1000178"] = "MEGA-EV",         // Beyaz Eşya
        ["1000373"] = "MEGA-EV",         // Elektrikli Ev Aletleri
        ["1000578"] = "MEGA-EV",         // Banyo & Ev Gereçleri
        ["1000604"] = "MEGA-EV",         // Dekorasyon & Aydınlatma
        ["1000702"] = "MEGA-EV",         // Ev Tekstili
        ["1000783"] = "MEGA-EV",         // Evcil Hayvan Ürünleri
        ["1001006"] = "MEGA-EV",         // Kırtasiye & Ofis
        ["1001155"] = "MEGA-EV",         // Mobilya
        ["1001262"] = "MEGA-EV",         // Mutfak Gereçleri
        ["1001374"] = "MEGA-EV",         // Süpermarket
        ["1001525"] = "MEGA-EV",         // Yapı Market & Bahçe
        ["1002465"] = "MEGA-EV",         // El İşi Ürünleri
        ["1002605"] = "MEGA-EV",         // Sağlık & Medikal Ürünler
        ["1003526"] = "MEGA-EV",         // 2.El Antika & Koleksiyon
        ["1193201"] = "MEGA-EV",         // Düğün, Davet, Organizasyon
        ["1194200"] = "MEGA-EV",         // Yaşam ve Etkinlik

        // ── Anne & Bebek ──
        ["1000008"] = "MEGA-BEBEK",      // Bebek Arabaları
        ["1000016"] = "MEGA-BEBEK",      // Bebek Bakım & Sağlık
        ["1000035"] = "MEGA-BEBEK",      // Bebek Bezi & Islak Mendil
        ["1000039"] = "MEGA-BEBEK",      // Bebek Giyim
        ["1000080"] = "MEGA-BEBEK",      // Bebek Güvenlik
        ["1000087"] = "MEGA-BEBEK",      // Bebek Odası & Park Yatak
        ["1000115"] = "MEGA-BEBEK",      // Bebek Oyuncakları
        ["1000121"] = "MEGA-BEBEK",      // Beslenme & Mama Sandalyesi
        ["1000126"] = "MEGA-BEBEK",      // Biberon ve Aksesuarları
        ["1000136"] = "MEGA-BEBEK",      // Emzirme Ürünleri
        ["1000170"] = "MEGA-BEBEK",      // Oto Koltuğu & Ana Kucağı
        ["1000175"] = "MEGA-BEBEK",      // Yürüteç & Yürüme Yardımcıları
        ["1002411"] = "MEGA-BEBEK",      // Çocuk Oyuncakları & Parti

        // ── Kozmetik & Kişisel Bakım ──
        ["1002507"] = "MEGA-KOZMETIK",   // Erkek Bakım Ürünleri
        ["1002520"] = "MEGA-KOZMETIK",   // Güzellik Salonu & Kuaför Ürünleri
        ["1002543"] = "MEGA-KOZMETIK",   // Kadın Bakım Ürünleri
        ["1002553"] = "MEGA-KOZMETIK",   // Makyaj
        ["1002579"] = "MEGA-KOZMETIK",   // Parfüm & Deodorant
        ["1002583"] = "MEGA-KOZMETIK",   // Saç Bakım & Şekillendirme
        ["1002639"] = "MEGA-KOZMETIK",   // Cilt Bakımı
        ["1094100"] = "MEGA-KOZMETIK",   // Cinsel Ürünler
        ["1259200"] = "MEGA-KOZMETIK",   // Ağız & Diş Bakımı

        // ── Mücevher & Saat ──
        ["1002680"] = "MEGA-MUCEVHER",   // Yatırımlık Altın & Gümüş
        ["1002690"] = "MEGA-MUCEVHER",   // Altın Takılar
        ["1002742"] = "MEGA-MUCEVHER",   // Gümüş Takılar
        ["1002767"] = "MEGA-MUCEVHER",   // Pırlanta Takılar
        ["1002809"] = "MEGA-MUCEVHER",   // Saat
        ["1002816"] = "MEGA-MUCEVHER",   // Çelik Takılar

        // ── Spor & Outdoor ──
        ["1003129"] = "MEGA-SPOR",       // Avcılık & Balıkçılık
        ["1003142"] = "MEGA-SPOR",       // Bireysel & Takım Sporları
        ["1003192"] = "MEGA-SPOR",       // Bisiklet & Scooter
        ["1003221"] = "MEGA-SPOR",       // Fitness & Kondisyon
        ["1003263"] = "MEGA-SPOR",       // Kış Sporları
        ["1003289"] = "MEGA-SPOR",       // Outdoor & Kamp
        ["1003335"] = "MEGA-SPOR",       // Spor Giyim & Ayakkabı
        ["1003363"] = "MEGA-SPOR",       // Tekne & Yat Malzemeleri
        ["1018101"] = "MEGA-SPOR",       // Su Sporları

        // ── Kitap, Müzik, Film, Oyun ──
        ["1002084"] = "MEGA-KITAP",      // Film
        ["1002113"] = "MEGA-KITAP",      // Kitap
        ["1002234"] = "MEGA-KITAP",      // Müzik
        ["1002349"] = "MEGA-KITAP",      // Yetişkin Hobi & Oyun

        // ── Otomotiv & Motosiklet ──
        ["1002841"] = "MEGA-OTOMOTIV",   // Aksesuar & Tuning
        ["1002975"] = "MEGA-OTOMOTIV",   // Lastik & Jant
        ["1002993"] = "MEGA-OTOMOTIV",   // Motosiklet
        ["1003041"] = "MEGA-OTOMOTIV",   // Ses Sistemleri & Navigasyon
        ["1003061"] = "MEGA-OTOMOTIV",   // Yedek Parça
        ["1126100"] = "MEGA-OTOMOTIV",   // Traktör
    };

    /// <summary>Sentetik mega id'lerin ortak öneki — YALNIZ okunabilirlik/belgeleme için. Ayırt etme
    /// <see cref="IsMega"/> ile yapılır (önek eşleşmesi değil, ÜYELİK).</summary>
    public const string SyntheticIdPrefix = "MEGA-";

    /// <summary>Verilen dış kimlik SENTETİK mega katmana mı ait? "N11'den gelen gerçek kategori" ile "bizim
    /// eklediğimiz üst katman" ayrımının TEK doğru kaynağı — kategori sayımı (mega hariç) ve senkron damgası
    /// bu yüklemi kullanır.
    ///
    /// <para>Diğer iki sezgi ÇÜRÜK, kullanılmamalı: <c>ParentExternalId == null</c> yanlıştır (79 GERÇEK
    /// top-level de N11'den kök olarak gelir ve grouper onları bağlayana kadar köksüzdür; <see cref="TopToMega"/>'da
    /// karşılığı olmayan yeni bir N11 kökü ise kalıcı olarak köksüz kalır), <c>LastModifiedExternal == null</c>
    /// ise tamamen geçersizdir (REST yolunda her düğüme null yazılıyor).</para></summary>
    public static bool IsMega(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return false;
        }

        return Megas.Any(m => string.Equals(m.ExternalId, externalId, StringComparison.Ordinal));
    }
}
