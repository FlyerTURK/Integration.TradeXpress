namespace Integration.TradeXpress.Orders;

/// <summary>
/// SİPARİŞ REZERVASYONU — siparişin stok üzerindeki tutamağı (2026-08-05 Hakan kararları; Faz 7).
///
/// <para><b>Kapatılan delik:</b> sipariş bugüne kadar stoğa HİÇ dokunmuyordu. Sipariş ile fiş arası boyunca
/// aynı maden hem satılmış hem satılabilir görünüyordu — sistemdeki en geniş aşırı satış kapısı buydu.</para>
///
/// <para><b>Yeni altyapı GEREKMEDİ:</b> rezervasyon bir FİŞTİR (<c>ProcessPaymentType.Reservation</c>).
/// Fiziksel Net'e girmez, <c>ReservedOut</c>'a yazılır, <c>Available = Net − ReservedOut</c> zinciri
/// satılabilir adet hesabını zaten besliyor ve fiş yazımı <c>CommodityStockChangedEto</c>'yu kendiliğinden
/// ateşliyor. Bu entity yalnız siparişle fiş arasındaki BAĞI ve iki ekseni tutar.</para>
///
/// <para><b>İKİ EKSEN, bilerek ayrı:</b> <see cref="Status"/> malın fiziksel yolculuğu,
/// <see cref="CancellationDecision"/> insan kararı. Kanaldan iptal talebi gelince YALNIZ ikinci eksen
/// hareket eder; maden karar verilene kadar tutulur.</para>
///
/// <para><b>⛔ Zaman aşımı YOK</b> — <i>"sipariş siparıştir"</i>. <c>Expire*</c>/<c>*Timeout*</c> adlı üye
/// eklemek mekanik olarak yasaktır (<c>OrderReservationConventionTests</c>).</para>
///
/// <para>Sipariş başına TEK kayıt (<see cref="OrderId"/> birebir). <c>Order</c>/<c>OrderLine</c> her
/// senkronizasyonda silinip yeniden yazıldığından bu katman AYRI yaşar — <c>OrderOperationalData</c> deseni.</para>
/// </summary>
public class OrderReservation : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected OrderReservation()
    {
    }

    public OrderReservation(Guid companyId, Guid orderId)
    {
        SetCompanyId(companyId);
        SetOrderId(orderId);
        Status = OrderReservationStatus.Blocked;
        CancellationDecision = OrderCancellationDecision.None;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (Order ile aynı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Ait olduğu sipariş — id-only bağ, BİREBİR. Set-once.</summary>
    public virtual Guid OrderId { get; protected set; }

    /// <summary>Stok ekseni. Başlangıç <see cref="OrderReservationStatus.Blocked"/> — <b>fail-closed</b>:
    /// kayıt açılıp fiş yazılamadıysa "rezerve edildi" YALANI söylenmez.</summary>
    public virtual OrderReservationStatus Status { get; protected set; }

    /// <summary>İptal ekseni — insan kararı. Stok eksenini KENDİLİĞİNDEN hareket ettirmez.</summary>
    public virtual OrderCancellationDecision CancellationDecision { get; protected set; }

    /// <summary>Rezervasyon fişi — id-only (FK yok; fiş silinse de bu kayıt bozulmaz).
    /// null = fiş hiç yazılamadı (<see cref="OrderReservationStatus.Blocked"/>).</summary>
    public virtual Guid? VoucherId { get; protected set; }

    /// <summary>Rezervasyonun kurulduğu an (UTC).</summary>
    public virtual DateTime? ReservedAt { get; protected set; }

    /// <summary>Rezervasyonun serbest bırakıldığı an (UTC).</summary>
    public virtual DateTime? ReleasedAt { get; protected set; }

    /// <summary>Kanaldan iptal talebinin geldiği an (UTC).</summary>
    public virtual DateTime? CancellationRequestedAt { get; protected set; }

    /// <summary>İptal kararının verildiği an (UTC).</summary>
    public virtual DateTime? CancellationDecidedAt { get; protected set; }

    /// <summary>Kararı veren kullanıcı — id-only.</summary>
    public virtual Guid? CancellationDecidedBy { get; protected set; }

    /// <summary>Kurulamama gerekçesi (<see cref="OrderReservationStatus.Blocked"/>) ya da karar notu.
    /// Gelen kutusunda kullanıcıya gösterilir — sessiz atlama olmaz.</summary>
    public virtual string? Note { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Rezervasyon fişi yazıldı → <see cref="OrderReservationStatus.Reserved"/>.</summary>
    public virtual void MarkReserved(Guid voucherId, DateTime at)
    {
        if (voucherId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(VoucherId));
        }

        VoucherId = voucherId;
        Status = OrderReservationStatus.Reserved;
        ReservedAt = at;
        ReleasedAt = null;
        Note = null;
    }

    /// <summary>Rezervasyon KURULAMADI — gerekçesiyle. Sessizce atlanmaz; gelen kutusuna düşer.</summary>
    public virtual void MarkBlocked(string reason)
    {
        Status = OrderReservationStatus.Blocked;
        Note = Clip(reason, OrderConsts.DetailLongTextMaxLength);
    }

    /// <summary>Fiziki çıkış yapıldı → <see cref="OrderReservationStatus.Fulfilled"/>. Dönüşü olmayan nokta:
    /// bundan sonra iptal REDDEDİLİR (artık iade sürecidir).</summary>
    public virtual void MarkFulfilled(DateTime at)
    {
        if (Status != OrderReservationStatus.Reserved)
        {
            throw new BusinessException("TradeXpress:OrderReservation:MustBeReservedToFulfill")
                .WithData("Status", Status);
        }

        Status = OrderReservationStatus.Fulfilled;
        ReleasedAt = at;
    }

    /// <summary>Rezervasyonu serbest bırakır (fiş satırları soft-delete edildikten SONRA çağrılır).
    /// <para><b>Fulfilled'dan serbest bırakma YOK:</b> mal çıktıysa geri dönüş iade sürecidir; rezervasyonu
    /// "serbest" saymak stoğu iki kez geri verirdi.</para></summary>
    public virtual void MarkReleased(DateTime at, string? reason = null)
    {
        if (Status == OrderReservationStatus.Fulfilled)
        {
            throw new BusinessException("TradeXpress:OrderReservation:CannotReleaseFulfilled");
        }

        Status = OrderReservationStatus.Released;
        ReleasedAt = at;
        if (reason is not null)
        {
            Note = Clip(reason, OrderConsts.DetailLongTextMaxLength);
        }
    }

    /// <summary>Kanaldan iptal talebi geldi → karar BEKLER. <b>Stok eksenine DOKUNMAZ</b> — maden tutulmaya
    /// devam eder (mal hazırlanmış olabilir; bunu yalnız kullanıcı bilir).</summary>
    public virtual void RequestCancellation(DateTime at)
    {
        // Karar zaten verilmişse yeniden "bekliyor"a düşürme: operatörün verdiği karar kanalın tekrar eden
        // bildirimiyle sessizce geri alınamaz.
        if (CancellationDecision is OrderCancellationDecision.Approved or OrderCancellationDecision.Rejected)
        {
            return;
        }

        // ZATEN BEKLİYORSA DOKUNMA — İLK talep anı korunur. Senkron worker'ı 2 dakikada bir aynı siparişle
        // döndüğü için CancellationRequestedAt her turda tazelenirdi; o zaman "ne zamandır karar bekliyor?" sorusunun cevabı
        // DAİMA "2 dakikadır" olurdu ve bekleyen işi önceliklendirmek imkânsızlaşırdı.
        if (CancellationDecision == OrderCancellationDecision.Pending)
        {
            return;
        }

        CancellationDecision = OrderCancellationDecision.Pending;
        CancellationRequestedAt = at;
    }

    /// <summary>Kullanıcı iptali ONAYLADI. Çıkış yapılmışsa BLOKLANIR — artık iade sürecidir.
    /// <para>Serbest bırakma AYRI adımdır (fiş satırlarının soft-delete'i) — bu metot yalnız KARARI kaydeder;
    /// karar ile fiziksel etkiyi tek çağrıda birleştirmek, yarıda kalan bir işlemde defteri kararla
    /// tutarsız bırakırdı.</para></summary>
    public virtual void ApproveCancellation(Guid? decidedBy, DateTime at, string? note = null)
    {
        if (Status == OrderReservationStatus.Fulfilled)
        {
            throw new BusinessException("TradeXpress:OrderReservation:AlreadyFulfilled");
        }

        CancellationDecision = OrderCancellationDecision.Approved;
        CancellationDecidedBy = decidedBy;
        CancellationDecidedAt = at;
        if (note is not null)
        {
            Note = Clip(note, OrderConsts.DetailLongTextMaxLength);
        }
    }

    /// <summary>Kullanıcı iptali REDDETTİ — rezervasyon tutulmaya devam eder.</summary>
    public virtual void RejectCancellation(Guid? decidedBy, DateTime at, string? note = null)
    {
        CancellationDecision = OrderCancellationDecision.Rejected;
        CancellationDecidedBy = decidedBy;
        CancellationDecidedAt = at;
        if (note is not null)
        {
            Note = Clip(note, OrderConsts.DetailLongTextMaxLength);
        }
    }

    public override string ToString()
    {
        return $"{OrderId}:{Status}";
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
