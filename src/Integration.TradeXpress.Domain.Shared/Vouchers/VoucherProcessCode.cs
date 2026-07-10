namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// İşlem kısa kodu (ERPPROV3 grid paritesi — kullanıcı gerçek grid otoritesi): <see cref="ProcessType"/> +
/// <see cref="ProcessDirectionType"/> + <see cref="ProcessPaymentType"/> harflerinin birleşimi
/// (ör. Nakit+Giriş+Peşin = "NGP", Taş+Giriş+Normal = "TGN"). Grid'deki "İşlem" kolonu bu kodu gösterir.
/// </summary>
public static class VoucherProcessCode
{
    /// <summary>Birleşik kısa kod. Çevir/Vadeli'de ödeme tipi yok (tip harfi eklenmez); Çeşni → "C";
    /// Takoz → sabit "TGA" (giriş) / "TCA" (çıkış). Hem Taş hem Takoz "T" ile başlar ama takozun 3. harfi
    /// daima "A"; hiçbir ödeme tipi "A" üretmediğinden takoz taş'tan bu harfle ayrışır → ÇAKIŞMA YOK
    /// (ör. Taş Giriş Normal = "TGN" ↔ Takoz Giriş = "TGA").</summary>
    public static string Of(ProcessType process, ProcessDirectionType direction, ProcessPaymentType? payment = null)
    {
        if (process == ProcessType.Bullion)
            return direction.IsInflow() ? "TGA" : "TCA";
        if (process == ProcessType.Assay)
            return "C";

        var pay = process is ProcessType.Convert or ProcessType.Future ? (ProcessPaymentType?)null : payment;
        return Code(process) + Code(direction) + (pay is { } p ? Code(p) : string.Empty);
    }

    public static string Code(ProcessType p)
    {
        return p switch
        {
            ProcessType.Metal    => "M",
            ProcessType.Scrap    => "H",
            ProcessType.Cash     => "N",
            ProcessType.Convert  => "C",
            ProcessType.Service  => "G",
            ProcessType.Future   => "V",
            ProcessType.Stone    => "T",
            ProcessType.Jewelry  => "J",
            // Transfer 'V' harfini Future ile PAYLAŞIR ama BİLEŞİK kodlar çakışmaz (kullanıcı kararı
            // 2026-07-03): Future yönü Buy/Sell → "VA"/"VS"; Virman yönü Giriş/Çıkış + ödeme Normal →
            // "VGN"/"VCN". Hiçbir Future kodu 3. harf üretmez (ödeme tipi eklenmez) → ayrışma garantili.
            ProcessType.Transfer => "V",
            // Bullion bu karaktere HİÇ düşmez: kısaltma kodu literal "TGA"/"TCA" ile özel-yolludur
            // (kullanıcı kuralı). Bu dal yalnız beklenmedik çağrılara karşı duruyor.
            ProcessType.Bullion  => "T",
            // Dekont (Borç/Alacak) — "D"; Convert'in "C"si ile çakışmaz (Assay literal "C" özel-yollu).
            ProcessType.DebitNote => "D",
            _ => "?",
        };
    }

    public static string Code(ProcessDirectionType d)
    {
        return d switch
        {
            ProcessDirectionType.Inbound  => "G",
            ProcessDirectionType.Outbound => "C",
            ProcessDirectionType.Credit   => "A",
            ProcessDirectionType.Debit    => "B",
            ProcessDirectionType.Buy      => "A",
            ProcessDirectionType.Sell     => "S",
            _ => "?",
        };
    }

    public static string Code(ProcessPaymentType t)
    {
        return t switch
        {
            ProcessPaymentType.Normal       => "N",
            ProcessPaymentType.WithCash     => "P",
            ProcessPaymentType.WithCurrency => "B",
            ProcessPaymentType.Return       => "I",
            ProcessPaymentType.Consignment  => "E",
            ProcessPaymentType.WithUnit     => "M",
            // "R" hiçbir ProcessType/Direction harfiyle çakışma üretmez ("A" değil → Takoz ayrışması korunur).
            ProcessPaymentType.Reservation  => "R",
            _ => "?",
        };
    }
}
