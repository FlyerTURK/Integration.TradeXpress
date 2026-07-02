namespace Integration.TradeXpress.Bullions;

/// <summary>
/// Takoz işleminde yan metalin (gümüş/platin/paladyum) bakiyeye nasıl yansıyacağı — ERPPRO <c>tMadenDurumu</c> karşılığı:
/// <list type="bullet">
///   <item><see cref="Deliver"/> (Madeni Ver): metalin kendi biriminde bakiye (<c>Miktar × Milyem</c>).</item>
///   <item><see cref="ConvertToGold"/> (Altına Çevir): kur üzerinden HAS bakiyesine (<c>değer × MetalKur / HasKur</c>).</item>
///   <item><see cref="DeductFromLabor"/> (İşçilikten Düş): kur üzerinden işçilik borcundan düşülür.</item>
///   <item><see cref="Keep"/> (Madeni Bırak): bakiyeye yansımaz — metal dükkânda kalır.</item>
/// </list>
/// </summary>
public enum MetalDisposition : byte
{
    /// <summary>Madeni Ver — metalin kendi biriminde bakiye.</summary>
    Deliver         = 0,

    /// <summary>Altına Çevir — kur üzerinden HAS bakiyesine.</summary>
    ConvertToGold   = 1,

    /// <summary>İşçilikten Düş — kur üzerinden işçilik borcundan düşülür.</summary>
    DeductFromLabor = 2,

    /// <summary>Madeni Bırak — bakiyeye yansımaz.</summary>
    Keep            = 3,
}
