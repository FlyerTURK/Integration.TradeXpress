using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Categories;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>N11 kategori KADEMELİ seçici — köklerden (79 top) yaprağa iner (<c>GetChildrenAsync</c>). Yaprak
/// seçilince <see cref="OnLeafSelected"/> tetikler. Zaten seçili kategori varsa breadcrumb + "Değiştir" (N11'de
/// ata zinciri tek-tek sorgulanamadığından yeniden seçim KÖKTEN başlar).</summary>
public partial class N11CategoryPicker : CrudComponentBase
{
    [Parameter] public string? SelectedExternalId { get; set; }
    [Parameter] public string? SelectedName { get; set; }

    /// <summary>Yaprak kategori seçilince (dış id + ad) — çağıran modele yazar + attribute'ları çeker.</summary>
    [Parameter] public EventCallback<N11CategorySelection> OnLeafSelected { get; set; }

    [Inject] private IN11CategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;

    // Her kademe: o seviyenin çocuk seçenekleri + seçili dış-id. Kök = index 0.
    private readonly List<CascadeLevel> _levels = new();
    private bool _picking;

    protected override async Task OnInitializedAsync()
    {
        // Seçili kategori yoksa doğrudan seçim moduna gir (kökleri yükle); varsa breadcrumb göster.
        if (string.IsNullOrEmpty(SelectedExternalId))
        {
            await StartPickingAsync();
        }
    }

    private async Task StartPickingAsync()
    {
        _picking = true;
        _levels.Clear();
        await LoadLevelAsync(null);
    }

    /// <summary>Verilen üst kategorinin çocuklarını yeni bir kademe olarak ekler (çocuk yoksa eklemez = yaprak).</summary>
    private async Task LoadLevelAsync(string? parentExternalId)
    {
        try
        {
            var children = await CategoryAppService.GetChildrenAsync(parentExternalId);
            if (children.Count > 0)
            {
                _levels.Add(new CascadeLevel { Options = children });
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(ex.Message);
        }
    }

    /// <summary>Bir kademede seçim değişti: alt kademeleri buda; yaprak ise callback, değilse çocukları yükle.</summary>
    private async Task OnLevelChangedAsync(int levelIndex, string? externalId)
    {
        var level = _levels[levelIndex];
        level.SelectedId = externalId;

        // Bu kademeden sonraki tüm kademeleri kaldır (kullanıcı üst seviyede yeniden dallanıyor).
        if (_levels.Count > levelIndex + 1)
        {
            _levels.RemoveRange(levelIndex + 1, _levels.Count - levelIndex - 1);
        }

        var node = level.Options.FirstOrDefault(o => o.ExternalId == externalId);
        if (node is null)
        {
            return;
        }

        if (node.IsLeaf)
        {
            _picking = false;
            SelectedExternalId = node.ExternalId;
            SelectedName = node.Name;
            await OnLeafSelected.InvokeAsync(new N11CategorySelection(node.ExternalId, node.Name));
        }
        else
        {
            await LoadLevelAsync(node.ExternalId);
        }
    }

    private async Task ChangeClickedAsync()
    {
        await StartPickingAsync();
    }

    private sealed class CascadeLevel
    {
        public List<N11CategoryTreeNodeDto> Options { get; set; } = new();
        public string? SelectedId { get; set; }
    }
}

/// <summary>Seçilen yaprak kategori — dış id + ad.</summary>
public record N11CategorySelection(string ExternalId, string Name);
