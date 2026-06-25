using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public string? CreatorName { get; set; }

    /// <summary>Sunucu recompute yönü (hangi alan düzenlendi) — kaydetmede kullanılır.</summary>
    public EditedField EditedField { get; set; } = EditedField.None;

    /// <summary>Bu satıra kadarki (devreden dahil) birim-bazlı yürüyen bakiye (okumada doldurulur).</summary>
    public List<VoucherBalanceLineDto> RunningBalances { get; set; } = new();
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
