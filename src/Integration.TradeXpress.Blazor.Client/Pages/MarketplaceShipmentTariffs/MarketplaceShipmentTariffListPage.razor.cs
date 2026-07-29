using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MarketplaceShipmentTariffs;

namespace Integration.TradeXpress.Blazor.Client.Pages.MarketplaceShipmentTariffs;

/// <summary>
/// Pazaryeri anlaşmalı kargo tarifesi listesi — SALT OKUNUR.
/// <para>Desi tablosu (taşıyıcı başına 101 satır) listeyle BİRLİKTE çekilmez; kullanıcı bir taşıyıcı
/// seçtiğinde o tarifenin detayı ayrıca yüklenir. Aksi hâlde altı taşıyıcı için 600+ satır her açılışta
/// boşuna taşınırdı.</para>
/// </summary>
public partial class MarketplaceShipmentTariffListPage
{
    #region Injected

    [Inject]
    protected IMarketplaceShipmentTariffAppService TariffAppService { get; set; } = default!;

    [Inject]
    protected IStringLocalizer<TradeXpressResource> L { get; set; } = default!;

    #endregion

    #region State

    private List<MarketplaceShipmentTariffDto> _tariffs = new();

    /// <summary>Seçili taşıyıcının tam görünümü (desi tablosu + barem); seçim yoksa <c>null</c>.</summary>
    private MarketplaceShipmentTariffDetailDto? _detail;

    private IReadOnlyList<object> _selectedItems = Array.Empty<object>();

    /// <summary>Varsayılan: yalnız yürürlüktekiler. Geçmiş sürümler kapalı gelir (tarife arşivi birikir).</summary>
    private bool _onlyEffective = true;

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    #endregion

    #region Data

    private async Task LoadAsync()
    {
        _tariffs = await TariffAppService.GetListAsync(new MarketplaceShipmentTariffListInput
        {
            OnlyEffective = _onlyEffective,
        });

        // Liste tazelendi: eski seçim artık listede olmayabilir → detay panelini kapat.
        _selectedItems = Array.Empty<object>();
        _detail = null;
    }

    private async Task OnFilterChangedAsync(bool onlyEffective)
    {
        _onlyEffective = onlyEffective;
        await LoadAsync();
    }

    /// <summary>Seçim değişince o tarifenin desi tablosunu yükler (lazy — liste hafif kalsın).</summary>
    private async Task OnSelectionChangedAsync(IReadOnlyList<object> items)
    {
        _selectedItems = items;

        if (items.FirstOrDefault() is not MarketplaceShipmentTariffDto row)
        {
            _detail = null;
            return;
        }

        if (_detail?.Id == row.Id)
        {
            return;
        }

        _detail = await TariffAppService.GetAsync(row.Id);
    }

    #endregion
}
