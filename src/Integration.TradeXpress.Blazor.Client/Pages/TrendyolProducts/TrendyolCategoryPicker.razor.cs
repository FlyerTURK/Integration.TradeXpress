using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolCategories;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>Trendyol kategori seçici — SERVER-SIDE arama (LookupEdit). Kullanıcı yazınca (en az 3 harf) sunucudan yaprak
/// kategoriler TAM YOL adıyla çekilir; liste ÖN-YÜKLENMEZ. Türkçe aksan/case-duyarsız ("kul"→"Kül"). Seçilince
/// <see cref="OnLeafSelected"/> tetikler. N11CategoryPicker paritesi (id-bazlı fark yok — her ikisi de string dış id).</summary>
public partial class TrendyolCategoryPicker : CrudComponentBase
{
    [Parameter] public string? SelectedExternalId { get; set; }
    [Parameter] public string? SelectedName { get; set; }

    /// <summary>Yaprak kategori seçilince (dış id + tam yol) — çağıran modele yazar + attribute'ları çeker.</summary>
    [Parameter] public EventCallback<TrendyolCategorySelection> OnLeafSelected { get; set; }

    [Inject] private ITrendyolCategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Yalnız son aramanın sonuçları (server'dan; en fazla 50). Ön-yükleme YOK.
    private List<TrendyolLeafCategoryDto> _results = new();

    // Kullanıcı arama kutusuna yazdı (LookupEdit min-3 harf koşulunu uygular) → sunucudan çek.
    private async Task OnSearchAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _results = new List<TrendyolLeafCategoryDto>();
            return;
        }

        try
        {
            _results = await CategoryAppService.SearchLeafCategoriesAsync(term);
        }
        catch (Exception ex)
        {
            _results = new List<TrendyolLeafCategoryDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yaprak seçildi: modeli güncelle + tam yolu ad olarak taşı + callback.
    private async Task OnCategoryChangedAsync(string? externalId)
    {
        SelectedExternalId = externalId;
        var leaf = _results.FirstOrDefault(c => c.ExternalId == externalId);
        if (leaf is null)
        {
            return;
        }

        SelectedName = leaf.Path;
        await OnLeafSelected.InvokeAsync(new TrendyolCategorySelection(leaf.ExternalId, leaf.Path));
    }
}

/// <summary>Seçilen yaprak kategori — dış id + tam yol adı.</summary>
public record TrendyolCategorySelection(string ExternalId, string Name);
