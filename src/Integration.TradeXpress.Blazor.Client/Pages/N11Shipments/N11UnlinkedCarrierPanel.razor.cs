using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Base.Dtos;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.N11Shipments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Shipments;

/// <summary>
/// Carisi bağlanmamış (ÖKSÜZ) kargo firmalarını gösterip kullanıcının cari alt hesabını seçmesini sağlar.
/// <para>Akış: şablonlar N11'den içe aktarılır → şablonlarda geçen firmalardan carisi olmayanlar bu panele düşer →
/// kullanıcı kendi cari planından seçer → bağ kanaldaki TÜM şablonlara yayılır (sunucu tarafı) ve firma listeden
/// çıkar. Sistem cari ÜRETMEZ; yalnız var olanı işaret eder.</para>
/// </summary>
public partial class N11UnlinkedCarrierPanel : CrudComponentBase
{
    /// <summary>Şablonların ait olduğu N11 satış kanalı.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    /// <summary>Bağ kurulduğunda üst bileşen şablonları tazelesin (cari bilgisi şablon DTO'sunda da görünür).</summary>
    [Parameter] public EventCallback OnLinked { get; set; }

    [Inject] private IN11ShipmentTemplateAppService AppService { get; set; } = default!;
    [Inject] private ISubAccountAppService SubAccountAppService { get; set; } = default!;

    private List<N11ShipmentTemplateCompanyDto> _unlinked = new();

    /// <summary>Şirketin cari alt hesapları — kullanıcının KENDİ planı (sistem burada kayıt açmaz).</summary>
    private List<SubAccountListDto> SubAccounts { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        // Tüm alt hesaplar tek seferde: kanal başına birkaç firma sorulacağı için sayfalama gereksiz.
        var page = await SubAccountAppService.GetListAsync(new SubAccountListRequestDto { MaxResultCount = ListRequestDto.AllPages });
        SubAccounts = page.Items as List<SubAccountListDto> ?? new List<SubAccountListDto>(page.Items);

        await ReloadAsync();
    }

    /// <summary>Öksüz listesini tazeler — içe aktarımdan sonra üst bileşen de çağırabilir.</summary>
    public async Task ReloadAsync()
    {
        _unlinked = await AppService.GetUnlinkedCompaniesAsync(SalesChannelId);
        StateHasChanged();
    }

    /// <summary>Seçilen cariyi firmaya bağlar; bağ kanaldaki tüm şablonlara yayılır (sunucu). Boş seçim = bağı çöz.</summary>
    private async Task LinkAsync(N11ShipmentTemplateCompanyDto row, Guid? subAccountId)
    {
        row.SubAccountId = subAccountId;
        await AppService.LinkCompanySubAccountAsync(SalesChannelId, row.ExternalId, subAccountId);

        await ReloadAsync();
        if (OnLinked.HasDelegate)
        {
            await OnLinked.InvokeAsync();
        }
    }
}
