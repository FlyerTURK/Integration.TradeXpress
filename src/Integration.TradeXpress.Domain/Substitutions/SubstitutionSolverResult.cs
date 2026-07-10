namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Solver çıktısı — <b>TÜM denemeler</b> (başarılı + başarısız) numaralandırma sırasıyla
/// (kullanıcı kararı: hesap ekranında tümü gösterilir, elenme nedeni kolonuyla; varyanta yalnız
/// başarılılardan Top-N gider) + ön-filtrede elenen emtiaların ayrı raporu.
/// </summary>
/// <param name="All">Tüm denemeler, numaralandırma sırasıyla.</param>
/// <param name="FilteredOut">Ön-filtrede elenen emtialar + teknik neden.</param>
/// <param name="TotalAvailableWeight">Ön-filtre SONRASI kalan emtiaların toplam ağırlık kapasitesi
/// (Σ adet × parça ağırlığı).</param>
/// <param name="InsufficientStock">Toplam kapasite talebin (tolerans alt bandının) altında — numaralandırma
/// HİÇ BAŞLATILMADI (2026-07-10 kullanıcı kararı: "envanterdeki toplam rakam istenilen rakamın altındaysa
/// hiç başlamasına gerek yok"). true iken All boştur.</param>
public sealed record SubstitutionSolverResult(
    IReadOnlyList<SubstitutionCombination> All,
    IReadOnlyList<SubstitutionFilteredCommodity> FilteredOut,
    decimal TotalAvailableWeight = 0m,
    bool InsufficientStock = false);

/// <summary>
/// Tek deneme (kombinasyon) — başarılıysa skor alanları (Rank/PackageCount) doldurulur.
/// </summary>
/// <param name="Lines">Kullanılan emtia satırları (yalnız adet &gt; 0 olanlar; girdi sırasıyla).</param>
/// <param name="Total">Kombinasyon toplam miktarı.</param>
/// <param name="Success">|Total − talep| ≤ efektif tolerans mı.</param>
/// <param name="FailureReason">Lokalize DEĞİL — teknik kod ("Remainder:0.6" / "StockExhausted");
/// UI lokalize eder. Başarılıda null.</param>
/// <param name="TotalCost">Toplam maliyet = Σ adet × parça maliyeti (skor 1. ölçüt — küçük iyi).</param>
/// <param name="PieceCount">Toplam parça adedi (skor 2. ölçüt — küçük iyi).</param>
/// <param name="PackageCount">Paket sayısı = min(eldekiAdet ÷ kullanılanAdet) tam bölme — kombinasyonun
/// stoktan kaç KEZ tekrarlanabileceği (skor 3. ölçüt — büyük iyi). Yalnız başarılıda; başarısızda 0.</param>
/// <param name="Rank">Skor sırası (1 = ana varyant adayı). Yalnız başarılıda; başarısızda null.</param>
public sealed record SubstitutionCombination(
    IReadOnlyList<(Guid CommodityId, int Count)> Lines,
    decimal Total,
    bool Success,
    string? FailureReason,
    decimal TotalCost,
    int PieceCount,
    int PackageCount,
    int? Rank);

/// <summary>Ön-filtrede elenen emtia — teknik neden ("PieceWeightExceedsTarget" | "NoStock"); UI lokalize eder.</summary>
public sealed record SubstitutionFilteredCommodity(
    Guid CommodityId,
    string Code,
    string Reason);
