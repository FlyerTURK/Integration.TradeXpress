using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Shipments;

/// <summary>Self-contained persistent N11 kargo şablonu drill'i — kanal edit formunun içinde açılır. İl/kargo firması
/// referans verilerini bir kez çeker, şablonları kanala göre listeler; CRUD anında AppService'e yazılır. Toolbar'da
/// İçe Aktar (N11'den upsert) + N11'e Gönder (seçili şablonu push; şartlı kargo varsa önce uyarı onayı) aksiyonları.</summary>
public partial class N11ShipmentTemplateDrill : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelTrN11GetDto Channel { get; set; } = default!;

    [Inject] private IN11ShipmentTemplateAppService AppService { get; set; } = default!;
    [Inject] private IN11CityAppService CityAppService { get; set; } = default!;
    [Inject] private IN11ShipmentCompanyAppService CompanyAppService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<N11ShipmentTemplateDto>? _drill;
    private N11UnlinkedCarrierPanel? _unlinkedPanel;

    private List<N11ShipmentTemplateDto> _templates = new();
    private List<N11CityDto> _cities = new();
    private List<N11ShipmentCompanyDto> _shipmentCompanies = new();

    protected override async Task OnInitializedAsync()
    {
        _cities = await CityAppService.GetCitiesAsync();
        _shipmentCompanies = await CompanyAppService.GetListAsync();
        _templates = await AppService.GetListAsync(Channel.Id);
    }

    // Yeni şablon: kanal varsayılanları. Adresler new() → null olmaz. Anlaşmalı kargo artık AYAR DEĞİL
    // (daima true, sunucuda sabit) → burada set edilmez.
    // Zorunlu N11 bilgi metinleri kanal düzeyi varsayılanlarıyla ön-doldurulur (varsa) → kullanıcı formda ezebilir.
    private N11ShipmentTemplateDto NewTemplate()
    {
        return new N11ShipmentTemplateDto
        {
            SalesChannelId = Channel.Id,
            ConditionalShippingUnit = N11ConditionalShippingUnit.Amount,
            ShippingInfo = Channel.DefaultShippingInfo,
            ExchangeInfo = Channel.DefaultExchangeInfo,
            InstallmentInfo = Channel.DefaultInstallmentInfo,
            WarehouseAddress = new(),
            ExchangeAddress = new(),
        };
    }

    // Cancel geri alabilsin diye JSON deep-copy (adres + liste alanları dahil tam kopya).
    private N11ShipmentTemplateDto CloneTemplate(N11ShipmentTemplateDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<N11ShipmentTemplateDto>(json)!;
    }

    private async Task<N11ShipmentTemplateDto> PersistCreate(N11ShipmentTemplateDto template)
    {
        var input = Mapper.Map<N11ShipmentTemplateDto, N11ShipmentTemplateCreateDto>(template);
        input.SalesChannelId = Channel.Id;
        return await AppService.CreateAsync(input);
    }

    private async Task<N11ShipmentTemplateDto> PersistUpdate(N11ShipmentTemplateDto template)
    {
        var input = Mapper.Map<N11ShipmentTemplateDto, N11ShipmentTemplateUpdateDto>(template);
        return await AppService.UpdateAsync(template.Id, input);
    }

    // Grid enum kolonları için lokalize metin (ComboBoxEnumEdit ile aynı "Enum:{Tip}:{Değer}" anahtar formatı).
    private string EnumText(string enumTypeName, Enum value)
    {
        return L[$"Enum:{enumTypeName}:{value}"].Value;
    }

    // Toolbar custom action'ları. Sil = built-in yerine override (N11 silme API'si yok → panele yönlendiren popup);
    // N11 ile Hizala = tam mutabakat (N11'den upsert + N11'de olmayan yerel şablonları sil).
    private IReadOnlyList<CrudToolbarAction> TemplateActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 100,
            Text = L["Delete"],
            Tooltip = L["Delete"],
            IconCssClass = TradeXpressIcons.Delete,
            Enabled = true,
            OnClick = ShowDeleteNotSupportedAsync,
        },
        new CrudToolbarAction
        {
            SortIndex = 150,
            Text = L["N11ShipmentTemplate:Sync"],
            Tooltip = L["N11ShipmentTemplate:Sync"],
            IconCssClass = TradeXpressIcons.SalesChannel,
            Enabled = true,
            OnClick = SyncAsync,
        },
    };

    // N11 silme teknik altyapısı sağlamadığından silme yalnız N11 panelinden yapılabilir → bilgi popup'ı (yerelde silmez).
    private async Task ShowDeleteNotSupportedAsync()
    {
        await UiService.ConfirmAsync(
            L["N11ShipmentTemplate:DeleteNotSupported"].Value,
            title: L["Delete"].Value,
            yesText: null,
            noText: null,
            showCancel: false,
            showNo: false);
    }

    // N11 ile Hizala: N11'deki şablonları yerelde upsert et + N11'de olmayanı PASİFLEŞTİR (backend),
    // listeyi tazele, öksüz kargo firması panelini yenile (yeni firma gelmiş olabilir), sonucu toast'la.
    private async Task SyncAsync()
    {
        try
        {
            var count = await AppService.SyncAsync(Channel.Id);
            _templates = await AppService.GetListAsync(Channel.Id);
            UiService.ShowSuccessToast(L["N11ShipmentTemplate:SyncSuccess", count]);

            if (_unlinkedPanel is not null)
            {
                await _unlinkedPanel.ReloadAsync();
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Cari bağı kurulunca şablon listesini tazele — bağ şablonun firma satırına da yansır.
    private async Task ReloadTemplatesAsync()
    {
        _templates = await AppService.GetListAsync(Channel.Id);
        StateHasChanged();
    }
}
