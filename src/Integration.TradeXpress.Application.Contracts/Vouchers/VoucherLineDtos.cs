using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş satırı — tek DTO; list/get/save/edit hepsinde kullanılır.
/// <para>Kaydetme: <see cref="VoucherId"/> yoksa fiş lazy oluşturulur (header alanları + numara);
/// <see cref="Id"/> (LineId) yoksa yeni satır eklenir, varsa güncellenir. Cash'te ekrandaki
/// değerler aynen kaydedilir (WYSIWYG). Okuma: snapshot kodlar + yürüyen bakiye doldurulur.</para>
/// </summary>
public class VoucherLineDto
{
    // ── Kimlik ──
    public Guid Id { get; set; }            // LineId (yeni satırda boş)
    public Guid? VoucherId { get; set; }
    public long VoucherNumber { get; set; }

    /// <summary>Fişin ConcurrencyStamp'i (okuma anındaki). Güncelleme/silmede sunucu bunu mevcutla karşılaştırır —
    /// fiş arada başkası tarafından değiştiyse kayıt reddedilir (sessiz last-writer-wins yerine açık uyarı).</summary>
    public string? VoucherConcurrencyStamp { get; set; }

    // ── Fiş başlığı (lazy create için; okuma sonuçlarında boş olabilir) ──
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? VaultId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? SubAccountId { get; set; }
    public DateTime VoucherDate { get; set; } = DateTime.Now;
    [StringLength(VoucherConsts.DescriptionMaxLength)]
    public string? VoucherDescription { get; set; }

    // ── Sınıflandırma ──
    public ProcessType Type { get; set; }
    public ProcessDirectionType Direction { get; set; }
    public ProcessPaymentType? PaymentType { get; set; }

    /// <summary>İşlem kısa kodu (süreç+yön+ödeme harfleri, ör. "NGP").</summary>
    public string ProcessCode => VoucherProcessCode.Of(Type, Direction, PaymentType);

    // ── Ana bacak ──
    public Guid? CommodityId { get; set; }
    [StringLength(VoucherConsts.CommodityCodeMaxLength)]
    public string CommodityCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal Factor { get; set; }
    public decimal Total { get; set; }
    public Guid MainUnitId { get; set; }
    /// <summary>Ana birimin kodu (okumada MainUnitId'den çözülür; DB'de saklanmaz).</summary>
    public string? MainUnitCode { get; set; }

    // ── Karşılık bacağı ──
    public Guid? PayCommodityId { get; set; }
    [StringLength(VoucherConsts.CommodityCodeMaxLength)]
    public string? PayCommodityCode { get; set; }
    public Guid? PayUnitId { get; set; }
    /// <summary>Karşılık biriminin kodu (okumada PayUnitId'den çözülür; DB'de saklanmaz).</summary>
    public string? PayUnitCode { get; set; }
    public decimal PayFactor { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal PayTotal { get; set; }
    public decimal PayUnitRate { get; set; }
    public decimal Profit { get; set; }

    public DateTime? DueDate { get; set; }
    [StringLength(VoucherConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // ── VİRMAN (Transfer) — ProcessType.Transfer satırına özel (diğer tiplerde null) ──
    /// <summary>Karşı taraf alt hesabı (SubAccount) — karşı bacak bu hesabın kendi voucher'ında açılır.</summary>
    public Guid? CounterAccountId { get; set; }
    /// <summary>Çift bacağı bağlayan ortak kimlik (legacy RefNo) — sunucu atar, istemciden gelene güvenilmez.</summary>
    public Guid? LinkId { get; set; }
    /// <summary>Karşı alt hesabın kodu (okumada CounterAccountId'den çözülür; DB'de saklanmaz) — grid gösterimi.</summary>
    public string? CounterAccountCode { get; set; }

    // ── TAKOZ (Bullion) — ProcessType.Bullion satırına özel (diğer tiplerde null) ──
    public BullionType? BullionType { get; set; }
    public Guid? AssayOfficeId { get; set; }
    public string? ReportNo { get; set; }
    public bool? IsReport { get; set; }
    public bool? IsExtra { get; set; }
    public decimal? AssayAmount { get; set; }
    public decimal? SilverFactor { get; set; }
    public decimal? PlatinumFactor { get; set; }
    public decimal? PalladiumFactor { get; set; }
    public MetalDisposition? SilverMode { get; set; }
    public MetalDisposition? PlatinumMode { get; set; }
    public MetalDisposition? PalladiumMode { get; set; }
    public BullionLaborMode? LaborMode { get; set; }
    public decimal? SilverLaborRate { get; set; }
    public decimal? PlatinumLaborRate { get; set; }
    public decimal? PalladiumLaborRate { get; set; }
    public Guid? GoldLaborUnitId { get; set; }
    public Guid? SilverLaborUnitId { get; set; }
    public Guid? PlatinumLaborUnitId { get; set; }
    public Guid? PalladiumLaborUnitId { get; set; }
    public Guid? SilverUnitId { get; set; }
    public Guid? PlatinumUnitId { get; set; }
    public Guid? PalladiumUnitId { get; set; }
    public decimal? GoldRate { get; set; }
    public decimal? SilverRate { get; set; }
    public decimal? PlatinumRate { get; set; }
    public decimal? PalladiumRate { get; set; }
    public decimal? GoldLaborUnitRate { get; set; }
    public decimal? SilverLaborUnitRate { get; set; }
    public decimal? PlatinumLaborUnitRate { get; set; }
    public decimal? PalladiumLaborUnitRate { get; set; }

    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public string? CreatorName { get; set; }

    /// <summary>Sunucu recompute yönü (hangi alan düzenlendi) — kaydetmede kullanılır.</summary>
    public EditedField EditedField { get; set; } = EditedField.None;

    /// <summary>Bu satıra kadarki (devreden dahil) birim-bazlı yürüyen bakiye (okumada doldurulur).</summary>
    public List<VoucherBalanceLineDto> RunningBalances { get; set; } = new();
}

/// <summary>Hesap ekstresi — devreden (dönem öncesi birim-bazlı net) + dönem satırları (yürüyen bakiyeli) + kapanış.
/// <para>Kapanış = son satırın yürüyen bakiyesi; dönemde satır yoksa devredene eşittir. Tip filtresi verilmişse
/// devreden/kapanış da AYNI filtreyle hesaplanır (filtreli ekstre kendi içinde tutarlı yürür).</para></summary>
public class AccountStatementDto
{
    /// <summary>Devreden: başlangıç tarihinden ÖNCEKİ satırların birim-bazlı net bakiyesi.</summary>
    public List<VoucherBalanceLineDto> OpeningBalances { get; set; } = new();

    /// <summary>Dönem satırları — kronolojik, yürüyen bakiyeli (devreden dahil).</summary>
    public List<VoucherLineDto> Lines { get; set; } = new();

    /// <summary>Kapanış (son durum): dönem sonundaki birim-bazlı net bakiye.</summary>
    public List<VoucherBalanceLineDto> ClosingBalances { get; set; } = new();
}

/// <summary>Cari bakiye sonucu — hesabın bakiye para birimi (konsolide hedefi) + birim-bazlı satırlar.</summary>
public class AccountBalanceDto
{
    /// <summary>Hesabın (Account) bakiye para birimi — konsolide toplam bu cinsten gösterilir.</summary>
    public Guid BalanceUnitId { get; set; }
    public string BalanceCode { get; set; } = string.Empty;

    public List<VoucherBalanceLineDto> Lines { get; set; } = new();
}

/// <summary>Cari bakiye satırı — birim bazında net (Net&gt;0 alacak, &lt;0 borç). Anlık hesaplanır.</summary>
public class VoucherBalanceLineDto
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;

    /// <summary>İşaretli net bakiye: &gt;0 ALACAK, &lt;0 BORÇ.</summary>
    public decimal Net { get; set; }

    public decimal Credit => Net > 0 ? Net : 0m;
    public decimal Debt   => Net < 0 ? -Net : 0m;
}
