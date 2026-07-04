using System;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Fiş satırı panellerine AccountSelectionPanel'den geçen fiş bağlamı — 10 ayrı parametre yerine
/// tek immutable nesne. Record value-eşitliği: içerik değişmedikçe aynı bağlam sayılır.
/// </summary>
public sealed record VoucherLineContext
{
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public Guid? VaultId { get; init; }
    public Guid AccountId { get; init; }
    public Guid? SubAccountId { get; init; }
    public DateTime VoucherDate { get; init; } = BusinessClock.Now();
    public string? VoucherDescription { get; init; }

    /// <summary>Seçili fiş (null → ilk satır kaydında sunucu yeni fiş açar).</summary>
    public Guid? VoucherId { get; init; }

    /// <summary>Başlık şeridi görüntüsü için hesap kodu.</summary>
    public string? AccountCode { get; init; }

    /// <summary>Başlık şeridi görüntüsü için alt-hesap kodu.</summary>
    public string? SubAccountCode { get; init; }
}
