using System.Collections.Generic;
using System.Globalization;

namespace Integration.TradeXpress.Orders;

/// <summary>N11 sipariş KODLARI → insan-okunur etiket (N11 SOAP Referans Dokümantasyonu v4.6 GROUND TRUTH): sipariş
/// durumu · kalem durumu · <b>ödeme tipi</b>. N11 bu alanları tam sayı KOD döner (ör. kalem "10", ödeme "1"); grid/popup
/// ham sayı yerine bu etiketi gösterir. Kod-içi sözlük (resx değil — N11-özel, çoğu marka adı dil-agnostik; tr/en parity
/// yükü yok). Bilinmeyen kod ham değerle döner (fail-open). Hem <see cref="N11OrderStatusMapper"/> (nötr eşleme) hem
/// grid/popup (görüntü) tüketir.</summary>
public static class N11OrderStatusCatalog
{
    // orderList.order.status — Sipariş Durumu (SOAP ref v4.6).
    private static readonly Dictionary<int, (string Tr, string En)> OrderStatuses = new()
    {
        [1] = ("İşlem Bekliyor", "Awaiting"),
        [2] = ("İşlemde", "Processing"),
        [3] = ("İptal Edilmiş", "Cancelled"),
        [4] = ("Geçersiz", "Invalid"),
        [5] = ("Tamamlandı", "Completed"),
    };

    // orderList.order.orderItemList.orderItem.status — Sipariş Maddesi Durumu (SOAP ref v4.6).
    private static readonly Dictionary<int, (string Tr, string En)> ItemStatuses = new()
    {
        [1] = ("İşlem Bekliyor", "Awaiting"),
        [2] = ("Ödendi", "Paid"),
        [3] = ("Geçersiz", "Invalid"),
        [4] = ("İptal Edilmiş", "Cancelled"),
        [5] = ("Kabul Edilmiş", "Accepted"),
        [6] = ("Kargoda", "In Cargo"),
        [7] = ("Teslim Edilmiş", "Delivered"),
        [8] = ("Reddedilmiş", "Rejected"),
        [9] = ("İade Edildi", "Returned"),
        [10] = ("Tamamlandı", "Completed"),
        [11] = ("İade İptal Değişim Talep Edildi", "Claim Requested"),
        [12] = ("İade İptal Değişim Tamamlandı", "Claim Completed"),
        [13] = ("Kargoda İade", "Return In Cargo"),
        [14] = ("Kargo Yapılması Gecikmiş", "Late Shipment"),
        [15] = ("Kabul Edilmiş Ama Zamanında Kargoya Verilmemiş", "Accepted But Not Shipped In Time"),
        [16] = ("Teslim Edilmiş İade", "Delivered Return"),
        [17] = ("Ödeme Ertelendi", "Payment Deferred"),
        [51] = ("İptal Talep Edildi", "Cancel Requested"),
        [52] = ("İade Talep Edildi", "Return Requested"),
        [53] = ("Değişim Talep Edildi", "Exchange Requested"),
    };

    // orderDetail.paymentType — Ödeme Tipi (SOAP ref v4.6). Çoğu marka/banka adı (dil-agnostik); 1 ve 14 dile göre değişir.
    private static readonly Dictionary<int, (string Tr, string En)> PaymentTypes = new()
    {
        [1] = ("Kredi Kartı", "Credit Card"),
        [2] = ("BKMEXPRESS", "BKMEXPRESS"),
        [3] = ("AKBANKDIREKT", "AKBANKDIREKT"),
        [4] = ("PAYPAL", "PAYPAL"),
        [5] = ("MallPoint", "MallPoint"),
        [6] = ("GARANTIPAY", "GARANTIPAY"),
        [7] = ("GarantiLoan", "GarantiLoan"),
        [8] = ("MasterPass", "MasterPass"),
        [9] = ("ISBANKPAY", "ISBANKPAY"),
        [10] = ("PAYCELL", "PAYCELL"),
        [11] = ("COMPAY", "COMPAY"),
        [12] = ("YKBPAY", "YKBPAY"),
        [13] = ("FIBABANK", "FIBABANK"),
        [14] = ("Diğer", "Other"),
    };

    /// <summary>Sipariş durum kodunun etiketi (kültüre göre tr/en); çözülemezse ham değer.</summary>
    public static string? OrderStatusLabel(string? rawStatus)
    {
        return Lookup(OrderStatuses, rawStatus);
    }

    /// <summary>Kalem durum kodunun etiketi (kültüre göre tr/en); çözülemezse ham değer.</summary>
    public static string? ItemStatusLabel(string? rawStatus)
    {
        return Lookup(ItemStatuses, rawStatus);
    }

    /// <summary>Ödeme tipi kodunun etiketi (ör. "1" → Kredi Kartı); çözülemezse ham değer.</summary>
    public static string? PaymentTypeLabel(string? rawPaymentType)
    {
        return Lookup(PaymentTypes, rawPaymentType);
    }

    private static string? Lookup(Dictionary<int, (string Tr, string En)> map, string? rawStatus)
    {
        if (!int.TryParse(rawStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) || !map.TryGetValue(code, out var label))
        {
            return rawStatus;   // bilinmeyen kod → ham değer (fail-open)
        }

        var tr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        return tr ? label.Tr : label.En;
    }
}
