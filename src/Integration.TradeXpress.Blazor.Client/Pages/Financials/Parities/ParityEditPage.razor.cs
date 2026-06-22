using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.Parities;

public partial class ParityEditPage
{
    public ParityEditPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject]
    protected IParityAppService ParityAppService { get; set; } = default!;

    [Inject]
    protected ICurrencyUnitAppService CurrencyUnitAppService { get; set; } = default!;

    [Inject]
    protected ITabManager TabManager { get; set; } = default!;

    // Görünür birimler (global + tenant). Çiftin iki bacağı farklı olmalı → karşılıklı dışla.
    private List<CurrencyUnitListDto> _units = new();

    protected IEnumerable<CurrencyUnitListDto> BaseCandidates
        => _units.Where(u => u.Id != EditModel.QuoteCurrencyUnitId);

    protected IEnumerable<CurrencyUnitListDto> QuoteCandidates
        => _units.Where(u => u.Id != EditModel.BaseCurrencyUnitId);

    protected override ICrudAppService<
        ParityGetDto, ParityListDto, Guid,
        ParityListRequestDto, ParityCreateDto, ParityUpdateDto> CrudAppService => ParityAppService;

    protected override string EntityChangeKey => "Parities";

    // Yapısal başlık (tab/top-panel/popup): L1 "Parite", L2 "BASE/QUOTE" (yeni kayıtta gizli), ikon.
    protected override string EditFormCaption => L["Parity"].Value;
    protected override string? EditEntityValue =>
        IsNewMode ? null : $"{EditModel.BaseCode}/{EditModel.QuoteCode}";
    protected override string? EditIconCssClass => TradeXpressIcons.Parity;

    // Tenant, global (host) pariteyi yalnız GÖRÜNTÜLER → salt-okunur form + bilgilendirme banner'ı.
    public override bool IsReadOnly => EditModel.IsGlobal && CurrentTenant?.Id != null;

    // Model yüklenince (snapshot'tan ÖNCE) combo adaylarını çek → dirty saymaz.
    protected override async Task OnModelLoadedAsync(ParityGetDto model)
    {
        var result = await CurrencyUnitAppService.GetListAsync(
            new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = result.Items.Where(u => u.IsActive).ToList();
    }

    public override async Task CloseAsync()
    {
        if (!IsPopupMode && CurrentMdiTab != null)
            await TabManager.TryCloseAsync(CurrentMdiTab.Id);
        else
            await base.CloseAsync();
    }
}
