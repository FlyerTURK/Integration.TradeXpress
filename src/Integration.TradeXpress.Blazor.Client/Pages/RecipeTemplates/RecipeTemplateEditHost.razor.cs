using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Services;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.RecipeTemplates;

/// <summary>
/// Reçete şablonu edit host code-behind — coordinator + satır düzenlemede kullanılan lookup'ların yüklenmesi
/// (hizmet katalogu, para birimleri). Layout DUMB kalır: servis çağırmaz.
/// </summary>
public partial class RecipeTemplateEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IRecipeTemplateAppService RecipeTemplateAppService { get; set; } = default!;
    [Inject] protected IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] protected ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<RecipeTemplateGetDto, RecipeTemplateListDto, Guid, RecipeTemplateListRequestDto>? _coordinator;
    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private IReadOnlyList<CurrencyUnitListDto> _currencyUnits = Array.Empty<CurrencyUnitListDto>();
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<RecipeTemplateGetDto, RecipeTemplateListDto, Guid, RecipeTemplateListRequestDto, RecipeTemplateCreateDto, RecipeTemplateUpdateDto>(
            RecipeTemplateAppService, Mapper);

        _services = await ServiceAppService.GetPickerListAsync();
        _currencyUnits = await CurrencyLookup.GetAsync();

        _ready = true;
    }
}
