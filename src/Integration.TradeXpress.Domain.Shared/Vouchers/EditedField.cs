namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// İşlem panelinde kullanıcının en son değiştirdiği alan. Hesap motoru
/// (<see cref="VoucherLineCalculator"/>) buna göre yön seçer: yapısal değişimde
/// (Commodity/PayUnit/PaymentType) pariteyi yeniden yükler, Amount/PayFactor'de
/// Tutar'ı, PayTotal'de Fiyat'ı geri-hesaplar.
/// </summary>
public enum EditedField : byte
{
    None = 0,
    Amount,
    PayFactor,
    PayTotal,
    Commodity,
    PayUnit,
    PaymentType,
    Direction,
}
