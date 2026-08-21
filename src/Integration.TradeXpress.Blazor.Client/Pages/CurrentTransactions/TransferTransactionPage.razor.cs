using System;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>/transfers sayfasının query kimliği: PushStateToUrl'in sekme URL'ine yazdığı
/// subAccountId/voucherId, sekme restore'unda RouteResolver'ın bu parametreler üzerinden geri
/// vermesiyle forma ulaşır — bunlar olmadan restore edilen transfer sekmesi kimliksiz açılıyordu.</summary>
public partial class TransferTransactionPage
{
    [Parameter]
    [SupplyParameterFromQuery(Name = "subAccountId")]
    public Guid? SubAccountId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "voucherId")]
    public Guid? VoucherId { get; set; }
}
