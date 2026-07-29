namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Taşıyıcının çok parçalı gönderiyi nasıl ücretlendirdiği. N11 resmi metni: "Fiyatlar parça başı değil,
/// kümülatif fiyatlardır… PTT Kargo firmasında hesaplama parça başı olarak yapılmaktadır."
/// <para>Örnek (N11, 2×5 desi): <see cref="Cumulative"/> → 10 desi satırı okunur (170,09);
/// <see cref="PerPiece"/> → 5 desi satırı parça sayısıyla çarpılır. Fark tek gönderide bile büyük olduğundan
/// taşıyıcı başına saklanır, hesapta varsayılmaz.</para>
/// </summary>
public enum ShipmentChargeBasis : byte
{
    /// <summary>Parçaların desisi TOPLANIR, tarifeden tek satır okunur (N11'de PTT dışındaki tüm taşıyıcılar).</summary>
    Cumulative = 1,

    /// <summary>Her parça AYRI ücretlendirilir (N11'de yalnız PTT Kargo).</summary>
    PerPiece = 2,
}
