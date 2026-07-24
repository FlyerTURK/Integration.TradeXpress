using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>Çekirdek kargo şablonunun satış kanalı dağıtımları drill'i — self-contained persistent. Çekirdek şablon
/// düzenleme formunun içinde açılır: hangi kanallarda (şu an yalnız N11) kullanılacağını buradan ekler/düzenler. Ekle,
/// çekirdekten + kanal varsayılanlarından ön-doldurulmuş bir N11 kargo şablonu üretir (elle giriş bypass) ve N11 edit
/// formunda tamamlatır; Kaydet N11 <c>CreateAsync</c>/<c>UpdateAsync</c> (validated + push). N11 silme API'si olmadığından
/// silme yalnız bilgi popup'ı gösterir. Referans desen: <see cref="N11Shipments.N11ShipmentTemplateDrill"/>.</summary>
public partial class ShipmentTemplateChannelDrill : CrudComponentBase
{
    /// <summary>Düzenlenen çekirdek kargo şablonunun id'si — dağıtımlar bunun için listelenir/üretilir.</summary>
    [Parameter, EditorRequired] public Guid CoreTemplateId { get; set; }

    [Inject] private IShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] private IN11ShipmentTemplateAppService N11AppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IN11CityAppService CityAppService { get; set; } = default!;
    [Inject] private IN11ShipmentCompanyAppService CompanyAppService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<N11ShipmentTemplateDto>? _drill;

    // Çekirdeğe bağlı N11 kargo şablonları (tam DTO — DrillList doğrudan düzenler). Deployment satırlarından türetilir.
    private List<N11ShipmentTemplateDto> _templates = new();

    // Şirketin N11 satış kanalları (Ekle'de kanal seçimi + grid'de kanal adı çözümü).
    private List<SalesChannelListDto> _n11Channels = new();

    // N11 edit formu referans verileri (bir kez çekilir; mevcut drill deseni).
    private List<N11CityDto> _cities = new();
    private List<N11ShipmentCompanyDto> _shipmentCompanies = new();

    // Kanal seçim popup'ı (yalnız birden çok N11 kanalı varsa).
    private bool _channelPickerVisible;
    private Guid? _pickedChannelId;

    protected override async Task OnInitializedAsync()
    {
        _cities = await CityAppService.GetCitiesAsync();
        _shipmentCompanies = await CompanyAppService.GetListAsync();

        var channels = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _n11Channels = channels.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();

        await ReloadAsync();
    }

    // Dağıtımları yeniden yükle: çekirdeğin deployment satırlarını çek, her N11 satırı için tam DTO'yu al (kanal başına
    // az kayıt). Yeni liste referansı → DrillList grid'i tazeler.
    private async Task ReloadAsync()
    {
        var deployments = await ShipmentTemplateAppService.GetChannelDeploymentsAsync(CoreTemplateId);
        var templates = new List<N11ShipmentTemplateDto>(deployments.Count);
        foreach (var deployment in deployments)
        {
            // Şu an yalnız N11; başka kanal türü gelirse şimdilik atlanır (ileride kanal-tür dallanması).
            if (deployment.SalesChannelType == SalesChannelType.TrN11)
            {
                templates.Add(await N11AppService.GetAsync(deployment.ChannelTemplateId));
            }
        }

        _templates = templates;
        StateHasChanged();
    }

    // Grid "Kanal" kolonu — SalesChannelId'yi şirketin N11 kanallarından ada çözer.
    private string ChannelName(N11ShipmentTemplateDto template)
    {
        return _n11Channels.FirstOrDefault(c => c.Id == template.SalesChannelId)?.Name ?? string.Empty;
    }

    // Built-in Yeni kapalı (AllowAdd=false) → factory yalnız imza gereği; pratikte kullanılmaz (Ekle taslak akışı esas).
    private N11ShipmentTemplateDto NewTemplateStub()
    {
        return new N11ShipmentTemplateDto
        {
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
        return await N11AppService.CreateAsync(input);
    }

    private async Task<N11ShipmentTemplateDto> PersistUpdate(N11ShipmentTemplateDto template)
    {
        var input = Mapper.Map<N11ShipmentTemplateDto, N11ShipmentTemplateUpdateDto>(template);
        return await N11AppService.UpdateAsync(template.Id, input);
    }

    // Toolbar aksiyonları: Ekle (kanal seç → taslak → aç) + Sil (N11 silme yok → bilgi popup'ı). Built-in Yeni/Sil kapalı.
    private IReadOnlyList<CrudToolbarAction> TemplateActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 0,
            Text = L["New"],
            Tooltip = L["New"],
            IconCssClass = TradeXpressIcons.Add,
            Enabled = true,
            OnClick = StartAddAsync,
        },
        new CrudToolbarAction
        {
            SortIndex = 100,
            Text = L["Delete"],
            Tooltip = L["Delete"],
            IconCssClass = TradeXpressIcons.Delete,
            Enabled = true,
            OnClick = ShowDeleteNotSupportedAsync,
        },
    };

    // Ekle: kanal yoksa uyar; tek kanalda otomatik seç; birden çoksa seçim popup'ı aç.
    private async Task StartAddAsync()
    {
        if (_n11Channels.Count == 0)
        {
            UiService.ShowWarningToast(L["ShipmentTemplate:ChannelMissing"].Value);
            return;
        }

        if (_n11Channels.Count == 1)
        {
            await BuildDraftAndStartAsync(_n11Channels[0].Id);
            return;
        }

        _pickedChannelId = _n11Channels[0].Id;
        _channelPickerVisible = true;
        StateHasChanged();
    }

    private async Task ConfirmChannelPickAsync()
    {
        if (_pickedChannelId is { } channelId)
        {
            _channelPickerVisible = false;
            await BuildDraftAndStartAsync(channelId);
        }
    }

    // Çekirdekten + kanal varsayılanlarından ön-doldurulmuş N11 taslağı (PERSIST ETMEZ) → düzenlenebilir DTO'ya map'le →
    // adresleri new() ile garanti et (edit formu null adresi render etmez) → DrillList yeni-kayıt popup'ında aç.
    private async Task BuildDraftAndStartAsync(Guid channelId)
    {
        try
        {
            var draft = await N11AppService.BuildDeploymentDraftAsync(CoreTemplateId, channelId);
            var editable = Mapper.Map<N11ShipmentTemplateCreateDto, N11ShipmentTemplateDto>(draft);
            editable.WarehouseAddress ??= new();
            editable.ExchangeAddress ??= new();
            _drill?.StartNewItem(editable);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

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
}
