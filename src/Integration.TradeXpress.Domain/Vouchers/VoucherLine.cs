using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş satırı — Voucher aggregate'ine ait child entity (iki-bacaklı takas modeli).
/// Ana bacak: Commodity + MainUnit + Amount/Factor/Total. Karşılık bacağı:
/// PayUnit + PayFactor/PayTotal. Kâr (Profit) ve piyasa değeri (MarketPrice)
/// snapshot'tır. Soft-delete: aktif satır setinden ABP query filter ile düşer,
/// ileride log/geçmiş için satır DB'de kalır.
/// </summary>
public class VoucherLine : CreationAuditedEntity<Guid>, ISoftDelete
{
    #region Constructors

    protected VoucherLine()
    {
    }

    public VoucherLine(Guid id, Guid voucherId, VoucherLineInput input)
        : base(id)
    {
        VoucherId = voucherId;
        Set(input);
    }

    #endregion

    #region Properties

    public virtual Guid VoucherId { get; protected set; }

    public virtual Voucher Voucher { get; protected set; } = null!;

    public virtual ProcessType Type { get; protected set; }

    public virtual ProcessDirectionType Direction { get; protected set; }

    public virtual ProcessPaymentType? PaymentType { get; protected set; }

    // ── Ana bacak ──────────────────────────────────────────────────────────────

    /// <summary>Emtia / döviz birimi Id'si (snapshot referans, FK değil).</summary>
    public virtual Guid? CommodityId { get; protected set; }

    public virtual string CommodityCode { get; protected set; } = string.Empty;

    /// <summary>Adet / miktar — N5.</summary>
    public virtual decimal Quantity { get; protected set; }

    /// <summary>İşlem miktarı — N2.</summary>
    public virtual decimal Amount { get; protected set; }

    /// <summary>Milyem / işlem çarpanı — N5 (nakitte 1).</summary>
    public virtual decimal Factor { get; protected set; }

    /// <summary>Ana bacak toplamı (genelde Amount × Factor) — N2.</summary>
    public virtual decimal Total { get; protected set; }

    public virtual Guid MainUnitId { get; protected set; }

    // ── Karşılık bacağı ──────────────────────────────────────────────────────────

    /// <summary>Satış/ödeme fiyatı (parite) — N5.</summary>
    public virtual decimal PayFactor { get; protected set; }

    /// <summary>O anki piyasa fiyatı (kâr/gösterim referansı) — N5.</summary>
    public virtual decimal MarketPrice { get; protected set; }

    /// <summary>Tahsil/tediye tutarı — N2.</summary>
    public virtual decimal PayTotal { get; protected set; }

    /// <summary>İşlemden elde edilen kâr (TL, yön bağımsız) — N2.</summary>
    public virtual decimal Profit { get; protected set; }

    public virtual Guid? PayCommodityId { get; protected set; }

    public virtual string? PayCommodityCode { get; protected set; }

    public virtual Guid? PayUnitId { get; protected set; }

    /// <summary>Karşılık biriminin işlem anındaki alış kuru snapshot'ı — N5.</summary>
    public virtual decimal PayUnitRate { get; protected set; }

    // ── Ortak ────────────────────────────────────────────────────────────────────

    public virtual DateTime? DueDate { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual bool IsDeleted { get; set; }

    #endregion

    #region Methods

    /// <summary>Değiştirilebilir alanları topluca günceller (ekle/düzenle ortak yolu).
    /// Sınıflandırma (Type) ve kapsam (VoucherId) burada değişmez.</summary>
    public virtual void Set(VoucherLineInput input)
    {
        Type             = input.Type;
        Direction        = input.Direction;
        PaymentType      = input.PaymentType;
        CommodityId      = input.CommodityId == Guid.Empty ? null : input.CommodityId;
        CommodityCode    = input.CommodityCode ?? string.Empty;
        Quantity         = input.Quantity;
        Amount           = input.Amount;
        Factor           = input.Factor;
        Total            = input.Total;
        MainUnitId       = input.MainUnitId;
        PayFactor        = input.PayFactor;
        MarketPrice      = input.MarketPrice;
        PayTotal         = input.PayTotal;
        Profit           = input.Profit;
        PayCommodityId   = input.PayCommodityId == Guid.Empty ? null : input.PayCommodityId;
        PayCommodityCode = input.PayCommodityCode;
        PayUnitId        = input.PayUnitId == Guid.Empty ? null : input.PayUnitId;
        PayUnitRate      = input.PayUnitRate;
        DueDate          = input.DueDate;
        Description      = input.Description;
    }

    #endregion
}
