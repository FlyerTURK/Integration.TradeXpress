using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Categories;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>N11 kategori seçici — TEK arama-destekli lookup (tüm yapraklar, TAM YOL adıyla). Kademeli drill yerine
/// doğrudan yaprak seçimi (yol ad tekrarını ayırt eder). Seçilince <see cref="OnLeafSelected"/> tetikler.</summary>
public partial class N11CategoryPicker : CrudComponentBase
{
    [Parameter] public string? SelectedExternalId { get; set; }
    [Parameter] public string? SelectedName { get; set; }

    /// <summary>Yaprak kategori seçilince (dış id + tam yol) — çağıran modele yazar + attribute'ları çeker.</summary>
    [Parameter] public EventCallback<N11CategorySelection> OnLeafSelected { get; set; }

    [Inject] private IN11CategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private List<N11LeafCategoryDto> _leaves = new();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _leaves = await CategoryAppService.GetLeafCategoriesAsync();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yaprak seçildi: modeli güncelle + tam yolu ad olarak taşı + callback.
    private async Task OnCategoryChangedAsync(string? externalId)
    {
        SelectedExternalId = externalId;
        var leaf = _leaves.FirstOrDefault(c => c.ExternalId == externalId);
        if (leaf is null)
        {
            return;
        }

        SelectedName = leaf.Path;
        await OnLeafSelected.InvokeAsync(new N11CategorySelection(leaf.ExternalId, leaf.Path));
    }
}

/// <summary>Seçilen yaprak kategori — dış id + tam yol adı.</summary>
public record N11CategorySelection(string ExternalId, string Name);
