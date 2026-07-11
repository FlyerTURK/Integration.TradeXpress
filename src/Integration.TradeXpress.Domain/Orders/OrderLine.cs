using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş satırı — <see cref="Order"/> aggregate'inin child'ı, KENDİ GERÇEĞİNİ TAŞIYAN bir SNAPSHOT (VoucherLine +
/// versiyon-snapshot felsefesi). Persistence açıkça yönetilir (kanal alt-entity'leri deseni): çekimde satırlar
/// silinip yeniden yazılır — <see cref="OrderId"/> id-only bağ (sert nav yok).
///
/// <para><b>Ürün-agnostik:</b> <see cref="ProductNameSnapshot"/>/<see cref="Barcode"/>/<see cref="StockCode"/>/
/// <see cref="Quantity"/>/<see cref="UnitPrice"/>/<see cref="LineTotal"/>/<see cref="RemoteLineStatus"/> HİÇBİR yerel
/// <see cref="ProductVariant"/> join'i OLMADAN tam anlamlı. <see cref="ProductVariantId"/> id-only OPSİYONEL zenginleştirme
/// (sert FK/cascade YOK): yerel ürünün silinmesi bu satırı ASLA bozmaz/orphan-silmez; yokluğu NORMAL durumdur, hata değil.</para>
/// </summary>
public class OrderLine : CreationAuditedEntity<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected OrderLine()
    {
    }

    public OrderLine(Guid companyId, Guid orderId, OrderLineSnapshot snapshot)
    {
        SetCompanyId(companyId);
        SetOrderId(orderId);
        RemoteLineId = Clip(snapshot.RemoteLineId, OrderConsts.RemoteLineIdMaxLength);
        Barcode = Clip(snapshot.Barcode, OrderConsts.BarcodeMaxLength);
        StockCode = Clip(snapshot.StockCode, OrderConsts.StockCodeMaxLength);
        ProductNameSnapshot = ClipRequired(snapshot.ProductNameSnapshot, nameof(ProductNameSnapshot), OrderConsts.ProductNameSnapshotMaxLength);
        Quantity = snapshot.Quantity;
        UnitPrice = snapshot.UnitPrice;
        LineTotal = snapshot.LineTotal;
        RemoteLineStatus = Clip(snapshot.RemoteLineStatus, OrderConsts.RemoteLineStatusMaxLength);
        ProductVariantId = snapshot.ProductVariantId == Guid.Empty ? null : snapshot.ProductVariantId;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (Order ile aynı; id-only).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Ait olduğu sipariş — id-only bağ (nav YOK; persistence açıkça yönetilir).</summary>
    public virtual Guid OrderId { get; protected set; }

    /// <summary>Uzak satır kimliği (kanal line id) — opsiyonel.</summary>
    public virtual string? RemoteLineId { get; protected set; }

    public virtual string? Barcode { get; protected set; }
    public virtual string? StockCode { get; protected set; }

    /// <summary>Satır ürün adının çekim ANINDAKİ snapshot'ı — yerel ürün olmasa da anlamlı (ZORUNLU dolu).</summary>
    public virtual string ProductNameSnapshot { get; protected set; } = null!;

    public virtual decimal Quantity { get; protected set; }
    public virtual decimal UnitPrice { get; protected set; }
    public virtual decimal LineTotal { get; protected set; }

    /// <summary>Uzak satır durumu (opsiyonel).</summary>
    public virtual string? RemoteLineStatus { get; protected set; }

    /// <summary>Yerel varyant bağı — id-only, OPSİYONEL zenginleştirme (sert FK yok). null = yerel eşleşme yok
    /// (normal durum). O1 doldurmaya çalışır; kalıcı null kabul edilebilir son durumdur.</summary>
    public virtual Guid? ProductVariantId { get; protected set; }

    #endregion

    #region Methods

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
