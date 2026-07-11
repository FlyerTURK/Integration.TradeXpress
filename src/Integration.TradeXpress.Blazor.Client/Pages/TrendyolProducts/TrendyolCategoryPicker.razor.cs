using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolCategories;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>Trendyol kategori seçici — İKİ mod: (1) SERVER-SIDE yaprak arama (LookupEdit, en az 3 harf, tam yol),
/// (2) kökten zincirleme cascade DxComboBox'lar (her seviye <see cref="ITrendyolCategoryAppService.GetChildrenAsync"/>
/// ile dolar; yaprak seçilince zincir biter). İki mod da AYNI <see cref="OnLeafSelected"/> callback'ini besler.
/// Türkçe aksan/case-duyarsız arama ("kul"→"Kül"). N11CategoryPicker paritesi (id-bazlı string dış id).</summary>
public partial class TrendyolCategoryPicker : CrudComponentBase
{
    [Parameter] public string? SelectedExternalId { get; set; }
    [Parameter] public string? SelectedName { get; set; }

    /// <summary>Yaprak kategori seçilince (dış id + tam yol) — çağıran modele yazar + attribute'ları çeker.</summary>
    [Parameter] public EventCallback<TrendyolCategorySelection> OnLeafSelected { get; set; }

    [Inject] private ITrendyolCategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // 0 = Arama (LookupEdit), 1 = Ağaç (cascade). Varsayılan: Arama.
    private int _activeMode;

    // ── Mod 1: Arama ── Yalnız son aramanın sonuçları (server'dan; en fazla 50). Ön-yükleme YOK.
    private List<TrendyolLeafCategoryDto> _results = new();

    // ── Mod 2: Ağaç (cascade) ── Zincir seviyeleri: her seviye bir combo (parent + seçenekler + seçim).
    private readonly List<CascadeLevel> _levels = new();

    // Cascade'de en dipteki seçim yaprak mı — false ise "yaprağa inin" uyarısı gösterilir (geçerli kategori yok).
    private bool _treeReachedLeaf;

    // İlk-kullanım otomatik senkron kontrolü (bir kez): DB boşsa hem arama hem Ağaç modu boş kalırdı → kullanıcı
    // hiçbir butona basmadan kategoriler gelsin diye picker açılışında lazy senkron.
    private bool _syncChecked;

    protected override async Task OnInitializedAsync()
    {
        await EnsureCategoriesSyncedAsync();
    }

    // DB'de hiç kategori yoksa (ilk kullanım) Trendyol'dan bir kez çek (host-global upsert). Doluysa no-op (ucuz kök sorgusu).
    private async Task EnsureCategoriesSyncedAsync()
    {
        if (_syncChecked)
        {
            return;
        }

        _syncChecked = true;
        try
        {
            var roots = await CategoryAppService.GetChildrenAsync(null);
            if (roots.Count > 0)
            {
                return; // Zaten senkron.
            }

            UiService.ShowWarningToast(L["Trendyol:Category:AutoSyncing"].Value);
            var count = await CategoryAppService.SyncCategoriesAsync();
            if (count > 0)
            {
                UiService.ShowSuccessToast(L["Trendyol:Category:SyncSuccess", count].Value);
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Mod değişti: "Ağaç" moduna ilk geçişte kök kategorileri yükle (lazy — Arama modunda gereksiz sorgu atma).
    private async Task OnModeChangedAsync(int mode)
    {
        _activeMode = mode;
        if (mode == 1 && _levels.Count == 0)
        {
            await LoadRootLevelAsync();
        }
    }

    // Kök kategoriler = GetChildrenAsync(null) → ilk combo. Zincir buradan başlar.
    private async Task LoadRootLevelAsync()
    {
        try
        {
            var roots = await CategoryAppService.GetChildrenAsync(null);
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
            return; // Seçim temizlendi → ara seviye; geçerli kategori yok.
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
            await OnLeafSelected.InvokeAsync(new TrendyolCategorySelection(node.ExternalId, path));
            return;
        }

        // Yaprak değil: çocukları yükle → altına yeni combo.
        try
        {
            var children = await CategoryAppService.GetChildrenAsync(node.ExternalId);
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

    // Yaprak seçildi (arama modu): modeli güncelle + tam yolu ad olarak taşı + callback.
    // TEMİZLEME serbest (kategori OPSİYONEL, 2026-07-11): boş değer çağırana BOŞ seçim olarak bildirilir.
    private async Task OnCategoryChangedAsync(string? externalId)
    {
        SelectedExternalId = externalId;
        if (string.IsNullOrEmpty(externalId))
        {
            SelectedName = null;
            await OnLeafSelected.InvokeAsync(new TrendyolCategorySelection(null, null));
            return;
        }

        var leaf = _results.FirstOrDefault(c => c.ExternalId == externalId);
        if (leaf is null)
        {
            return;
        }

        SelectedName = leaf.Path;
        await OnLeafSelected.InvokeAsync(new TrendyolCategorySelection(leaf.ExternalId, leaf.Path));
    }

    /// <summary>Cascade tek seviye durumu — parent dış id + o seviyenin seçenekleri + seçilen dış id.</summary>
    private sealed class CascadeLevel
    {
        public string? ParentExternalId { get; init; }
        public List<TrendyolCategoryTreeNodeDto> Options { get; init; } = new();
        public string? SelectedExternalId { get; set; }
    }
}

/// <summary>Seçilen yaprak kategori — dış id + tam yol adı. Kategori OPSİYONEL (2026-07-11): temizleme
/// boş seçim (null, null) olarak bildirilir.</summary>
public record TrendyolCategorySelection(string? ExternalId, string? Name);
