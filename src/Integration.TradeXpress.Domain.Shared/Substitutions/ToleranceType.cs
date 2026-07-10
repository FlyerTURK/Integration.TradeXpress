namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil tolerans türü — kombinasyon toplamının talepten sapma sınırının nasıl yorumlanacağı.
/// ToleranceValue=0 → mutlak eşitlik (exact-match). Tolerans &gt; 0 olan grupla üretilen varyant açıklamasına
/// ticari tolerans notu OTOMATİK eklenir (konsept: ticari bildirim zorunluluğu — M3 push tarafında).</summary>
public enum ToleranceType
{
    /// <summary>Mutlak gram toleransı: |toplam − talep| ≤ ToleranceValue.</summary>
    Gram = 1,

    /// <summary>Binde (göreceli) tolerans: |toplam − talep| ≤ talep × ToleranceValue / 1000.</summary>
    PerMille = 2,
}
