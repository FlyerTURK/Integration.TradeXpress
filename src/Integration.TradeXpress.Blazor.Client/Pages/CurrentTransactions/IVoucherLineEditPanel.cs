using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Düzelt akışında fiş satırını yükleyebilen süreç panellerinin ortak sözleşmesi.
/// AccountSelectionPanel bu arayüz üzerinden tip-bazlı dispatch yapar (12'li else-if zinciri yerine).</summary>
internal interface IVoucherLineEditPanel
{
    /// <summary>Var olan fiş satırını panele düzenleme (UPDATE) modunda yükler.</summary>
    Task LoadForEditAsync(VoucherLineDto dto);
}
