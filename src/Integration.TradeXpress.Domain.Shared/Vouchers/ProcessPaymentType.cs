namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Ödeme/işlem tipi (ERPPROV3 <c>PaymentType</c> paritesi). İşlemin bakiyeye mi
/// yansıyacağı yoksa anlık peşin mi olduğunu belirler. Bazı işlem türlerinde hiç
/// olmaz → <see cref="VoucherLine.PaymentType"/> <c>null</c>.
/// </summary>
public enum ProcessPaymentType : byte
{
    /// <summary>Normal — bakiyeye yansır (veresiye/hesaba).</summary>
    Normal       = 0,

    /// <summary>Peşin — anlık ödeme (bakiyeye yansımaz).</summary>
    WithCash     = 1,

    /// <summary>Bedelli.</summary>
    WithCurrency = 2,

    /// <summary>İade.</summary>
    Return       = 3,

    /// <summary>Emanet.</summary>
    Consignment  = 4,

    /// <summary>Birim bazlı (legacy <c>MIKTAR</c>).</summary>
    WithUnit     = 5,

    /// <summary>Rezervasyon — bakiyeye YANSIMAZ, fiziksel stok hareketi YARATMAZ; yalnız
    /// kullanılabilir stoğu düşüren taahhüt sayacı. Kapanma elle [ilk faz].</summary>
    Reservation  = 6,
}
