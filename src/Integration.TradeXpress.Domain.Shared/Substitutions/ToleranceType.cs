namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil tolerans türü — kombinasyon toplamının talepten sapma sınırının nasıl yorumlanacağı.
/// ToleranceValue=0 → mutlak eşitlik (exact-match). Tolerans &gt; 0 olan grupla üretilen varyant açıklamasına
/// ticari tolerans notu OTOMATİK eklenir (konsept: ticari bildirim zorunluluğu — M3 push tarafında).</summary>
public enum ToleranceType
{
    /// <summary>Mutlak MİKTAR toleransı — emtianın kendi ölçü biriminde (madende gram, Good'da kg/litre/düzine vb.;
    /// birim varsayımı YOK): |toplam − talep| ≤ ToleranceValue. Değer 0 → tam eşleşme (exact-match).</summary>
    Amount = 1,

    /// <summary>Binde (göreceli) tolerans: |toplam − talep| ≤ talep × ToleranceValue / 1000.</summary>
    PerMille = 2,
}
