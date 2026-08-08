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

    /// <summary>Bu N11 SİPARİŞ durumu satıcı aksiyonu (toplu Kabul/Red) BEKLİYOR mu? Yalnız {1 İşlem Bekliyor,
    /// 2 İşlemde} için <c>true</c>; 3 İptal / 4 Geçersiz / 5 Tamamlandı ve bilinmeyen/boş kod → <c>false</c>
    /// (fail-safe: N11 siparişi kapatmışsa toplu Kabul/Red anlamsız — buton gösterilmez).</summary>
    public static bool AwaitsSellerActionForOrder(string? rawOrderStatus)
    {
        if (!int.TryParse(rawOrderStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;   // bilinmeyen/boş kod → aksiyon gösterme (güvenli)
        }

        return code is 1 or 2;
    }

    /// <summary>Bu N11 kalem durumu satıcı aksiyonu (Kabul/Red/Kargoya-Ver) BEKLİYOR mu? Yalnız {1 İşlem Bekliyor,
    /// 2 Ödendi, 5 Kabul Edilmiş} kodları için <c>true</c>; diğer TÜM kodlar (6 Kargoda, 7 Teslim, 10 Tamamlandı,
    /// 4 İptal, 8 Reddedilmiş, 9/11/12/13/16 iade/claim, vb.) için <c>false</c>. Bilinmeyen/parse edilemeyen kod →
    /// <c>false</c> (fail-safe: N11 tarafında ne olduğu belirsizse aksiyon gösterme).</summary>
    public static bool AwaitsSellerAction(string? rawItemStatus)
    {
        if (!int.TryParse(rawItemStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;   // bilinmeyen/boş kod → aksiyon gösterme (güvenli)
        }

        return code is 1 or 2 or 5;
    }

    /// <summary>Kalem "İptal Talep Edildi" (kod <b>51</b>) mi — rezervasyonun iptal kararı eksenini uyandırır.
    ///
    /// <para><b>52 (iade) ve 53 (değişim) BİLİNÇLİ OLARAK DIŞARIDA:</b> onlar iptal değil İADE sürecinin
    /// sinyalidir ve tamamen farklı bir yol izler — iade, malın fiziksel olarak kasaya girmesini bekler.
    /// İkisini aynı köprüye bağlamak, teslim edilmiş bir siparişin iade talebini "iptal kararı bekliyor" diye
    /// göstermek olurdu; operatör iptali onaylarsa stok geri verilir ama mal hâlâ müşteridedir.</para>
    ///
    /// <para>Bilinmeyen/boş kod → <c>false</c> (fail-safe: uydurma iptal sinyali üretme).</para></summary>
    public static bool IsCancellationRequested(string? rawItemStatus)
    {
        if (!int.TryParse(rawItemStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        return code == 51;
    }

    /// <summary>Kalem İADE/DEĞİŞİM sürecinde mi — talepten teslim alınmış iadeye kadar tüm aşamalar.
    ///
    /// <para><b>Kapsam:</b> 9 (İade Edildi) · 11 (Talep) · 12 (Tamamlandı) · 13 (Kargoda İade) ·
    /// 16 (Teslim Edilmiş İade) · 52 (İade Talebi) · 53 (Değişim Talebi).</para>
    ///
    /// <para><b>51 (İptal Talebi) BURAYA GİRMEZ</b> — o ayrı bir eksendir ve rezervasyonun iptal kararını
    /// uyandırır. İkisi karışsaydı iptal talebi "iade girişi bekliyor" diye görünür, kullanıcı hiç çıkmamış
    /// bir malın iadesini kaydetmeye çalışırdı.</para>
    ///
    /// <para><b>Bu bir SİNYALDİR, karar değil</b> (§6): sistem yalnız "bu siparişte iade süreci var" der.
    /// Stok, mal fiziksel olarak kasaya GİRENE kadar dönmez — girişi operatör kaydeder.</para></summary>
    public static bool IsReturnFlowSignal(string? rawItemStatus)
    {
        if (!int.TryParse(rawItemStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        return code is 9 or 11 or 12 or 13 or 16 or 52 or 53;
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
