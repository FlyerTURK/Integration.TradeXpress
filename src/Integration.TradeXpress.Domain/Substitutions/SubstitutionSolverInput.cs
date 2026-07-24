namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Solver girdisi — SAF veri (DB'siz/DI'sız; besleme M3'te MetalReport + grup çözümlemesinden gelir).
/// <b>LİSTE SIRASI = TÜKETİM ÖNCELİĞİ</b> (kullanıcı-kontrollü; üsttekiler önce doldurulur).
/// </summary>
/// <param name="RequestedAmount">İstenilen miktar (ör. 12.0 gram) — pozitif olmalı.</param>
/// <param name="ToleranceType">Tolerans türü (Gram mutlak | PerMille göreceli).</param>
/// <param name="ToleranceValue">Tolerans değeri — 0 = mutlak eşitlik; negatif olamaz.</param>
/// <param name="Commodities">Sıralı emtia listesi (sıra = tüketim önceliği).</param>
public sealed record SubstitutionSolverInput(
    decimal RequestedAmount,
    ToleranceType ToleranceType,
    decimal ToleranceValue,
    IReadOnlyList<SubstitutionCommodity> Commodities);

/// <summary>
/// Solver emtiası (ADAY) — tek parçanın standart ağırlığı + kullanılabilir adet + parça maliyeti.
/// <b>Varyant boyutu (Dilim-2):</b> aynı maden, etkin varyantlarına AYRI aday satırları olarak açılır
/// (aynı <see cref="PieceWeight"/> — madenin StableQuantity'si; farklı işçilik → farklı <see cref="UnitCost"/> +
/// farklı varyant stoğu). Solver matematiği aday-agnostiktir — varyant açılımı beslemede yapılır.
/// </summary>
/// <param name="Id">Emtia kimliği (ilk fazda MetalId; varyantlı adaylarda AYNI MetalId birden çok adayda görünür).</param>
/// <param name="Code">Emtia kodu (rapor/log okunabilirliği).</param>
/// <param name="PieceWeight">Tek parça standart ağırlığı (Metal.StableQuantity) — pozitif olmalı.</param>
/// <param name="AvailableCount">KULLANILABİLİR adet (M3 besleme hesaplar — rezervasyon geldiğinde
/// yalnız besleme değişir, solver etkilenmez; konsept "rezervasyonlu stok" notu).</param>
/// <param name="UnitCost">Parça maliyeti (skor ölçütü; M3'te reçete motoru değerlemesinden gelir).</param>
/// <param name="VariantId">Adayın metal varyantı — null = katalog varyantı olmayan maden (legacy aday).</param>
/// <param name="VariantCode">Varyant kodu (görünüm/rapor); <see cref="VariantId"/> null ise null.</param>
public sealed record SubstitutionCommodity(
    Guid Id,
    string Code,
    decimal PieceWeight,
    int AvailableCount,
    decimal UnitCost,
    Guid? VariantId = null,
    string? VariantCode = null);
