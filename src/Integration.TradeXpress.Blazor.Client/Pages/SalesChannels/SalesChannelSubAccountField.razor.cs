using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.TradeXpress.Accounts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

public partial class SalesChannelSubAccountField
{
    [Parameter] public Guid? Value { get; set; }

    [Parameter] public EventCallback<Guid?> ValueChanged { get; set; }

    [Inject] private ISubAccountAppService SubAccountAppService { get; set; } = default!;

    private List<SubAccountListDto> _subAccounts = new();

    protected override async Task OnInitializedAsync()
    {
        // Alt hesap sayısı şirket başına yönetilebilir ölçekte; tamamı bir kez çekilip lookup'a verilir
        // (kanal formu tek seferlik açılır, sayfalı arama karmaşıklığına değmez).
        var page = await SubAccountAppService.GetListAsync(
            new SubAccountListRequestDto { MaxResultCount = ListRequestDto.AllPages });

        _subAccounts = page.Items as List<SubAccountListDto> ?? new List<SubAccountListDto>(page.Items);
    }

    private async Task OnChangedAsync(Guid? subAccountId)
    {
        Value = subAccountId;
        await ValueChanged.InvokeAsync(subAccountId);
    }
}
