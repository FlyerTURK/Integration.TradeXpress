using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Futures;

/// <summary>Vadeli edit host — ince sarmal (coordinator + birim listesi kurar, geri kalan CrudEditHost'ta).
/// (@code bloğu 2026-08-07'de code-behind'a taşındı — dosyaya dokunma kuralı.)</summary>
public partial class FutureEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>ÇAĞRI-BAŞI footer daraltma (2026-08-06 Hakan kararı) — gerekçe GoodEditHost'ta.</summary>
    [Parameter] public bool SupportsSaveAndNew { get; set; } = true;

    [Parameter] public bool SupportsDelete { get; set; } = true;

    /// <summary>Sınıflandırma panelinden ÖN-DOLDURMA (2026-08-07 U1 — gerekçe MetalEditHost'ta). Tohum yazımı
    /// razor'daki <c>ApplyNewDefaults</c> lambda'sında.</summary>
    [Parameter] public string? SeedCode { get; set; }

    [Parameter] public string? SeedName { get; set; }

    private List<CurrencyUnitListDto> _units = new();
    private ICommitCoordinator<FutureGetDto, FutureListDto, Guid, FutureListRequestDto>? _coordinator;
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<FutureGetDto, FutureListDto, Guid, FutureListRequestDto, FutureCreateDto, FutureUpdateDto>(
            FutureAppService, Mapper);

        var result = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = result.Items.ToList();

        _ready = true;
    }

    private void ApplyNewDefaults(FutureGetDto m)
    {
        m.IsActive = true;
        m.FollowingFactor = 1m;

        // Panel tohumu (U1 — gerekçe MetalEditHost'ta).
        if (!string.IsNullOrWhiteSpace(SeedCode))
        {
            m.Code = SeedCode!;
        }

        if (!string.IsNullOrWhiteSpace(SeedName))
        {
            m.Name = SeedName!;
        }
    }

    private async Task OnEditFollowingUnitAsync(Guid? followingId)
    {
        if (followingId is not { } id || id == Guid.Empty)
        {
            return;
        }

        var code = _units.FirstOrDefault(u => u.Id == id)?.Code;
        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }
}
