using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyTaxonomies;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.EtsyProducts;

/// <summary>Etsy taksonomi (kategori) seçici — İKİ mod: (1) SERVER-SIDE yaprak arama (LookupEdit, en az 3 harf, tam yol),
/// (2) kökten zincirleme cascade DxComboBox'lar (her seviye <see cref="IEtsyTaxonomyAppService.GetChildrenAsync"/> ile
/// dolar; yaprak seçilince zincir biter). Etsy taksonomisi derin+büyük → cascade özellikle değerli. İki mod da AYNI
/// <see cref="OnLeafSelected"/> callback'ini besler. TrendyolCategoryPicker paritesi (dış id string).</summary>
public partial class EtsyTaxonomyPicker : CrudComponentBase
{
    [Parameter] public string? SelectedExternalId { get; set; }
    [Parameter] public string? SelectedName { get; set; }

    /// <summary>Yaprak taksonomi seçilince (dış id + tam yol) — çağıran modele yazar. Temizleme boş seçim bildirir.</summary>
    [Parameter] public EventCallback<EtsyTaxonomySelection> OnLeafSelected { get; set; }

    [Inject] private IEtsyTaxonomyAppService TaxonomyAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // 0 = Arama (LookupEdit), 1 = Ağaç (cascade). Varsayılan: Arama.
    private int _activeMode;

    // ── Mod 1: Arama ── Yalnız son aramanın sonuçları (server'dan; en fazla 50). Ön-yükleme YOK.
    private List<EtsyLeafCategoryDto> _results = new();

    // ── Mod 2: Ağaç (cascade) ── Zincir seviyeleri: her seviye bir combo (parent + seçenekler + seçim).
    private readonly List<CascadeLevel> _levels = new();

    // Cascade'de en dipteki seçim yaprak mı — false ise "yaprağa inin" uyarısı gösterilir (geçerli taksonomi yok).
    private bool _treeReachedLeaf;

    // İlk-kullanım otomatik senkron kontrolü (bir kez): DB boşsa hem arama hem Ağaç modu boş kalırdı → kullanıcı
    // hiçbir butona basmadan taksonomi gelsin diye picker açılışında lazy senkron.
    private bool _syncChecked;

    protected override async Task OnInitializedAsync()
    {
        await EnsureTaxonomySyncedAsync();
    }

    // DB'de hiç taksonomi yoksa (ilk kullanım) Etsy'den bir kez çek (host-global upsert). Doluysa no-op (ucuz kök sorgusu).
    private async Task EnsureTaxonomySyncedAsync()
    {
        if (_syncChecked)
        {
            return;
        }

        _syncChecked = true;
        try
        {
            var roots = await TaxonomyAppService.GetChildrenAsync(null);
            if (roots.Count > 0)
            {
                return; // Zaten senkron.
            }

            UiService.ShowWarningToast(L["Etsy:Taxonomy:AutoSyncing"].Value);
            var count = await TaxonomyAppService.SyncTaxonomyAsync();
            if (count > 0)
            {
                UiService.ShowSuccessToast(L["Etsy:Taxonomy:SyncSuccess", count].Value);
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Mod değişti: "Ağaç" moduna ilk geçişte kök düğümleri yükle (lazy — Arama modunda gereksiz sorgu atma).
    private async Task OnModeChangedAsync(int mode)
    {
        _activeMode = mode;
        if (mode == 1 && _levels.Count == 0)
        {
            await LoadRootLevelAsync();
        }
    }

    // Kök düğümler = GetChildrenAsync(null) → ilk combo. Zincir buradan başlar.
    private async Task LoadRootLevelAsync()
    {
        try
        {
            var roots = await TaxonomyAppService.GetChildrenAsync(null);
            _levels.Clear();
            _levels.Add(new CascadeLevel { ParentExternalId = null, Options = roots });
            _treeReachedLeaf = false;
        }
        catch (Exception ex)
        {
            _levels.Clear();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Cascade seviye seçimi değişti: alttaki tüm combolar sıfırlanır; seçim yaprak ise zincir biter + callback,
    // değilse hemen altına yeni combo (o seviyenin çocukları) eklenir.
    private async Task OnLevelChangedAsync(int levelIndex, string? externalId)
    {
        var level = _levels[levelIndex];
        level.SelectedExternalId = externalId;

        // Bu seviyenin ALTINDAKİ tüm seviyeleri kaldır (stale alt seçim kalmasın).
        if (_levels.Count > levelIndex + 1)
        {
            _levels.RemoveRange(levelIndex + 1, _levels.Count - levelIndex - 1);
        }

        _treeReachedLeaf = false;

        if (string.IsNullOrEmpty(externalId))
        {
            return; // Seçim temizlendi → ara seviye; geçerli taksonomi yok.
        }

        var node = level.Options.FirstOrDefault(o => o.ExternalId == externalId);
        if (node is null)
        {
            return;
        }

        if (node.IsLeaf)
        {
            // Yaprak: zincir tamam → tam yolu (seçili seviyelerin adları) kur + callback.
            _treeReachedLeaf = true;
            var path = BuildSelectedPath(levelIndex);
            SelectedExternalId = node.ExternalId;
            SelectedName = path;
            await OnLeafSelected.InvokeAsync(new EtsyTaxonomySelection(node.ExternalId, path));
            return;
        }

        // Yaprak değil: çocukları yükle → altına yeni combo.
        try
        {
            var children = await TaxonomyAppService.GetChildrenAsync(node.ExternalId);
            _levels.Add(new CascadeLevel { ParentExternalId = node.ExternalId, Options = children });
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Seçili zincir adlarını "Kök > ... > Yaprak" olarak birleştirir (0..levelIndex seviyelerinin seçimleri).
    private string BuildSelectedPath(int levelIndex)
    {
        var names = new List<string>();
        for (var i = 0; i <= levelIndex; i++)
        {
            var lv = _levels[i];
            var selected = lv.Options.FirstOrDefault(o => o.ExternalId == lv.SelectedExternalId);
            if (selected is not null)
            {
                names.Add(selected.Name);
            }
        }

        return string.Join(" > ", names);
    }

    // ── Mod 1: Arama ── Kullanıcı yazdı (LookupEdit min-3 harf koşulunu uygular) → sunucudan çek.
    private async Task OnSearchAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _results = new List<EtsyLeafCategoryDto>();
            return;
        }

        try
        {
            _results = await TaxonomyAppService.SearchLeafCategoriesAsync(term);
        }
        catch (Exception ex)
        {
            _results = new List<EtsyLeafCategoryDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yaprak seçildi (arama modu): modeli güncelle + tam yolu ad olarak taşı + callback.
    // TEMİZLEME serbest (taksonomi taslakta OPSİYONEL): boş değer çağırana BOŞ seçim olarak bildirilir.
    private async Task OnTaxonomyChangedAsync(string? externalId)
    {
        SelectedExternalId = externalId;
        if (string.IsNullOrEmpty(externalId))
        {
            SelectedName = null;
            await OnLeafSelected.InvokeAsync(new EtsyTaxonomySelection(null, null));
            return;
        }

        var leaf = _results.FirstOrDefault(c => c.ExternalId == externalId);
        if (leaf is null)
        {
            return;
        }

        SelectedName = leaf.FullPathName;
        await OnLeafSelected.InvokeAsync(new EtsyTaxonomySelection(leaf.ExternalId, leaf.FullPathName));
    }

    /// <summary>Cascade tek seviye durumu — parent dış id + o seviyenin seçenekleri + seçilen dış id.</summary>
    private sealed class CascadeLevel
    {
        public string? ParentExternalId { get; init; }
        public List<EtsyTaxonomyTreeNodeDto> Options { get; init; } = new();
        public string? SelectedExternalId { get; set; }
    }
}

/// <summary>Seçilen yaprak taksonomi — dış id + tam yol adı. Taksonomi taslakta OPSİYONEL: temizleme
/// boş seçim (null, null) olarak bildirilir.</summary>
public record EtsyTaxonomySelection(string? ExternalId, string? Name);
