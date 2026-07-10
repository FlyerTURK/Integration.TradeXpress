namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Planlayıcı girdisi — SAF veri (DB'siz/DI'sız; besleme M4 köprü adaptöründen — hesap sonucu DTO'sundan — gelir).
/// Yalnız BAŞARILI kombinasyonlar beklenir (Rank'lı); sıralama/Top-N seçimi planlayıcıda yapılır.
/// </summary>
/// <param name="ToleranceType">Grup tolerans türü (ticari bildirim metni üretimi için).</param>
/// <param name="ToleranceValue">Grup tolerans değeri — 0 = bildirim yok.</param>
/// <param name="TopN">Oluşturulacak varyant (kombinasyon) sayısı üst sınırı; ≤0 → tüm başarılılar.</param>
/// <param name="SuccessfulCombinations">Başarılı kombinasyonlar (Rank zorunlu; sırası önemsiz — planlayıcı Rank'a göre sıralar).</param>
public sealed record SubstitutionStockItemPlanInput(
    ToleranceType ToleranceType,
    decimal ToleranceValue,
    int TopN,
    IReadOnlyList<SubstitutionPlanCombination> SuccessfulCombinations);

/// <summary>Planlayıcıya giren tek başarılı kombinasyon — solver skoru (Rank/paket) + bileşim satırları.</summary>
/// <param name="Rank">Skor sırası (1 = ana varyant adayı).</param>
/// <param name="PackageCount">Paket sayısı — kombinasyonun stoktan kaç KEZ tekrarlanabileceği (→ OverrideStock).</param>
/// <param name="Lines">Bileşim satırları (tüketim önceliği sırasıyla; yalnız adet &gt; 0).</param>
public sealed record SubstitutionPlanCombination(
    int Rank,
    int PackageCount,
    IReadOnlyList<SubstitutionPlanCombinationLine> Lines);

/// <summary>Kombinasyonun tek emtia satırı — görünen ad çakışan gramaj metinlerinin ayrıştırıcısıdır.</summary>
public sealed record SubstitutionPlanCombinationLine(
    Guid MetalId,
    string MetalName,
    decimal PieceWeight,
    int Count);

/// <summary>
/// Köprü PLANI — kanal-agnostik nötr çıktı: N11/Trendyol adaptörleri bu planı kendi graf tiplerine uygular
/// (özellik "Kombinasyon" + değerler + StockItem reçeteleri + paket stoğu). Fiyat TAŞIMAZ — fiyat mevcut
/// maliyet zincirinden (reçete → NetCost → marj → türetilmiş) doğar (bağlayıcı karar 1).
/// </summary>
/// <param name="ToleranceNotice">Ticari tolerans bildirimi (tolerans &gt; 0 ise; push açıklamasına iliştirilecek metin) — yoksa null.</param>
/// <param name="Items">Kombinasyon plan kayıtları, Rank artan sırayla (ilk kayıt = ana varyant).</param>
public sealed record SubstitutionStockItemPlan(
    string? ToleranceNotice,
    IReadOnlyList<SubstitutionStockItemPlanItem> Items);

/// <summary>
/// Tek kombinasyonun nötr plan kaydı — kanal adaptörünün StockItem kurulumu için gereken her şey.
/// </summary>
/// <param name="Rank">Skor sırası (1 = ana varyant).</param>
/// <param name="IsPrimary">true = ana varyant (en iyi skor; kanal deseninde ilk sıra/DisplayOrder 0 temsili).</param>
/// <param name="ValueText">Kanal özellik DEĞERİ görünen metni (ör. "1×10gr + 2×1gr") — plan içinde benzersiz.</param>
/// <param name="PlanKey">Nötr imza bileşenleri — "{MetalId}x{Count}|..." (MetalId artan sıralı, sıra-bağımsız
/// deterministik anahtar). Kanal imzası (CombinationSignature) DEĞİL — kanal kendi kuralıyla üretir.</param>
/// <param name="PackageCount">Paket sayısı → kanal StockItem OverrideStock.</param>
/// <param name="ImageUrl">Görsel-atama noktası — AI kombinasyon görseli projesi buraya bağlanacak
/// (konsept notu); ŞİMDİLİK DAİMA null.</param>
/// <param name="RecipeLines">Kombinasyon bileşenleri → kanal StockItem REÇETESİ (metal satırları).</param>
public sealed record SubstitutionStockItemPlanItem(
    int Rank,
    bool IsPrimary,
    string ValueText,
    string PlanKey,
    int PackageCount,
    string? ImageUrl,
    IReadOnlyList<SubstitutionPlanRecipeLine> RecipeLines);

/// <summary>Plan reçete satırı — maden + adet; Amount = Count × PieceWeight (adet→gram, katalog kuralı).</summary>
public sealed record SubstitutionPlanRecipeLine(
    Guid MetalId,
    int Count,
    decimal PieceWeight,
    decimal Amount);
