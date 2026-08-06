namespace Integration.TradeXpress.Orders;

/// <summary>
/// SİPARİŞ KALEMİ ↔ FİŞ SATIRI BAĞI — <b>çoka-çok</b> (2026-08-05 Hakan senaryosu).
///
/// <para><b>Neden çoka-çok:</b> müşteri 23 gramlık ve 27 gramlık iki sipariş verip telefonla "tek 50 gramlık
/// yapın" derse, operatör TEK fiş satırıyla İKİ siparişi kapatır. Bire-bir bir bağ bu gerçeği taşıyamaz;
/// birini "kapanmamış" göstermek zorunda kalırdı.</para>
///
/// <para><b>Fiyat farkı ASLA TÜRETİLMEZ</b> (2026-08-05 karar #12): birleştirme müşterinin rızasıyla olur ve
/// farkın yansıtılıp yansıtılmayacağı tamamen satıcı-müşteri diyaloğuna bağlıdır. <see cref="PriceDifference"/>
/// <b>null = beyan edilmedi</b>, <b>0 = "fark yok" BEYANI</b> — ikisi farklı bilgidir ve karıştırılırsa
/// sistem hiç sorulmamış bir soruya "hayır" cevabı uydurmuş olur.</para>
///
/// <para><b>"Farklı ürün gönderildi" bayrağı YOK</b> (bilinçli): sipariş kalemi ürün VARYANTINA, fiş satırı
/// EMTİAYA işaret eder — farklı id uzayları. Kıyaslanabilir sanmak sahte kesinlik olurdu.</para>
/// </summary>
public class OrderFulfillmentLink : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected OrderFulfillmentLink()
    {
    }

    public OrderFulfillmentLink(
        Guid companyId,
        Guid orderId,
        string remoteLineId,
        Guid voucherId,
        Guid voucherLineId,
        OrderFulfillmentLinkKind kind)
    {
        SetCompanyId(companyId);
        SetOrderId(orderId);
        RemoteLineId = ClipRequired(remoteLineId, nameof(RemoteLineId), OrderConsts.RemoteLineIdMaxLength);
        SetVoucher(voucherId, voucherLineId);
        Kind = kind;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı. Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sipariş — id-only. Set-once.</summary>
    public virtual Guid OrderId { get; protected set; }

    /// <summary>Kanal satır kimliği — resync'e dayanıklı kalem anahtarı (<c>OrderLine.Id</c> DEĞİL:
    /// satır her çekimde yeniden yaratılır). Set-once.</summary>
    public virtual string RemoteLineId { get; protected set; } = null!;

    /// <summary>Fiş — id-only. Set-once.</summary>
    public virtual Guid VoucherId { get; protected set; }

    /// <summary>Fiş satırı — id-only. AYNI satır birden çok siparişe bağlanabilir (birleştirme senaryosu).</summary>
    public virtual Guid VoucherLineId { get; protected set; }

    /// <summary>Bağın türü — rezerve / fiziki çıkış / iade.</summary>
    public virtual OrderFulfillmentLinkKind Kind { get; protected set; }

    /// <summary>Bu bağın karşıladığı adet.</summary>
    public virtual decimal FulfilledQuantity { get; protected set; }

    /// <summary>Bu bağın karşıladığı miktar (madende gram).</summary>
    public virtual decimal FulfilledAmount { get; protected set; }

    /// <summary><b>null = beyan edilmedi</b> · <b>0 = "fark yok" beyanı</b>. Sistem ASLA türetmez;
    /// yalnız kullanıcı girer.</summary>
    public virtual decimal? PriceDifference { get; protected set; }

    /// <summary>Fark birimi — <see cref="PriceDifference"/> doluysa anlamlı.</summary>
    public virtual Guid? PriceDifferenceUnitId { get; protected set; }

    /// <summary>Operatör notu (ör. "müşteri rızasıyla 23+27 birleştirildi").</summary>
    public virtual string? Note { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Karşılanan miktarları ayarlar.</summary>
    public virtual void SetFulfilled(decimal quantity, decimal amount)
    {
        FulfilledQuantity = quantity;
        FulfilledAmount = amount;
    }

    /// <summary>Fiyat farkını KULLANICI BEYANI olarak kaydeder. <paramref name="difference"/> null geçilirse
    /// "beyan edilmedi"ye döner — 0 yazmak "fark yok" DEMEKTİR, ikisi karıştırılmaz.</summary>
    public virtual void DeclarePriceDifference(decimal? difference, Guid? unitId, string? note = null)
    {
        PriceDifference = difference;
        PriceDifferenceUnitId = difference is null ? null : unitId;
        if (note is not null)
        {
            Note = Clip(note, OrderConsts.DetailLongTextMaxLength);
        }
    }

    public override string ToString()
    {
        return $"{OrderId}/{RemoteLineId} → {VoucherLineId} ({Kind})";
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

    private void SetVoucher(Guid voucherId, Guid voucherLineId)
    {
        if (voucherId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(VoucherId));
        }

        if (voucherLineId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(VoucherLineId));
        }

        VoucherId = voucherId;
        VoucherLineId = voucherLineId;
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
