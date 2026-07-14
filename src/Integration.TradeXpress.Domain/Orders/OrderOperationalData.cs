namespace Integration.TradeXpress.Orders;

/// <summary>
/// Siparişin YEREL/OPERASYONEL katmanı — <see cref="Order"/>/<see cref="Order.Detail"/>'den TAMAMEN BAĞIMSIZ yaşar
/// (Order.Detail her başarılı re-fetch'te bütünüyle DEĞİŞTİRİLİR — bkz. <c>Order.SetDetail</c>; buradaki veri
/// resync'e DAYANIKLI). <see cref="OrderId"/> ile BİREBİR eşleşir (sipariş başına tek kayıt).
///
/// <para><b>Operatör düzeltmesi felsefesi:</b> N11'den gelen orijinal alıcı/adres/kargo bilgisi
/// (<see cref="Order.Detail"/> içinde) ASLA değiştirilmez (denetim/ihtilaf kanıtı) — müşterinin yazım hatası
/// (adres/isim/telefon) ya da eksik kargo bilgisi burada AYRI bir düzeltme katmanında tutulur. Görüntü/edit
/// formu değeri = düzeltme varsa düzeltme, yoksa orijinal.</para>
///
/// <para>Kalem-başı operasyonel veri (ürün versiyonu bağı + customText düzeltmesi) <see cref="OrderLineOperationalData"/>'da
/// ayrı yaşar (RemoteLineId anahtarlı — bu entity ile KARIŞTIRILMAZ).</para>
/// </summary>
public class OrderOperationalData : FullAuditedEntity<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected OrderOperationalData()
    {
    }

    public OrderOperationalData(Guid companyId, Guid orderId)
    {
        SetCompanyId(companyId);
        SetOrderId(orderId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (Order ile aynı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Ait olduğu sipariş — id-only bağ, BİREBİR (sipariş başına tek kayıt). Set-once.</summary>
    public virtual Guid OrderId { get; protected set; }

    /// <summary>Alıcı düzeltmesi — null = düzeltme yok, orijinal (Order.Detail.Buyer) gösterilir.</summary>
    public virtual OrderOperationalParty? BuyerCorrection { get; protected set; }

    /// <summary>Fatura adresi düzeltmesi — null = düzeltme yok, orijinal gösterilir.</summary>
    public virtual OrderOperationalAddress? BillingAddressCorrection { get; protected set; }

    /// <summary>Teslimat adresi düzeltmesi — null = düzeltme yok, orijinal gösterilir.</summary>
    public virtual OrderOperationalAddress? ShippingAddressCorrection { get; protected set; }

    /// <summary>Kargo firması override'ı — null = düzeltme yok, Order.CargoProvider (kanal snapshot'ı) gösterilir.</summary>
    public virtual string? CargoProviderOverride { get; protected set; }

    /// <summary>Kargo takip no override'ı — null = düzeltme yok, Order.CargoTrackingNumber gösterilir.</summary>
    public virtual string? CargoTrackingNumberOverride { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Alıcı düzeltmesini ayarlar — TÜM alanlar boşsa düzeltme temizlenir (null; orijinale döner).</summary>
    public virtual void CorrectBuyer(string? fullName, string? email, string? tcId, string? taxId, string? taxOffice)
    {
        BuyerCorrection = OrderOperationalParty.HasAnyValue(fullName, email, tcId, taxId, taxOffice)
            ? new OrderOperationalParty(fullName, email, tcId, taxId, taxOffice)
            : null;
    }

    /// <summary>Fatura adresi düzeltmesini ayarlar — TÜM alanlar boşsa düzeltme temizlenir.</summary>
    public virtual void CorrectBillingAddress(OrderOperationalAddress? address)
    {
        BillingAddressCorrection = address is not null && address.HasAny() ? address : null;
    }

    /// <summary>Teslimat adresi düzeltmesini ayarlar — TÜM alanlar boşsa düzeltme temizlenir.</summary>
    public virtual void CorrectShippingAddress(OrderOperationalAddress? address)
    {
        ShippingAddressCorrection = address is not null && address.HasAny() ? address : null;
    }

    /// <summary>Kargo bilgisi override'ını ayarlar — boş geçilirse override temizlenir (orijinale döner).</summary>
    public virtual void OverrideCargo(string? provider, string? trackingNumber)
    {
        CargoProviderOverride = Clip(provider, OrderConsts.CargoProviderMaxLength);
        CargoTrackingNumberOverride = Clip(trackingNumber, OrderConsts.CargoTrackingNumberMaxLength);
    }

    public override string ToString()
    {
        return OrderId.ToString();
    }

    private void SetCompanyId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = value;
    }

    private void SetOrderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(OrderId));
        }

        OrderId = value;
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

    #endregion
}

/// <summary>Alıcı düzeltmesi (operatörün girdiği) — <see cref="Orders.OrderDetailParty"/> ile AYNI şekilde, ama
/// mutable/editable (owned JSON; OrderDetailParty'nin tersine bu bir düzeltme, marketplace snapshot'ı değil).</summary>
public class OrderOperationalParty
{
    #region Constructors

    protected OrderOperationalParty()
    {
    }

    public OrderOperationalParty(string? fullName, string? email, string? tcId, string? taxId, string? taxOffice)
    {
        FullName = Clip(fullName, OrderConsts.DetailShortTextMaxLength);
        Email = Clip(email, OrderConsts.DetailShortTextMaxLength);
        TcId = Clip(tcId, OrderConsts.DetailShortTextMaxLength);
        TaxId = Clip(taxId, OrderConsts.DetailShortTextMaxLength);
        TaxOffice = Clip(taxOffice, OrderConsts.DetailShortTextMaxLength);
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

    /// <summary>Herhangi bir alan doluysa true — hepsi boşsa düzeltme anlamsız (temizlenir).</summary>
    public static bool HasAnyValue(string? fullName, string? email, string? tcId, string? taxId, string? taxOffice)
    {
        return !string.IsNullOrWhiteSpace(fullName)
            || !string.IsNullOrWhiteSpace(email)
            || !string.IsNullOrWhiteSpace(tcId)
            || !string.IsNullOrWhiteSpace(taxId)
            || !string.IsNullOrWhiteSpace(taxOffice);
    }

    public override string ToString()
    {
        return FullName ?? string.Empty;
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

    #endregion
}

/// <summary>Adres düzeltmesi (operatörün girdiği) — <see cref="Orders.OrderDetailAddress"/> ile AYNI alan seti,
/// ama mutable/editable (owned JSON; bir düzeltme, marketplace snapshot'ı değil).</summary>
public class OrderOperationalAddress
{
    #region Constructors

    protected OrderOperationalAddress()
    {
    }

    public OrderOperationalAddress(
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
        FullName = Clip(fullName, OrderConsts.DetailShortTextMaxLength);
        Line = Clip(line, OrderConsts.DetailLongTextMaxLength);
        Neighborhood = Clip(neighborhood, OrderConsts.DetailShortTextMaxLength);
        District = Clip(district, OrderConsts.DetailShortTextMaxLength);
        City = Clip(city, OrderConsts.DetailShortTextMaxLength);
        PostalCode = Clip(postalCode, OrderConsts.DetailShortTextMaxLength);
        Gsm = Clip(gsm, OrderConsts.DetailShortTextMaxLength);
        TcId = Clip(tcId, OrderConsts.DetailShortTextMaxLength);
        TaxId = Clip(taxId, OrderConsts.DetailShortTextMaxLength);
        TaxOffice = Clip(taxOffice, OrderConsts.DetailShortTextMaxLength);
    }

    #endregion

    #region Properties

    public virtual string? FullName { get; protected set; }
    public virtual string? Line { get; protected set; }
    public virtual string? Neighborhood { get; protected set; }
    public virtual string? District { get; protected set; }
    public virtual string? City { get; protected set; }
    public virtual string? PostalCode { get; protected set; }
    public virtual string? Gsm { get; protected set; }
    public virtual string? TcId { get; protected set; }
    public virtual string? TaxId { get; protected set; }
    public virtual string? TaxOffice { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Doldurulmuş herhangi bir alan var mı — hepsi boşsa düzeltme anlamsız (temizlenir).</summary>
    public virtual bool HasAny()
    {
        return !string.IsNullOrWhiteSpace(FullName)
            || !string.IsNullOrWhiteSpace(Line)
            || !string.IsNullOrWhiteSpace(Neighborhood)
            || !string.IsNullOrWhiteSpace(District)
            || !string.IsNullOrWhiteSpace(City)
            || !string.IsNullOrWhiteSpace(PostalCode)
            || !string.IsNullOrWhiteSpace(Gsm)
            || !string.IsNullOrWhiteSpace(TcId)
            || !string.IsNullOrWhiteSpace(TaxId)
            || !string.IsNullOrWhiteSpace(TaxOffice);
    }

    public override string ToString()
    {
        var parts = new[] { Line, Neighborhood, District, City, PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
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

    #endregion
}
