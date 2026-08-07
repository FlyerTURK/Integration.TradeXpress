using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol KÖK kategorilerinin başlangıç komisyon oranları (%) — 2026-08-06 Hakan kararı:
/// <i>"Kategori ağacının en belirgin parentlerine bu genel oranları geç. Child kategoriler bu parentlerden inherit
/// yararlansın. Sonrasında satış kanalına özel komisyonları belirleriz."</i>
///
/// <para><b>Neden yalnız 16 kök:</b> Trendyol ağacı binlerce yaprak taşır; oran ise yayınlarda ürün GRUBU düzeyinde
/// veriliyor. Kökten aşağı kalıtım (<c>TrendyolCommissionResolver</c>) sayesinde 16 satır tüm ağacı kapsar. Daha
/// derin bir düğüme oran girilirse o dal kökü EZER — kalıtım "en yakın dolu ata kazanır" kuralıyla çalışır.</para>
///
/// <para><b>Oranların kaynağı ÜÇÜNCÜ TARAF, Trendyol API'si DEĞİL:</b> Trendyol'un kategori ucu komisyon
/// döndürmüyor (yalnız <c>id/name/parentId/isLeaf</c>). Buradaki sayılar iki yayınlanmış tablodan
/// (ideasoft 06.02.2026 · Paraşüt) türetilmiş ORTALAMALARDIR; kaynaklar kategori başına ARALIK veriyor ve
/// birbiriyle tam örtüşmüyor. Gerçek oran satıcının sözleşmesine, satıcı seviyesine (1–5), kadın girişimci
/// programına ve kampanya dönemine bağlıdır; tek otorite Satıcı Paneli → <i>Anlaşma Bilgileri</i> ekranıdır.</para>
///
/// <para><b>Yalnız BOŞ oranı doldurur</b> (<c>TrendyolCategoryAppService.SeedRootCommissionRatesAsync</c>): bir kez
/// yazıldıktan sonra kullanıcının girdiği değeri bir daha EZMEZ. Aksi halde her sync gerçek sözleşme oranını bu
/// tahminlere geri çevirirdi.</para>
///
/// <para>⚠ Oranın TABANI (KDV dahil mi hariç mi) hâlâ modellenmedi — kaynaklar çelişiyor, fark ~4 puan ve doğrudan
/// net kâra giriyor. Bkz. <c>TrendyolProducts.TrendyolCommissionDefaults</c>.</para>
/// </summary>
public static class TrendyolCommissionSeedRates
{
    /// <summary>Kök kategori Trendyol id'si → oran (%). Anahtar ExternalId'dir (AD DEĞİL): ad yazımı
    /// ("&amp;" boşlukları, büyük harf) sync'ten sync'e değişebilir, id sabittir. Haritada olmayan kök oransız kalır
    /// ve o daldaki ürünler <c>PlaceholderRate</c>'e düşer — sessiz sıfır değil.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> ByRootExternalId =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["368"]  = 21.75m,   // Aksesuar          — atkı/şal %21,36 · kemer/şapka %22,37 · saat %21,36 · takı-bijuteri %22,37 · çanta %21,36
            ["2862"] = 16.50m,   // Anne & Bebek & Çocuk — tablodaki tüm bebek satırları %16,50
            ["403"]  = 23.39m,   // Ayakkabı          — tabloda tek satır, aynen
            ["5558"] = 16.83m,   // Bahçe & Elektrikli El Aletleri — bahçe dek. %17,50 · havuz %17,50 · el aletleri %15,50
            ["5559"] = 17.50m,   // Banyo Yapı & Hırdavat — doğrudan satır YOK; yapı/hırdavat bandı (bahçe ile aynı kuşak)
            ["3981"] = 9.00m,    // Ek Hizmetler      — online eğitim %10,17 · dijital hediye kartı %5,00 · yazılım %12,00
            ["1071"] = 12.00m,   // Elektronik        — ⚠ EN GENİŞ YELPAZE: telefon %7 · TV/konsol %8 · beyaz eşya-klima %11 · bilgisayar yedek parça %15,5 · tablet aksesuar %22 · telefon yedek parça %27. Ciro büyük kalemlerde (telefon/TV/beyaz eşya) toplandığı için ortalama aşağı çekildi; alt dallara oran girilerek düzeltilmeli.
            ["758"]  = 20.50m,   // Ev & Mobilya      — aydınlatma %21,36 · ev tekstili %20,34 · mutfak gereçleri %19,32 · mobilya/züccaciye %20–22
            ["522"]  = 21.36m,   // Giyim             — üst/alt/iç/dış/spor giyim tek satırda %21,36
            ["685"]  = 19.50m,   // Hobi & Eğlence    — oyuncak %20–22 · parti-yılbaşı %20,34 · sanatsal malzeme %16,78
            ["1216"] = 15.00m,   // Kırtasiye & Ofis  — kitap+kırtasiye %12–15 · boya/sanatsal kağıt-kalem %16,78
            ["687"]  = 13.50m,   // Kitap             — kitap ve kırtasiye %12–15 ortası
            ["1070"] = 17.50m,   // Kozmetik & Kişisel Bakım — cilt/ağız bakım %16,78 · epilatör-saç %17,50 · hasta bezi %17,29 · kozmetik %17–20 · parfüm %17–19
            ["790"]  = 16.50m,   // Otomobil & Motosiklet — oto bakım/temizlik %16,50
            ["3186"] = 17.00m,   // Spor & Outdoor    — %14–20 aralığının ortası
            ["1219"] = 14.00m,   // Süpermarket       — atıştırmalık/kuru gıda/süt %15,25 · evcil mama %15,25 · gıda %10–15
        };
}
