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
/// Solver emtiası — tek parçanın standart ağırlığı + kullanılabilir adet + parça maliyeti.
/// </summary>
/// <param name="Id">Emtia kimliği (ilk fazda MetalId).</param>
/// <param name="Code">Emtia kodu (rapor/log okunabilirliği).</param>
/// <param name="PieceWeight">Tek parça standart ağırlığı (Metal.StableQuantity) — pozitif olmalı.</param>
/// <param name="AvailableCount">KULLANILABİLİR adet (M3 besleme hesaplar — rezervasyon geldiğinde
/// yalnız besleme değişir, solver etkilenmez; konsept "rezervasyonlu stok" notu).</param>
/// <param name="UnitCost">Parça maliyeti (skor ölçütü; M3'te reçete motoru değerlemesinden gelir).</param>
public sealed record SubstitutionCommodity(
    Guid Id,
    string Code,
    decimal PieceWeight,
    int AvailableCount,
    decimal UnitCost);
