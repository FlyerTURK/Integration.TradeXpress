using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Substitutions;

public partial class SubstitutionGroupEditHost
{
    public SubstitutionGroupEditHost()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISubstitutionGroupAppService SubstitutionGroupAppService { get; set; } = default!;
    [Inject] protected IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private List<MetalListDto> _metals = new();
    private List<MetalVariantLookupDto> _metalVariants = new();
    private ICommitCoordinator<SubstitutionGroupGetDto, SubstitutionGroupListDto, Guid, SubstitutionGroupListRequestDto>? _coordinator;
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<
            SubstitutionGroupGetDto, SubstitutionGroupListDto, Guid,
            SubstitutionGroupListRequestDto, SubstitutionGroupCreateDto, SubstitutionGroupUpdateDto>(
            SubstitutionGroupAppService, Mapper);

        // Muadil listesine yalnız ADET-hesaplı + STANDART gramajlı madenler girebilir
        // (solver kuralı: IsQuantity + StableQuantity>0 — hesap servisinde fail-fast).
        _metals = (await MetalAppService.GetPickerListAsync())
            .Where(m => m.IsQuantity && m.StableQuantity > 0m)
            .ToList();

        // Varyant Kapsamı ağacının veri kaynağı — host-level katalog dahil (servis filtreleri kendi kapatır).
        _metalVariants = await MetalAppService.GetVariantLookupAsync();

        _ready = true;
    }

    // Yeni kayıt varsayılanları — tür şimdilik yalnız Metal (form'da sabit/disabled).
    private static void ApplyNew(SubstitutionGroupGetDto model)
    {
        model.IsActive = true;
        model.Type = SubstitutionType.Metal;
        model.ToleranceType = ToleranceType.Amount;
    }
}
