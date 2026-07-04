namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// İşlem yönü (ERPPROV3 <c>VoucherDirection</c> paritesi). Tek enum; UI combosu
/// <see cref="ProcessType"/>'a göre ilgili alt kümeyi gösterir (Nakit/Maden/Hurda →
/// Giriş/Çıkış; Çevir → Alacak/Borç; Vadeli → Alış/Satış). Bakiye işareti (+/−) ve
/// "giriş mi" kararı bu değerden türetilir: <c>(int)Direction % 2 == 0</c> → giriş
/// (tek kaynak: <see cref="ProcessDirectionTypeExtensions.IsInflow"/>).
/// </summary>
public enum ProcessDirectionType : byte
{
    /// <summary>Giriş — bakiyeye (+) yönde.</summary>
    Inbound  = 0,

    /// <summary>Çıkış — bakiyeye (−) yönde.</summary>
    Outbound = 1,

    /// <summary>Alacak — bakiyeye (+) (çevrim/borç-alacak ekseni).</summary>
    Credit   = 2,

    /// <summary>Borç — bakiyeye (−).</summary>
    Debit    = 3,

    /// <summary>Alış — alım (vadeli alış vb.).</summary>
    Buy      = 4,

    /// <summary>Satış — satım.</summary>
    Sell     = 5,
}
