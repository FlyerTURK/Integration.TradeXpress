namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// İşlem kısa kodu (ERPPROV3 paritesi): <see cref="ProcessType"/> + <see cref="ProcessDirectionType"/> +
/// <see cref="ProcessPaymentType"/> harflerinin birleşimi (ör. Nakit+Giriş+Peşin = "NGP").
/// Grid'deki "İşlem" kolonu bu kodu gösterir.
/// </summary>
public static class VoucherProcessCode
{
    /// <summary>Birleşik kısa kod. Çevir/Vadeli'de ödeme tipi yok (tip harfi eklenmez);
    /// Takoz → yön kodu ("1"/"2"); Çeşni → "C".</summary>
    public static string Of(ProcessType process, ProcessDirectionType direction, ProcessPaymentType? payment = null)
    {
        if (process == ProcessType.Bullion)
            return ((int)direction % 2) == 0 ? "1" : "2";
        if (process == ProcessType.Assay)
            return "C";

        var pay = process is ProcessType.Convert or ProcessType.Future ? (ProcessPaymentType?)null : payment;
        return Code(process) + Code(direction) + (pay is { } p ? Code(p) : string.Empty);
    }

    public static string Code(ProcessType p) => p switch
    {
        ProcessType.Metal    => "M",
        ProcessType.Scrap    => "H",
        ProcessType.Cash     => "N",
        ProcessType.Convert  => "C",
        ProcessType.Service  => "G",
        ProcessType.Future   => "V",
        ProcessType.Stone    => "T",
        ProcessType.Transfer => "V",
        _ => "?",
    };

    public static string Code(ProcessDirectionType d) => d switch
    {
        ProcessDirectionType.Inbound  => "G",
        ProcessDirectionType.Outbound => "C",
        ProcessDirectionType.Credit   => "A",
        ProcessDirectionType.Debit    => "B",
        ProcessDirectionType.Buy      => "A",
        ProcessDirectionType.Sell     => "S",
        _ => "?",
    };

    public static string Code(ProcessPaymentType t) => t switch
    {
        ProcessPaymentType.Normal       => "N",
        ProcessPaymentType.WithCash     => "P",
        ProcessPaymentType.WithCurrency => "B",
        ProcessPaymentType.Return       => "I",
        ProcessPaymentType.Consignment  => "E",
        ProcessPaymentType.WithUnit     => "M",
        _ => "?",
    };
}
