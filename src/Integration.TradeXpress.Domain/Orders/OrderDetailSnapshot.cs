namespace Integration.TradeXpress.Orders;

/// <summary>
/// Bir siparişin PAZARYERİNDEN gelen ZENGİN DETAY snapshot'ı — N11 <c>getOrderDetail</c>'in kanal-agnostik projeksiyonu.
/// <see cref="Order"/>'da TEK owned JSON kolonu (<c>Detail</c>) olarak yaşar (value-converter; <c>OrderDetailSnapshotJson</c>).
/// Sipariş DETAY popup'ının kaynağı: alıcı · fatura/teslimat adresi · tutar kırılımı · kalem-başı komisyon/indirim/
/// nitelik/kargo. Popup DB'den okur — tıklamada CANLI çağrı YOK (2026-07-11 kullanıcı kararı: "canlı çekme, sistemimizi
/// hizala; getOrderDetail daha iyi"). Sync sırasında order-başına doldurulur (enrichment; çekilemezse null, sipariş yine
/// kaydolur).
///
/// <para><b>KENDİ GERÇEĞİNİ TAŞIR (VoucherLine felsefesi):</b> tüm alanlar yerel ürün/kanal olmadan tam anlamlı;
/// kanal silinse de detay sağ kalır. TOLERANT: uzak veri kontrolümüzde değil → alanlar nullable, boş→null, taşan
/// uzunluk KIRPILIR (fail-fast DEĞİL — kaydı kaybetmek daha kötü; framework <see cref="Integration.Framework.Addressing.Address"/>
/// VO'su zorunlu City/Line ile fail-fast ettiğinden burada KULLANILMAZ).</para>
///
/// <para><b>PII (KVKK) — 2026-07-11 kullanıcı kararı:</b> O0'ın "tam kimlik saklanmaz" duruşu bu detay için
/// GENİŞLETİLDİ; alıcı e-posta/TC/vergi + tam adres SAKLANIR ("Tümü" kapsamı seçildi). Salt-okuma; dışarı yazılmaz.</para>
/// </summary>
public class OrderDetailSnapshot
{
    #region Constructors

    protected OrderDetailSnapshot()
    {
        Items = new List<OrderDetailItem>();
    }

    public OrderDetailSnapshot(
        OrderDetailParty? buyer,
        OrderDetailAddress? billingAddress,
        OrderDetailAddress? shippingAddress,
        int? invoiceType,
        string? paymentType,
        string? citizenshipId,
        OrderDetailTotals? totals,
        IEnumerable<OrderDetailItem>? items,
        DateTime fetchedAt)
    {
        Buyer = buyer;
        BillingAddress = billingAddress;
        ShippingAddress = shippingAddress;
        InvoiceType = invoiceType;
        PaymentType = OrderSnapshotText.Short(paymentType);
        CitizenshipId = OrderSnapshotText.Short(citizenshipId);
        Totals = totals;
        Items = items?.Where(i => i is not null).ToList() ?? new List<OrderDetailItem>();
        FetchedAt = fetchedAt;
    }

    #endregion

    #region Properties

    /// <summary>Siparişi veren alıcı (ad/e-posta/TC/vergi).</summary>
    public virtual OrderDetailParty? Buyer { get; protected set; }

    /// <summary>Fatura adresi.</summary>
    public virtual OrderDetailAddress? BillingAddress { get; protected set; }

    /// <summary>Teslimat (kargo) adresi.</summary>
    public virtual OrderDetailAddress? ShippingAddress { get; protected set; }

    /// <summary>Fatura tipi (N11 ham kodu): 1 Bireysel · 2 Kurumsal (null = gelmedi).</summary>
    public virtual int? InvoiceType { get; protected set; }

    /// <summary>Ödeme tipi (N11 ham metni).</summary>
    public virtual string? PaymentType { get; protected set; }

    /// <summary>Alıcı TC kimlik numarası (order.citizenshipId).</summary>
    public virtual string? CitizenshipId { get; protected set; }

    /// <summary>Tutar kırılımı (billingTemplate) — komisyon/indirim/fatura/vade.</summary>
    public virtual OrderDetailTotals? Totals { get; protected set; }

    /// <summary>Kalem detayları (komisyon/indirim/kargo/nitelik) — grid liste satırından ZENGİN üst küme.</summary>
    public virtual IReadOnlyList<OrderDetailItem> Items { get; protected set; } = null!;

    /// <summary>Bu detayın pazaryerinden çekildiği an (UTC) — tazelik göstergesi.</summary>
    public virtual DateTime FetchedAt { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"OrderDetail(items={Items.Count})";
    }

    #endregion
}

/// <summary>Siparişi veren alıcı — kişisel kimlik (PII; salt-okuma snapshot).</summary>
public class OrderDetailParty
{
    #region Constructors

    protected OrderDetailParty()
    {
    }

    public OrderDetailParty(string? fullName, string? email, string? tcId, string? taxId, string? taxOffice)
    {
        FullName = OrderSnapshotText.Short(fullName);
        Email = OrderSnapshotText.Short(email);
        TcId = OrderSnapshotText.Short(tcId);
        TaxId = OrderSnapshotText.Short(taxId);
        TaxOffice = OrderSnapshotText.Short(taxOffice);
    }

    #endregion

    #region Properties

    public virtual string? FullName { get; protected set; }
    public virtual string? Email { get; protected set; }
    public virtual string? TcId { get; protected set; }
    public virtual string? TaxId { get; protected set; }
    public virtual string? TaxOffice { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return FullName ?? string.Empty;
    }

    #endregion
}

/// <summary>Sipariş adresi (fatura/teslimat) — TOLERANT snapshot (tüm alan nullable; framework Address VO'su zorunlu
/// City/Line ile fail-fast ettiğinden uzak veri için KULLANILMAZ).</summary>
public class OrderDetailAddress
{
    #region Constructors

    protected OrderDetailAddress()
    {
    }

    public OrderDetailAddress(
        string? fullName,
        string? line,
        string? neighborhood,
        string? district,
        string? city,
        string? postalCode,
        string? gsm,
        string? tcId,
        string? taxId,
        string? taxOffice)
    {
        FullName = OrderSnapshotText.Short(fullName);
        Line = OrderSnapshotText.Long(line);
        Neighborhood = OrderSnapshotText.Short(neighborhood);
        District = OrderSnapshotText.Short(district);
        City = OrderSnapshotText.Short(city);
        PostalCode = OrderSnapshotText.Short(postalCode);
        Gsm = OrderSnapshotText.Short(gsm);
        TcId = OrderSnapshotText.Short(tcId);
        TaxId = OrderSnapshotText.Short(taxId);
        TaxOffice = OrderSnapshotText.Short(taxOffice);
    }

    #endregion

    #region Properties

    /// <summary>Adres sahibinin adı.</summary>
    public virtual string? FullName { get; protected set; }

    /// <summary>Açık adres (cadde/sokak/no).</summary>
    public virtual string? Line { get; protected set; }

    public virtual string? Neighborhood { get; protected set; }
    public virtual string? District { get; protected set; }
    public virtual string? City { get; protected set; }
    public virtual string? PostalCode { get; protected set; }

    /// <summary>Telefon (gsm).</summary>
    public virtual string? Gsm { get; protected set; }

    public virtual string? TcId { get; protected set; }
    public virtual string? TaxId { get; protected set; }
    public virtual string? TaxOffice { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Doldurulmuş herhangi bir alan var mı — hepsi boşsa popup adresi hiç göstermez.</summary>
    public virtual bool HasAny()
    {
        return !string.IsNullOrWhiteSpace(FullName)
            || !string.IsNullOrWhiteSpace(Line)
            || !string.IsNullOrWhiteSpace(City)
            || !string.IsNullOrWhiteSpace(District)
            || !string.IsNullOrWhiteSpace(Gsm);
    }

    public override string ToString()
    {
        var parts = new[] { Line, Neighborhood, District, City, PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }

    #endregion
}

/// <summary>Sipariş tutar kırılımı (N11 billingTemplate) — komisyon/indirim/fatura/vade toplamları.</summary>
public class OrderDetailTotals
{
    #region Constructors

    protected OrderDetailTotals()
    {
    }

    public OrderDetailTotals(
        decimal? originalPrice,
        decimal? dueAmount,
        decimal? sellerInvoiceAmount,
        decimal? totalMallDiscountPrice,
        decimal? totalSellerDiscount,
        decimal? totalServiceItemOriginalPrice)
    {
        OriginalPrice = originalPrice;
        DueAmount = dueAmount;
        SellerInvoiceAmount = sellerInvoiceAmount;
        TotalMallDiscountPrice = totalMallDiscountPrice;
        TotalSellerDiscount = totalSellerDiscount;
        TotalServiceItemOriginalPrice = totalServiceItemOriginalPrice;
    }

    #endregion

    #region Properties

    /// <summary>Tüm indirimlerden ÖNCEki tutar.</summary>
    public virtual decimal? OriginalPrice { get; protected set; }

    /// <summary>Tahsil edilecek tutar (vade farkı dahil).</summary>
    public virtual decimal? DueAmount { get; protected set; }

    /// <summary>Komisyon/satıcı fatura tutarı.</summary>
    public virtual decimal? SellerInvoiceAmount { get; protected set; }

    /// <summary>Toplam N11 indirimi.</summary>
    public virtual decimal? TotalMallDiscountPrice { get; protected set; }

    /// <summary>Toplam satıcı indirimi.</summary>
    public virtual decimal? TotalSellerDiscount { get; protected set; }

    /// <summary>Toplam servis (kargo hizmeti) fiyatı.</summary>
    public virtual decimal? TotalServiceItemOriginalPrice { get; protected set; }

    #endregion
}

/// <summary>Sipariş kalemi ZENGİN detayı (getOrderDetail item) — komisyon/indirim/kargo/nitelik. Grid liste satırının
/// (<see cref="OrderLine"/>) üst kümesi; popup kalem grid'inin kaynağı.</summary>
public class OrderDetailItem
{
    #region Constructors

    protected OrderDetailItem()
    {
        Attributes = new List<OrderDetailItemAttribute>();
        CustomTexts = new List<OrderDetailItemCustomText>();
    }

    public OrderDetailItem(
        string? remoteLineId,
        string? productId,
        string? productName,
        string? productSellerCode,
        string? skuId,
        decimal quantity,
        decimal price,
        decimal? commission,
        decimal? dueAmount,
        decimal? mallDiscount,
        decimal? sellerDiscount,
        decimal? sellerInvoiceAmount,
        string? status,
        DateTime? approveDate,
        DateTime? shipmentDate,
        string? shipmentCompany,
        int? shipmentMethod,
        string? shipmentCode,
        string? shipmentCompanyId,
        string? shipmentCompanyShortName,
        string? trackingNumber,
        string? campaignNumber,
        string? campaignNumberStatus,
        IEnumerable<OrderDetailItemAttribute>? attributes,
        IEnumerable<OrderDetailItemCustomText>? customTexts)
    {
        RemoteLineId = OrderSnapshotText.Short(remoteLineId);
        ProductId = OrderSnapshotText.Short(productId);
        ProductName = OrderSnapshotText.Long(productName);
        ProductSellerCode = OrderSnapshotText.Short(productSellerCode);
        SkuId = OrderSnapshotText.Short(skuId);
        Quantity = quantity;
        Price = price;
        Commission = commission;
        DueAmount = dueAmount;
        MallDiscount = mallDiscount;
        SellerDiscount = sellerDiscount;
        SellerInvoiceAmount = sellerInvoiceAmount;
        Status = OrderSnapshotText.Short(status);
        ApproveDate = approveDate;
        ShipmentDate = shipmentDate;
        ShipmentCompany = OrderSnapshotText.Short(shipmentCompany);
        ShipmentMethod = shipmentMethod;
        ShipmentCode = OrderSnapshotText.Short(shipmentCode);
        ShipmentCompanyId = OrderSnapshotText.Short(shipmentCompanyId);
        ShipmentCompanyShortName = OrderSnapshotText.Short(shipmentCompanyShortName);
        TrackingNumber = OrderSnapshotText.Short(trackingNumber);
        CampaignNumber = OrderSnapshotText.Short(campaignNumber);
        CampaignNumberStatus = OrderSnapshotText.Short(campaignNumberStatus);
        Attributes = attributes?.Where(a => a is not null).ToList() ?? new List<OrderDetailItemAttribute>();
        CustomTexts = customTexts?.Where(c => c is not null).ToList() ?? new List<OrderDetailItemCustomText>();
    }

    #endregion

    #region Properties

    public virtual string? RemoteLineId { get; protected set; }
    public virtual string? ProductId { get; protected set; }
    public virtual string? ProductName { get; protected set; }
    public virtual string? ProductSellerCode { get; protected set; }

    /// <summary>N11 SKU ID (stockKeepingUnitId).</summary>
    public virtual string? SkuId { get; protected set; }

    public virtual decimal Quantity { get; protected set; }

    /// <summary>Birim fiyat (indirimler hariç liste fiyatı).</summary>
    public virtual decimal Price { get; protected set; }

    /// <summary>N11 hizmet tutarı (komisyon).</summary>
    public virtual decimal? Commission { get; protected set; }

    /// <summary>Tahsil edilecek tutar.</summary>
    public virtual decimal? DueAmount { get; protected set; }

    /// <summary>Ürünle ilgili N11 indirimi.</summary>
    public virtual decimal? MallDiscount { get; protected set; }

    /// <summary>Mağaza indirimi.</summary>
    public virtual decimal? SellerDiscount { get; protected set; }

    /// <summary>Satıcı fatura tutarı.</summary>
    public virtual decimal? SellerInvoiceAmount { get; protected set; }

    /// <summary>Ham kalem durumu (N11 orderItem.status kodu) — etiket <see cref="N11OrderStatusCatalog"/>'da.</summary>
    public virtual string? Status { get; protected set; }

    /// <summary>Kalemin kabul edildiği tarih (UTC).</summary>
    public virtual DateTime? ApproveDate { get; protected set; }

    /// <summary>Kargolama tarihi (UTC).</summary>
    public virtual DateTime? ShipmentDate { get; protected set; }

    public virtual string? ShipmentCompany { get; protected set; }

    /// <summary>Kargo yöntemi (N11 ham kodu): 1 Kargo · 2 Diğer.</summary>
    public virtual int? ShipmentMethod { get; protected set; }

    public virtual string? ShipmentCode { get; protected set; }

    /// <summary>Kargo firması N11 id'si (shipmentInfo.shipmentCompany.id).</summary>
    public virtual string? ShipmentCompanyId { get; protected set; }

    /// <summary>Kargo firması kısa adı (ör. "YK").</summary>
    public virtual string? ShipmentCompanyShortName { get; protected set; }

    /// <summary>Kargo takip numarası (kalem düzeyi shipmentInfo.trackingNumber).</summary>
    public virtual string? TrackingNumber { get; protected set; }

    /// <summary>N11 kampanya numarası (shipmentInfo.campaignNumber).</summary>
    public virtual string? CampaignNumber { get; protected set; }

    /// <summary>Kampanya numarası durumu (shipmentInfo.campaignNumberStatus).</summary>
    public virtual string? CampaignNumberStatus { get; protected set; }

    /// <summary>Kalem nitelikleri (ad/değer) — ör. Renk/Beden.</summary>
    public virtual IReadOnlyList<OrderDetailItemAttribute> Attributes { get; protected set; } = null!;

    /// <summary>Alıcının girdiği ÖZEL METİN seçenekleri (getOrderDetail customTextOptionValues) — kişiselleştirilmiş
    /// üründe ne yazılacağı (ör. kaşe/mühür metni, mürekkep rengi). Kaşe satıcısı için "ne kazınacak" bilgisi.</summary>
    public virtual IReadOnlyList<OrderDetailItemCustomText> CustomTexts { get; protected set; } = null!;

    #endregion

    #region Methods

    public override string ToString()
    {
        return ProductName ?? ProductSellerCode ?? string.Empty;
    }

    #endregion
}

/// <summary>Sipariş kalemi niteliği (ad/değer) — getOrderDetail item.attributes.attribute.</summary>
public class OrderDetailItemAttribute
{
    #region Constructors

    protected OrderDetailItemAttribute()
    {
    }

    public OrderDetailItemAttribute(string? name, string? value)
    {
        Name = OrderSnapshotText.Short(name);
        Value = OrderSnapshotText.Long(value);
    }

    #endregion

    #region Properties

    public virtual string? Name { get; protected set; }
    public virtual string? Value { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Name}: {Value}";
    }

    #endregion
}

/// <summary>Alıcının girdiği ÖZEL METİN (getOrderDetail item.customTextOptionValues.customTextOptionValue) — seçenek
/// adı (option, ör. "mürekkep rengi") + değeri (text, ör. "SİYAH" ya da kaşeye yazılacak çok satırlı metin).</summary>
public class OrderDetailItemCustomText
{
    #region Constructors

    protected OrderDetailItemCustomText()
    {
    }

    public OrderDetailItemCustomText(string? option, string? text)
    {
        Option = OrderSnapshotText.Short(option);
        Text = OrderSnapshotText.Long(text);
    }

    #endregion

    #region Properties

    /// <summary>Seçenek adı (ör. "mürekkep rengi", "yazılacak yazı").</summary>
    public virtual string? Option { get; protected set; }

    /// <summary>Alıcının girdiği değer (çok satırlı olabilir — kaşe metni/adres).</summary>
    public virtual string? Text { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Option}: {Text}";
    }

    #endregion
}

/// <summary>Detay snapshot'ı için TOLERANT metin kırpma — boş→null, taşan uzunluk kırpılır (uzak veri onarım
/// felsefesi; fail-fast DEĞİL). JSON blob'da kolon-uzunluğu yok ama şişmeye karşı savunmacı sınır.</summary>
internal static class OrderSnapshotText
{
    public static string? Short(string? value)
    {
        return Clip(value, OrderConsts.DetailShortTextMaxLength);
    }

    public static string? Long(string? value)
    {
        return Clip(value, OrderConsts.DetailLongTextMaxLength);
    }

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }
}
