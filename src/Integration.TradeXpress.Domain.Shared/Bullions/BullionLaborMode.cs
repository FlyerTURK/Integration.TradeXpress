namespace Integration.TradeXpress.Bullions;

/// <summary>
/// Takoz işçiliğinin tahsil şekli — ERPPRO <c>tIscilikDurumu</c> karşılığı.
/// Sonucu <c>PayUnitId</c>'ye yansır: <see cref="DeductFromGold"/> → HAS birimi,
/// <see cref="WithCash"/> → seçilen para birimi (metal-dışı).
/// </summary>
public enum BullionLaborMode : byte
{
    /// <summary>Altından Düş — işçilik HAS cinsinden borçlanır.</summary>
    DeductFromGold = 0,

    /// <summary>Para İle — işçilik seçilen para biriminde borçlanır.</summary>
    WithCash       = 1,
}
