using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.AddOns;

/// <summary>AddOn edit host code-behind — coordinator kurulumu + yeni-kayıt varsayılanları + para birimi lookup
/// verisi (host yükler; DUMB layout servis çağırmaz; inline döviz ekle/düzelt sonrası tazelenir).</summary>
public partial class AddOnEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IAddOnAppService AddOnAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;
    [Inject] protected ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;

    private ICommitCoordinator<AddOnGetDto, AddOnListDto, Guid, AddOnListRequestDto>? _coordinator;
    private bool _ready;

    // Para birimi lookup verisi — açılışta bir kez yüklenir; inline ekle/düzelt sonrası ReloadCurrencyUnitsAsync tazeler.
    protected IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; private set; } = Array.Empty<CurrencyUnitListDto>();

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<AddOnGetDto, AddOnListDto, Guid, AddOnListRequestDto, AddOnCreateDto, AddOnUpdateDto>(
            AddOnAppService, Mapper);
        CurrencyUnits = await CurrencyLookup.GetAsync();
        _ready = true;
    }

    // Inline döviz ekle/düzelt sonrası lookup listesini tazeler (yeni birim anında combo'ya düşsün).
    private async Task ReloadCurrencyUnitsAsync()
    {
        CurrencyLookup.Invalidate();
        CurrencyUnits = await CurrencyLookup.GetAsync();
        StateHasChanged();
    }

    // Yeni kayıt varsayılanları: aktif + sıra 1 (fiyat 0 = ücretsiz).
    private static void ApplyNew(AddOnGetDto model)
    {
        model.IsActive = true;
        model.DisplayOrder = 1;
    }
}
