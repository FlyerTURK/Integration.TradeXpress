using System;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş satırı değişim günlüğü kaydı — denormalize skaler alanlar (grid/filtre) + tam satırın
/// (<see cref="VoucherLineDto"/>) o anki anlık görüntüsü (popup detay gösterimi; SUNUCU deserialize eder,
/// istemci JSON parse etmez).
/// </summary>
public class VoucherLineHistoryDto
{
    public Guid Id { get; set; }
    public Guid VoucherLineId { get; set; }
    public Guid VoucherId { get; set; }
    public VoucherLineChangeType ChangeType { get; set; }

    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public ProcessType ProcessType { get; set; }
    public string ProcessCode { get; set; } = string.Empty;
    public string? CommodityCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal Total { get; set; }
    public string? MainUnitCode { get; set; }
    public string? Description { get; set; }
    public Guid SubAccountId { get; set; }

    /// <summary>Değişimi yapan kullanıcı (CreatorId → ad; okuma anında çözülür).</summary>
    public Guid? CreatorId { get; set; }
    public string? CreatorName { get; set; }

    /// <summary>Değişimin tarihi/saati (ABP <c>CreationTime</c>) — ayrı "ChangedAt" alanı YOK (DRY).</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Tam satırın o anki hâli — <c>SnapshotJson</c>'dan SUNUCU deserialize eder.</summary>
    public VoucherLineDto Snapshot { get; set; } = null!;
}
