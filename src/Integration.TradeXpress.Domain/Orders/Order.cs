using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// NÖTR sipariş aggregate'i — TÜM satış kanallarının siparişleri buraya map olur (kanal yalnız discriminator:
/// <see cref="SalesChannelId"/> + <see cref="ChannelType"/>; kanal-başına ayrı tablo/panel YOKTUR). Ortak sipariş
/// panelinin bel kemiği: tek grid + kanal/durum/tarih filtresi bu tek tip üzerinden çalışır.
///
/// <para><b>SALT-OKUMA çekim (Sipariş Fazı O0):</b> pazaryerinden GET ile çekilir + bu tabloya idempotent upsert
/// edilir. FİŞ YOK, REZERVASYON YOK, STOK HAREKETİ YOK, pazaryerine YAZMA YOK — yalnız görüntüleme. Alanlar
/// pazaryerinden gelen SNAPSHOT'lardır (VoucherLine felsefesi): yerel ürün/kanal silinse bile sipariş sağ kalır.</para>
///
/// <para><b>İdempotency anahtarı</b> (<see cref="SalesChannelId"/>, <see cref="RemoteOrderId"/>): ikinci çekim
/// durumu/satırları GÜNCELLER, dublike üretmez. <b>Company-owned</b> (<see cref="ICompanyOwned"/>, non-nullable
/// <see cref="CompanyId"/>) + per-tenant (<see cref="IMultiTenant"/>) — SalesChannel deseni.</para>
/// </summary>
public class Order : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Order()
    {
    }

    public Order(
        Guid companyId,
        Guid salesChannelId,
        SalesChannelType channelType,
        string remoteOrderId,
        string orderNumber)
    {
        SetCompanyId(companyId);
        SetSalesChannel(salesChannelId, channelType);
        RemoteOrderId = ClipRequired(remoteOrderId, nameof(RemoteOrderId), OrderConsts.RemoteOrderIdMaxLength);
        OrderNumber = ClipRequired(orderNumber, nameof(OrderNumber), OrderConsts.OrderNumberMaxLength);
        NeutralStatus = OrderStatus.Unknown;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Oluşturmadan sonra değişmez (set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Siparişin geldiği satış kanalı — id-only referans (nav YOK; aggregate'ler arası). Set-once.</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + filtre. Set-once.</summary>
    public virtual SalesChannelType ChannelType { get; protected set; }

    /// <summary>Kanaldaki uzak sipariş kimliği — idempotency anahtarı (SalesChannelId ile birlikte tekil). Değişmez.</summary>
    public virtual string RemoteOrderId { get; protected set; } = null!;

    /// <summary>İnsan-okunur sipariş numarası (kanal orderNumber).</summary>
    public virtual string OrderNumber { get; protected set; } = null!;

    /// <summary>Sipariş tarihi — UTC saklanır (zaman damgası; iş-tarihi DEĞİL → normalizasyon-muafiyeti gerekmez).
    /// Görüntü katmanı kullanıcı yerel saatine çevirir.</summary>
    public virtual DateTime OrderDate { get; protected set; }

    /// <summary>Nötr (kanal-agnostik) durum — ortak filtre/görüntü.</summary>
    public virtual OrderStatus NeutralStatus { get; protected set; }

    /// <summary>Ham kanal durumu (Created/Shipped ...) — nötr eşlemenin kaynağı, denetim için saklanır.</summary>
    public virtual string? RemoteStatus { get; protected set; }

    /// <summary>Müşteri görünen adı (maskeli/kısa — tam kimlik saklanmaz).</summary>
    public virtual string? CustomerName { get; protected set; }

    /// <summary>Sipariş tutarı (kanal para birimi = tipik TRY).</summary>
    public virtual decimal TotalAmount { get; protected set; }

    /// <summary>Tutarın para birimi — id-only (TRY host çözümü); null = çözülemedi (yerel birim varsayımı).</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Kargo firması (opsiyonel).</summary>
    public virtual string? CargoProvider { get; protected set; }

    /// <summary>Kargo takip numarası (opsiyonel).</summary>
    public virtual string? CargoTrackingNumber { get; protected set; }

    /// <summary>Bu kaydın pazaryerinden en son çekildiği an (UTC) — çekim tazeliği göstergesi.</summary>
    public virtual DateTime FetchedAt { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Uzak snapshot'ı (değişebilen alanlar) TOPLU günceller — ikinci çekimin idempotent güncelleme yolu.
    /// Kimlik/kapsam alanları (Company/SalesChannel/RemoteOrderId) burada değişmez.</summary>
    public virtual void ApplyRemote(
        string orderNumber,
        DateTime orderDate,
        OrderStatus neutralStatus,
        string? remoteStatus,
        string? customerName,
        decimal totalAmount,
        Guid? currencyUnitId,
        string? cargoProvider,
        string? cargoTrackingNumber,
        DateTime fetchedAt)
    {
        OrderNumber = ClipRequired(orderNumber, nameof(OrderNumber), OrderConsts.OrderNumberMaxLength);
        OrderDate = orderDate;
        NeutralStatus = neutralStatus;
        RemoteStatus = Clip(remoteStatus, OrderConsts.RemoteStatusMaxLength);
        CustomerName = Clip(customerName, OrderConsts.CustomerNameMaxLength);
        TotalAmount = totalAmount;
        CurrencyUnitId = currencyUnitId == Guid.Empty ? null : currencyUnitId;
        CargoProvider = Clip(cargoProvider, OrderConsts.CargoProviderMaxLength);
        CargoTrackingNumber = Clip(cargoTrackingNumber, OrderConsts.CargoTrackingNumberMaxLength);
        FetchedAt = fetchedAt;
    }

    public override string ToString()
    {
        return OrderNumber;
    }

    private void SetCompanyId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = value;
    }

    private void SetSalesChannel(Guid salesChannelId, SalesChannelType channelType)
    {
        if (salesChannelId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelId));
        }

        SalesChannelId = salesChannelId;
        ChannelType = channelType;
    }

    /// <summary>Uzak metin snapshot'ı: boş → null; taşan uzunluk KIRPILIR (fail-fast yerine onarım — uzak veri bizim
    /// kontrolümüzde değil; kaydı kaybetmek daha kötü, import onarım felsefesiyle aynı).</summary>
    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    /// <summary>Zorunlu uzak metin: boşsa tipli exception (çağıran fallback sağlamalı), doluysa kırpılır.</summary>
    private static string ClipRequired(string? value, string propertyName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RequiredPropertyException(propertyName);
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    #endregion
}
