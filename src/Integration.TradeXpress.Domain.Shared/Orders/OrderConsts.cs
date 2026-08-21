namespace Integration.TradeXpress.Orders;

/// <summary>Sipariş (Order) + sipariş satırı (OrderLine) alan sınırları — Domain.Shared (entity + EF + DTO paylaşır).
/// Alanlar pazaryerinden gelen SNAPSHOT'lardır (yerel ürüne bağımsız) → uzak veriye dayanıklı üst sınırlar.</summary>
public static class OrderConsts
{
    /// <summary>Kanaldaki uzak sipariş kimliği (idempotency anahtarı — shipmentPackageId gibi; numerik olabilir ama string tutulur).</summary>
    public const int RemoteOrderIdMaxLength = 128;

    /// <summary>İnsan-okunur sipariş numarası (kanal orderNumber).</summary>
    public const int OrderNumberMaxLength = 64;

    /// <summary>Ham kanal durumu metni (Created/Shipped/Delivered ...).</summary>
    public const int RemoteStatusMaxLength = 64;

    /// <summary>Müşteri görünen adı (maskeli/kısa — KVKK gereği tam kimlik saklanmaz).</summary>
    public const int CustomerNameMaxLength = 256;

    /// <summary>Kargo firması adı (opsiyonel).</summary>
    public const int CargoProviderMaxLength = 128;

    /// <summary>Kargo takip numarası (opsiyonel).</summary>
    public const int CargoTrackingNumberMaxLength = 128;

    // ── OrderLine ──────────────────────────────────────────────────────────────
    /// <summary>Uzak satır kimliği (kanal line/orderLine id).</summary>
    public const int RemoteLineIdMaxLength = 128;

    public const int BarcodeMaxLength = 64;
    public const int StockCodeMaxLength = 100;

    /// <summary>Satırdaki ürün adının o ANDAKİ snapshot'ı (yerel ürün silinse/hiç olmasa da satır tam anlamlı).</summary>
    public const int ProductNameSnapshotMaxLength = 512;

    /// <summary>Uzak satır durumu (kanal line status — opsiyonel).</summary>
    public const int RemoteLineStatusMaxLength = 64;

    // ── OrderDetailSnapshot (owned JSON — getOrderDetail zengin detayı; TEK nvarchar(max) kolon) ──
    /// <summary>Detay snapshot'ındaki KISA metin alanları (ad/e-posta/il/ilçe/telefon/vergi/durum vb.) — JSON blob'da
    /// kolon-uzunluğu yok ama şişme/DoS'a karşı savunmacı kırpma sınırı.</summary>
    public const int DetailShortTextMaxLength = 256;

    /// <summary>Detay snapshot'ındaki UZUN metin alanları (açık adres, nitelik değeri) — savunmacı kırpma sınırı.</summary>
    public const int DetailLongTextMaxLength = 1024;

    // ── OrderLineOperationalData (O2 — state machine) ──────────────────────────
    /// <summary>N11 OrderItemReject'e gönderilen serbest metin gerekçe.</summary>
    public const int RejectReasonMaxLength = 512;

    /// <summary>N11 kargo firması id'si (shipmentCompany.id) — MakeOrderItemShipment isteği.</summary>
    public const int ShipmentCompanyIdMaxLength = 32;

    // ── OrderReservation — "Karar Bekleyenler" görünümü ────────────────────────
    /// <summary>YAŞLANAN rezerv eşiği (gün). ⛔ Süre AŞIMI DEĞİLDİR — zaman aşımı kavramı yok ("sipariş
    /// siparıştir"), eşiği aşan rezervasyon KENDİLİĞİNDEN bırakılmaz. Yalnız GÖRÜNÜRLÜK: bu yaştan eski aktif
    /// rezervler "Karar Bekleyenler" sekmesine düşer ki unutulmuş sipariş kendini göstersin (2026-08-21).</summary>
    public const int AgingReservationThresholdDays = 7;
}
