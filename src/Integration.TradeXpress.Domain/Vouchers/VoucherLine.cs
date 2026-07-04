using System;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Conventions;
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

    [AllowNavigation("Aggregate-içi child→root: VoucherLine, Voucher aggregate'inin parçası (inverse: Voucher.Lines).")]
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

    /// <summary>Vade tarihi — <b>date-only</b> (saat taşımaz; DxDateEdit gün seçer).
    /// <para><b>Wall-clock (kaymasız):</b> <c>[DisableDateTimeNormalization]</c> ile ABP <c>IClock</c> (UTC) bu değeri
    /// UTC'ye çevirmez; giriş <see cref="BusinessClock.Today"/> ile Kind=Unspecified gelir, <see cref="Set"/>
    /// <see cref="BusinessClock.AsBusinessDate"/> ile günü sabitler → dönem/vade karşılaştırmaları gün kaymaz.</para></summary>
    [DisableDateTimeNormalization]
    public virtual DateTime? DueDate { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual bool IsDeleted { get; set; }

    // ── VİRMAN (Transfer) — ProcessType.Transfer satırına özel (diğer tiplerde null) ──

    /// <summary>Karşı taraf alt hesabı (SubAccount) — id-only referans (legacy StokId karşılığı).
    /// Karşı bacak bu alt hesabın KENDİ voucher'ında açılır (fiş = tek cari kuralı).</summary>
    public virtual Guid? CounterAccountId { get; protected set; }

    /// <summary>Çift bacağı bağlayan ortak bağlantı kimliği (legacy RefNo karşılığı) —
    /// iki zıt yönlü satır aynı LinkId'yi taşır; güncelleme/silme ikizini bu kimlikle bulur.</summary>
    public virtual Guid? LinkId { get; protected set; }

    // ── TAKOZ (Bullion) — ProcessType.Bullion satırına özel (diğer tiplerde null) ───────
    // Ana metal = Factor(=altın milyemi) @ MainUnitId; altın işçilik = PayFactor @ PayUnitId.
    // Yan metaller (gümüş/platin/paladyum) + işçilikleri + dağıtım durumları + kur snapshot'ları.

    public virtual BullionType? BullionType { get; protected set; }
    public virtual Guid? AssayOfficeId { get; protected set; }
    public virtual string? ReportNo { get; protected set; }
    public virtual bool? IsReport { get; protected set; }
    public virtual bool? IsExtra { get; protected set; }
    /// <summary>Çeşni numune miktarı (girişte cari bakiyeye dahil).</summary>
    public virtual decimal? AssayAmount { get; protected set; }

    // Yan metal milyemleri
    public virtual decimal? SilverFactor { get; protected set; }
    public virtual decimal? PlatinumFactor { get; protected set; }
    public virtual decimal? PalladiumFactor { get; protected set; }

    // Dağıtım durumları (Madeni Ver / Altına Çevir / İşçilikten Düş / Madeni Bırak) + işçilik tahsil şekli
    public virtual MetalDisposition? SilverMode { get; protected set; }
    public virtual MetalDisposition? PlatinumMode { get; protected set; }
    public virtual MetalDisposition? PalladiumMode { get; protected set; }
    public virtual BullionLaborMode? LaborMode { get; protected set; }

    // İşçilik fiyatları (altın = PayFactor; gümüş/platin/paladyum yeni — PT/PD ERPPROV3'te YOK, eklendi)
    public virtual decimal? SilverLaborRate { get; protected set; }
    public virtual decimal? PlatinumLaborRate { get; protected set; }
    public virtual decimal? PalladiumLaborRate { get; protected set; }

    // İşçilik birimleri (edit-load için)
    public virtual Guid? GoldLaborUnitId { get; protected set; }
    public virtual Guid? SilverLaborUnitId { get; protected set; }
    public virtual Guid? PlatinumLaborUnitId { get; protected set; }
    public virtual Guid? PalladiumLaborUnitId { get; protected set; }

    // Yan metal bacak birimleri (gümüş/platin/paladyum bakiyesi hangi birime postlanır)
    public virtual Guid? SilverUnitId { get; protected set; }
    public virtual Guid? PlatinumUnitId { get; protected set; }
    public virtual Guid? PalladiumUnitId { get; protected set; }

    // Kur snapshot'ları (kayıt anında dondurulur — poster ek kur okuması YAPMAZ)
    public virtual decimal? GoldRate { get; protected set; }
    public virtual decimal? SilverRate { get; protected set; }
    public virtual decimal? PlatinumRate { get; protected set; }
    public virtual decimal? PalladiumRate { get; protected set; }
    public virtual decimal? GoldLaborUnitRate { get; protected set; }
    public virtual decimal? SilverLaborUnitRate { get; protected set; }
    public virtual decimal? PlatinumLaborUnitRate { get; protected set; }
    public virtual decimal? PalladiumLaborUnitRate { get; protected set; }

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
        // Date-only wall-clock: saat atılır + Kind=Unspecified (giriş Local/Utc gelse bile vade günü kaymaz).
        DueDate          = input.DueDate is { } due ? BusinessClock.AsBusinessDate(due) : null;
        Description      = input.Description;

        // ── Virman (Transfer) alanları ──
        CounterAccountId = input.CounterAccountId == Guid.Empty ? null : input.CounterAccountId;
        LinkId           = input.LinkId == Guid.Empty ? null : input.LinkId;

        // ── Takoz (Bullion) alanları ──
        BullionType           = input.BullionType;
        AssayOfficeId         = input.AssayOfficeId == Guid.Empty ? null : input.AssayOfficeId;
        ReportNo              = input.ReportNo;
        IsReport              = input.IsReport;
        IsExtra               = input.IsExtra;
        AssayAmount           = input.AssayAmount;
        SilverFactor          = input.SilverFactor;
        PlatinumFactor        = input.PlatinumFactor;
        PalladiumFactor       = input.PalladiumFactor;
        SilverMode            = input.SilverMode;
        PlatinumMode          = input.PlatinumMode;
        PalladiumMode         = input.PalladiumMode;
        LaborMode             = input.LaborMode;
        SilverLaborRate       = input.SilverLaborRate;
        PlatinumLaborRate     = input.PlatinumLaborRate;
        PalladiumLaborRate    = input.PalladiumLaborRate;
        GoldLaborUnitId       = input.GoldLaborUnitId;
        SilverLaborUnitId     = input.SilverLaborUnitId;
        PlatinumLaborUnitId   = input.PlatinumLaborUnitId;
        PalladiumLaborUnitId  = input.PalladiumLaborUnitId;
        SilverUnitId          = input.SilverUnitId;
        PlatinumUnitId        = input.PlatinumUnitId;
        PalladiumUnitId       = input.PalladiumUnitId;
        GoldRate              = input.GoldRate;
        SilverRate            = input.SilverRate;
        PlatinumRate          = input.PlatinumRate;
        PalladiumRate         = input.PalladiumRate;
        GoldLaborUnitRate     = input.GoldLaborUnitRate;
        SilverLaborUnitRate   = input.SilverLaborUnitRate;
        PlatinumLaborUnitRate = input.PlatinumLaborUnitRate;
        PalladiumLaborUnitRate = input.PalladiumLaborUnitRate;
    }

    #endregion
}
