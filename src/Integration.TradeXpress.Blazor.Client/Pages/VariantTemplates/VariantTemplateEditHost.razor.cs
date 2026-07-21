using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.VariantTemplates;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.VariantTemplates;

/// <summary>VariantTemplate edit host code-behind — coordinator kurulumu. Şablon içeriği (özellik grupları + değerleri)
/// dumb layout'ta iç içe DrillList ile düzenlenir; graf save coordinator üzerinden AppService'e gider.</summary>
public partial class VariantTemplateEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IVariantTemplateAppService VariantTemplateAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<VariantTemplateGetDto, VariantTemplateListDto, Guid, VariantTemplateListRequestDto>? _coordinator;
    private bool _ready;

    protected override Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<VariantTemplateGetDto, VariantTemplateListDto, Guid, VariantTemplateListRequestDto, VariantTemplateCreateDto, VariantTemplateUpdateDto>(
            VariantTemplateAppService, Mapper);
        _ready = true;
        return Task.CompletedTask;
    }
}
