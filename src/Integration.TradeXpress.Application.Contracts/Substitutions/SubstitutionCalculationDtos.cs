using System;
using System.Collections.Generic;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil hesaplama girdisi. Tolerans varsayılanı GRUP ayarıdır (konsept: tolerans politikası grup üzerinde
/// tanımlıdır); Dilim-3 istisnası: ÜRÜN-düzeyi Muadil modu kendi kalıcı konfigürasyonunu (Product.Substitution*)
/// opsiyonel override alanlarıyla geçirir — boş bırakan çağıran (hesaplama sayfası) statüko davranışında kalır.
/// Şube/kasa filtresi stok raporundaki hiyerarşik kapsamla aynı davranır (null = alt kırılımları topla;
/// şirket daima working company).
/// </summary>
public class SubstitutionCalculationInput
{
    public Guid SubstitutionGroupId { get; set; }

    /// <summary>İstenilen miktar (gram) — pozitif olmalı (fail-fast).</summary>
    public decimal TargetQuantity { get; set; }

    /// <summary>Varyant adayı üst sınırı — Rank ≤ TopN başarılılar <see cref="SubstitutionTrialDto.IsTopCandidate"/>
    /// işaretlenir. ≤0 gönderilirse varsayılan (50) uygulanır.</summary>
    public int TopN { get; set; } = SubstitutionCalculationConsts.DefaultTopN;

    /// <summary>Stok kapsamı: şube (null = şirketin tümü) — GetStockAsync filtresiyle birebir.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Stok kapsamı: kasa (null = şubenin tümü) — GetStockAsync filtresiyle birebir.</summary>
    public Guid? VaultId { get; set; }

    /// <summary>Ürün-düzeyi varyant kapsamı (<c>Product.SubstitutionOverrideVariantIds</c>) — düz liste tüm grup
    /// madenlerini kapsar; maden başına etkin küme = listenin o madenin katalog varyantlarıyla KESİŞİMİ.
    /// <para><b>null = ÜRÜN BAĞLAMI YOK</b> (grup hesaplama sayfası): kapsam grup kalemlerinden çözülür.
    /// <b>Liste verildiyse ürünün kendi kapsamıdır ve BOŞ olması da bir cevaptır</b> ("bu madeni istemiyorum") —
    /// ürün gruba bağlanırken kapsam ürüne kopyalandığından grup artık belirleyici değildir (2026-07-27 kararı).
    /// Bu ayrım null-üzerinden yürür: boş listeyle null'ı eşitlemek kullanıcının kaldırma kararını yutar.</para></summary>
    public List<Guid>? OverrideVariantIds { get; set; }

    /// <summary>Tolerans türü override'ı (Dilim-3; ürün konfigürasyonu) — değerle ÇİFT dolar; boş = grup ayarı.</summary>
    public ToleranceType? ToleranceTypeOverride { get; set; }

    /// <summary>Tolerans değeri override'ı — türle ÇİFT dolar; boş = grup ayarı; negatif geçersiz (fail-fast).</summary>
    public decimal? ToleranceValueOverride { get; set; }
}

public static class SubstitutionCalculationConsts
{
    public const int DefaultTopN = 50;
}

/// <summary>
/// Muadil hesaplama sonucu — kullanıcı tablosu (TÜM denemeler: başarılı + başarısız, elenme nedeni
/// kolonuyla) + ön-filtre raporu + özet. Varyanta yalnız <see cref="SubstitutionTrialDto.IsTopCandidate"/>
/// işaretli (Rank ≤ TopN) başarılılar gider.
/// </summary>
public class SubstitutionCalculationResultDto
{
    public Guid GroupId { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    public decimal TargetQuantity { get; set; }
    public ToleranceType ToleranceType { get; set; }
    public decimal ToleranceValue { get; set; }
    /// <summary>Efektif tolerans (gram) — PerMille'de talep × değer / 1000 çözülmüş hâli.</summary>
    public decimal EffectiveTolerance { get; set; }
    public int TopN { get; set; }

    /// <summary>Toplam deneme sayısı (başarılı + başarısız).</summary>
    public int TrialCount { get; set; }
    /// <summary>Başarılı (tolerans içi) kombinasyon sayısı.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Ön-filtre sonrası toplam kullanılabilir ağırlık (Σ adet × parça gramı).</summary>
    public decimal TotalAvailableWeight { get; set; }
    /// <summary>Toplam kapasite talebin (tolerans alt bandının) altında — numaralandırma HİÇ başlatılmadı
    /// (2026-07-10 kullanıcı kararı); true iken Trials boştur, UI bilgi bandı gösterir.</summary>
    public bool InsufficientStock { get; set; }

    /// <summary>Maliyet para birimi (ülke/yerel birim kodu). Kur çözülemeyen hesap HİÇ koşmaz
    /// (fail-fast <c>RatesMissing</c>, 2026-07-10 kullanıcı kararı) → başarılı sonuçta DAİMA dolu.</summary>
    public string CostCurrencyCode { get; set; } = string.Empty;

    /// <summary>Tüm denemeler, numaralandırma sırasıyla (kullanıcının onayladığı tablo formatı).</summary>
    public List<SubstitutionTrialDto> Trials { get; set; } = new();

    /// <summary>Ön-filtrede elenen emtialar + teknik neden.</summary>
    public List<SubstitutionFilteredOutDto> FilteredOut { get; set; } = new();
}

/// <summary>Tek deneme (kombinasyon) satırı — tablo kolonlarıyla birebir.</summary>
public class SubstitutionTrialDto
{
    /// <summary>Kombinasyon bileşimi — metal-adet çiftleri (yalnız adet &gt; 0; tüketim önceliği sırasıyla).</summary>
    public List<SubstitutionTrialLineDto> Lines { get; set; } = new();

    /// <summary>Toplam gram.</summary>
    public decimal TotalWeight { get; set; }
    /// <summary>Sapma = toplam − talep (işaretli; |sapma| ≤ efektif tolerans ⇔ başarılı).</summary>
    public decimal Deviation { get; set; }
    /// <summary>Toplam maliyet (skor 1. ölçüt — küçük iyi; <see cref="SubstitutionCalculationResultDto.CostCurrencyCode"/> cinsinden).</summary>
    public decimal TotalCost { get; set; }
    /// <summary>Toplam parça adedi (skor 2. ölçüt — küçük iyi).</summary>
    public int PieceCount { get; set; }
    /// <summary>Paket sayısı — kombinasyonun stoktan kaç KEZ tekrarlanabileceği (skor 3. ölçüt — büyük iyi).</summary>
    public int PackageCount { get; set; }

    /// <summary>Bu kombinasyonun DETERMİNİSTİK varyant kodu ("G5.0GR995X1+G1.0GR995X3") — kayıt anında
    /// üretilenle AYNI (tek kaynak: <c>SubstitutionCombinationCodeBuilder</c>). Ürün formu kaydetmeden
    /// varyant önizlemesini bu kodla eşler; kaydedince aynı kod bulunduğu için varyant kaydı (ve kanal SKU
    /// bağı) yerinde kalır.</summary>
    public string CombinationCode { get; set; } = string.Empty;

    /// <summary>Bileşimin okunabilir özeti ("1×5gr + 4×1gr") — varyant tablosundaki "Kombinasyon" kolonunu
    /// besler. Kodla aynı sırada (büyük parçadan küçüğe) ve aynı üreticiden gelir.</summary>
    public string CombinationSummary { get; set; } = string.Empty;

    /// <summary>
    /// Bu kombinasyonun REÇETE satırları — ürün formu kaydetmeden varyantın içini gösterebilsin diye.
    /// Kayıt anında sunucunun kuracağı satırlarla aynı matematik (tek üretici).
    /// <para><b>Yalnız VARYANTA DÖNÜŞECEK kombinasyonlarda dolu</b> (en iyi sıradakiler): yüzlerce denemenin
    /// hepsi için satır üretmek yanıtı gereksiz şişirirdi — diğerlerinde boş kalır.</para>
    /// </summary>
    public List<ProductRecipeLineGraphDto> RecipeLines { get; set; } = new();

    public bool Success { get; set; }
    /// <summary>Teknik başarısızlık nedeni ("StockExhausted" / "Remainder:0.6") — UI lokalize eder; başarılıda null.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Skor sırası (1 = ana varyant adayı); yalnız başarılıda.</summary>
    public int? Rank { get; set; }
    /// <summary>true = varyant adayı (Success + Rank ≤ TopN).</summary>
    public bool IsTopCandidate { get; set; }
}

/// <summary>Kombinasyonun tek emtia satırı — maden + kullanılan adet (+ parça verisi, tablo gösterimi için).</summary>
public class SubstitutionTrialLineDto
{
    public Guid MetalId { get; set; }
    public string MetalCode { get; set; } = string.Empty;
    /// <summary>Adayın metal varyantı (Dilim-2 varyant boyutu) — null = katalog varyantı olmayan maden (legacy).</summary>
    public Guid? VariantId { get; set; }
    /// <summary>Varyant kodu (tablo gösterimi); <see cref="VariantId"/> null ise null.</summary>
    public string? VariantCode { get; set; }
    /// <summary>Kullanılan adet.</summary>
    public int Count { get; set; }
    /// <summary>Tek parça standart ağırlığı (Metal.StableQuantity, gram).</summary>
    public decimal PieceWeight { get; set; }
    /// <summary>Parça maliyeti (satış kuru değerlemesi + işçilik) — kur çözülemeyen hesap koşmadığından
    /// (fail-fast RatesMissing) DAİMA gerçek maliyettir.</summary>
    public decimal UnitCost { get; set; }
}

/// <summary>Ön-filtrede elenen emtia ADAYI — teknik neden ("PieceWeightExceedsTarget" | "NoStock"); UI lokalize
/// eder. Eleme varyant boyutunda aday başınadır — varyant alanları hangi varyantın elendiğini gösterir.</summary>
public class SubstitutionFilteredOutDto
{
    public Guid MetalId { get; set; }
    public string MetalCode { get; set; } = string.Empty;
    /// <summary>Elenen adayın metal varyantı — null = katalog varyantı olmayan maden (legacy).</summary>
    public Guid? VariantId { get; set; }
    /// <summary>Varyant kodu (gösterim); <see cref="VariantId"/> null ise null.</summary>
    public string? VariantCode { get; set; }
    public string Reason { get; set; } = string.Empty;
}
