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

    /// <summary>İÇ KARŞI TARAF (Teyit modu): karşı taraf bir iç kasa ise onun id'si; aksi halde null.
    /// Dolu olduğunda panel kaydı POSTLAMAZ — <see cref="Confirmations.IConfirmationAppService.ProposeAsync"/>
    /// ile Teyit kurar (iki taraf kendi kaydını yazıp teyitleyene dek ledger kımıldamaz). Null = bugünkü
    /// normal cari akışı (davranış değişmez).</summary>
    public Guid? CounterpartyVaultId { get; init; }

    /// <summary>BEYAN kipi (gelen kutusundan "Kendi Girişimi Yaz"): doluysa panel yeni bir teklif AÇMAZ,
    /// bu Teyit'e alıcının KENDİ satırını yazar (<see cref="Confirmations.IConfirmationAppService.DeclareAsync"/>).
    /// Sunucu satırın gönderenin satırıyla AYNA olduğunu doğrular. Null = normal (teklif/cari) akışı.</summary>
    public Guid? DeclareConfirmationId { get; init; }
}
