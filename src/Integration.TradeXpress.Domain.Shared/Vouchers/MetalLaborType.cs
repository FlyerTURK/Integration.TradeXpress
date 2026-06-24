namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Maden işçilik türü (ERPPROV3 <c>MetalLaborType</c> paritesi) — işçiliğin neye göre hesaplandığı:
/// <see cref="Amount"/> = miktar (gram) başına → <c>işçilikToplamı = Amount × işçilikBedeli</c>;
/// <see cref="Quantity"/> = adet başına → <c>işçilikToplamı = Adet × işçilikBedeli</c>.
/// </summary>
public enum MetalLaborType : byte
{
    /// <summary>İşçilik miktar (gram) başına.</summary>
    Amount   = 0,

    /// <summary>İşçilik adet başına.</summary>
    Quantity = 1,
}
