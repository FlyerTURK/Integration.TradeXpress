using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

/// <summary>İşlem Raporu sekmesinin kalıcı görünüm durumu (TabPageState sözleşmesi) — son ÇALIŞTIRILAN
/// sorgunun filtreleri. Restore'da filtreler dolu gelir; sorgu OTOMATİK çalıştırılmaz (maliyet kullanıcı
/// kararı — Getir'e basar).</summary>
internal sealed record TransactionReportTabState(
    DateTime Start,
    DateTime End,
    Guid? BranchId,
    Guid? VaultId,
    ProcessType? SelectedType);
