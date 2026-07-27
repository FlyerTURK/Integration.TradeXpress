using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Cari işlemler / virman sekmesinin kalıcı GÖRÜNÜM durumu (TabPageState sözleşmesi):
/// sekme geri yüklendiğinde liste kipi + tarih aralığı + tip filtresi + bakiye kapsamı aynen döner.
/// Kimlik (subAccountId/voucherId) burada DEĞİL — o sekme URL'sinde yaşar (PushStateToUrl).</summary>
internal sealed record CurrentTransactionTabState(
    bool ListMode,
    DateTime ListStart,
    DateTime ListEnd,
    ProcessType[] ListTypes,
    BalanceViewMode BalanceViewMode);
