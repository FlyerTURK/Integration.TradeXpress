namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş kaleminin YEREL/OPERASYONEL katmanı — <see cref="OrderLine"/>/<see cref="Order.Detail"/>'den TAMAMEN
/// BAĞIMSIZ yaşar (ikisi de her senkronizasyonda silinip/bütünüyle değiştirilir — bkz. OrderSyncManager.ReplaceLinesAsync
/// + Order.SetDetail; buradaki veri resync'e DAYANIKLI). (<see cref="OrderId"/>, <see cref="RemoteLineId"/>) ile
/// eşleşir — <c>OrderLine.Id</c> DEĞİL (satır her çekimde yeniden yaratılır, id kararlı değil; RemoteLineId kanaldan
/// gelen kararlı kimliktir).
///
/// <para><b>Ürün versiyonu bağı</b> (Sipariş Fazı O1, task #57): kalem yerel <c>ProductVariant</c>'a eşleştiğinde
/// EŞLEŞME ANINDAKİ isim/görsel <see cref="ProductSnapshotName"/>/<see cref="ProductSnapshotImageUrl"/>'e DONAR —
/// ürün sonradan değişse/silinse de sipariş kalemi o ANKİ görüntüyü korur (VoucherLine felsefesi).</para>
///
/// <para><b>Alıcı metni düzeltmesi:</b> N11'den gelen orijinal <c>customText</c> (Order.Detail içinde) ASLA
/// değiştirilmez (denetim/ihtilaf kanıtı) — operatör düzeltmesi <see cref="CustomTextCorrections"/>'ta AYRI tutulur.
/// Bu entity, Order aggregate'inin İLK yazma ucudur (O0 tamamen salt-okumaydı).</para>
/// </summary>
public class OrderLineOperationalData : FullAuditedEntity<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected OrderLineOperationalData()
    {
        CustomTextCorrections = new List<OrderLineCustomTextCorrection>();
    }

    public OrderLineOperationalData(Guid companyId, Guid orderId, string remoteLineId)
    {
        SetCompanyId(companyId);
        SetOrderId(orderId);
        RemoteLineId = ClipRequired(remoteLineId, nameof(RemoteLineId), OrderConsts.RemoteLineIdMaxLength);
        CustomTextCorrections = new List<OrderLineCustomTextCorrection>();
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (Order ile aynı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Ait olduğu sipariş — id-only bağ. Set-once.</summary>
    public virtual Guid OrderId { get; protected set; }

    /// <summary>Kanal satır kimliği (kanal line id) — resync'te KARARLI kalan eşleşme anahtarı. Set-once.</summary>
    public virtual string RemoteLineId { get; protected set; } = null!;

    /// <summary>Eşleşen yerel varyant — id-only, opsiyonel (sert FK yok; yerel ürün silinse de bu kayıt bozulmaz).</summary>
    public virtual Guid? ProductVariantId { get; protected set; }

    /// <summary>Eşleşme ANINDAKİ varyant adı — ürün sonradan değişse/silinse de DONAR.</summary>
    public virtual string? ProductSnapshotName { get; protected set; }

    /// <summary>Eşleşme ANINDAKİ görsel — dış URL ya da (yüklenmiş görselse) data-URL; her ikisi de doğrudan
    /// &lt;img src&gt; ile kullanılabilir. null = eşleşme yok ya da ürünün görseli yok.</summary>
    public virtual string? ProductSnapshotImageUrl { get; protected set; }

    /// <summary>Eşleştirmenin yapıldığı an (UTC) — null = hiç eşleştirilmedi.</summary>
    public virtual DateTime? MatchedAt { get; protected set; }

    /// <summary>Alıcının özel metinlerine (customText) operatör düzeltmeleri — orijinal Order.Detail'deki metin
    /// DEĞİŞMEZ, düzeltme burada ayrı tutulur (owned JSON; Option başına TEK güncel düzeltme).</summary>
    public virtual List<OrderLineCustomTextCorrection> CustomTextCorrections { get; protected set; } = null!;

    /// <summary>Kalemin YEREL işlem durumu (Sipariş Fazı O2 — N11'e yazılan eylemler). Geçişler guard'lı:
    /// Pending→Accepted|Rejected; Accepted→Shipped. Varsayılan Pending.</summary>
    public virtual OrderLineActionStatus ActionStatus { get; protected set; } = OrderLineActionStatus.Pending;

    /// <summary>Red gerekçesi (N11'e OrderItemReject ile gönderilen serbest metin) — yalnız Rejected durumunda dolu.</summary>
    public virtual string? RejectReason { get; protected set; }

    /// <summary>Son aksiyonun (kabul/red/kargo) N11'e BAŞARIYLA bildirildiği an (UTC).</summary>
    public virtual DateTime? ActionAt { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Ürün versiyonu bağını kurar/günceller — eşleşme anının donmuş görüntüsü (otomatik eşleştirme ya
    /// da operatörün manuel düzeltmesi).</summary>
    public virtual void SetProductMatch(Guid? productVariantId, string? snapshotName, string? snapshotImageUrl, DateTime matchedAt)
    {
        ProductVariantId = productVariantId == Guid.Empty ? null : productVariantId;
        ProductSnapshotName = Clip(snapshotName, OrderConsts.ProductNameSnapshotMaxLength);
        ProductSnapshotImageUrl = snapshotImageUrl;
        MatchedAt = matchedAt;
    }

    /// <summary>Bir özel metin (customText) seçeneğine operatör düzeltmesi ekler/günceller — aynı Option varsa
    /// üzerine yazılır (son değer geçerli; tam denetim izi gerekmiyor).</summary>
    public virtual void CorrectCustomText(string option, string correctedText, DateTime correctedAt)
    {
        var normalizedOption = ClipRequired(option, nameof(option), OrderConsts.DetailShortTextMaxLength);
        var text = ClipRequired(correctedText, nameof(correctedText), OrderConsts.DetailLongTextMaxLength);

        var existing = CustomTextCorrections.FirstOrDefault(c =>
            string.Equals(c.Option, normalizedOption, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            CustomTextCorrections.Remove(existing);
        }

        CustomTextCorrections.Add(new OrderLineCustomTextCorrection(normalizedOption, text, correctedAt));
    }

    /// <summary>Bir seçeneğin düzeltmesini kaldırır (orijinal N11 metnine döner) — kayıt yoksa no-op (idempotent).</summary>
    public virtual void ClearCustomTextCorrection(string option)
    {
        var existing = CustomTextCorrections.FirstOrDefault(c =>
            string.Equals(c.Option, option, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            CustomTextCorrections.Remove(existing);
        }
    }

    /// <summary>N11'e OrderItemAccept BAŞARIYLA gönderildikten SONRA çağrılır (state machine: Pending → Accepted).
    /// Zaten Accepted/Rejected/Shipped ise fail-fast (çift-yazma/geçersiz geçiş sessizce yutulmaz).</summary>
    public virtual void MarkAccepted(DateTime at)
    {
        EnsurePending();
        ActionStatus = OrderLineActionStatus.Accepted;
        RejectReason = null;
        ActionAt = at;
    }

    /// <summary>N11'e OrderItemReject BAŞARIYLA gönderildikten SONRA çağrılır (state machine: Pending → Rejected).</summary>
    public virtual void MarkRejected(string reason, DateTime at)
    {
        EnsurePending();
        RejectReason = ClipRequired(reason, nameof(reason), OrderConsts.RejectReasonMaxLength);
        ActionStatus = OrderLineActionStatus.Rejected;
        ActionAt = at;
    }

    /// <summary>N11'e MakeOrderItemShipment BAŞARIYLA gönderildikten SONRA çağrılır (state machine: Accepted → Shipped).
    /// Kabul edilmemiş kalem kargoya verilemez (guard).</summary>
    public virtual void MarkShipped(DateTime at)
    {
        if (ActionStatus != OrderLineActionStatus.Accepted)
        {
            throw new BusinessException("TradeXpress:OrderLine:MustBeAcceptedBeforeShipment")
                .WithData("ActionStatus", ActionStatus);
        }

        ActionStatus = OrderLineActionStatus.Shipped;
        ActionAt = at;
    }

    private void EnsurePending()
    {
        if (ActionStatus != OrderLineActionStatus.Pending)
        {
            throw new BusinessException("TradeXpress:OrderLine:AlreadyActioned")
                .WithData("ActionStatus", ActionStatus);
        }
    }

    public override string ToString()
    {
        return RemoteLineId;
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

/// <summary>Alıcının özel metnine (customText) operatör düzeltmesi — hangi seçenek (Option), düzeltilmiş metin,
/// ne zaman. Orijinal N11 metni Order.Detail'de değişmeden kalır.</summary>
public class OrderLineCustomTextCorrection
{
    #region Constructors

    protected OrderLineCustomTextCorrection()
    {
    }

    public OrderLineCustomTextCorrection(string option, string correctedText, DateTime correctedAt)
    {
        Option = option;
        CorrectedText = correctedText;
        CorrectedAt = correctedAt;
    }

    #endregion

    #region Properties

    /// <summary>Hangi customText seçeneğine ait (ör. "yazılacak yazı") — Order.Detail'deki Option ile eşleşir.</summary>
    public virtual string Option { get; protected set; } = null!;

    public virtual string CorrectedText { get; protected set; } = null!;

    public virtual DateTime CorrectedAt { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Option}: {CorrectedText}";
    }

    #endregion
}
