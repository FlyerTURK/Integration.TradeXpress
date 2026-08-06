using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Components.Shared;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.TrendyolShipments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>
/// Trendyol satış kanalı KURULUM SİHİRBAZI — N11'le AYNI kabuk (<see cref="WizardShell"/>), FARKLI adımlar.
///
/// <para><b>N11 sihirbazından iki bilinçli sapma:</b></para>
/// <list type="bullet">
///   <item><b>KDV adımı YOK.</b> N11'de gerekliydi çünkü ürün listesi <c>vatRate</c> döndürmüyor ve kayıtlar
///   KDV'siz doğuyordu. Trendyol içe aktarımı oranı UZAKTAN OKUYOR → kullanıcıya sormanın anlamı yok, sorsak
///   uzak gerçeğin üstüne tahmin koymuş olurduk.</item>
///   <item><b>Kargo adımı ŞABLON değil FİRMA.</b> N11'de adlandırılmış şablon nesnesi var
///   (<c>GetShipmentTemplateList</c> — firmalar + iller + eşik bir arada); Trendyol'da böyle bir nesne YOKTUR,
///   ürün başına kargo firması seçilir (<c>cargoCompanyId</c>).</item>
/// </list>
///
/// <para><b>Kategori adımı da yok</b> — Trendyol kategori ağacı kanal oluşturulurken AppService hook'uyla zaten
/// senkronlanıyor. Sihirbaza ayrı adım koymak, olmayan bir işi varmış gibi göstermek olurdu.</para>
///
/// <para><b>Kargo firması listesi canlı uçtan gelmiyor:</b> Trendyol'da firmaları döndüren bir HTTP ucu yoktur;
/// liste resmî statik tablodan seed edilir (<c>TrendyolCargoProviderSeeder</c>). Seed hiç koşmadıysa adım boş
/// görünür ve kullanıcı uyarılır — sessizce boş combo göstermek yerine.</para>
/// </summary>
public partial class SalesChannelTrTrendyolWizard : CrudComponentBase
{
    /// <summary>MEVCUT kanalın kimliği — verilirse sihirbaz "kurulumu tamamla" kipinde açılır (kanal
    /// OLUŞTURULMAZ). Boşsa yeni kanal kurulumu.</summary>
    [Parameter] public Guid? Id { get; set; }

    [Inject] private ISalesChannelTrTrendyolAppService ChannelAppService { get; set; } = default!;
    [Inject] private ITrendyolCargoProviderAppService CargoProviderAppService { get; set; } = default!;
    [Inject] private ISalesChannelTrTrendyolProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // ── 1. adım: kimlik ─────────────────────────────────────────────────────────────────────────────
    private string? _code;
    private string? _name;
    private string? _sellerId;
    private string? _apiKey;
    private string? _apiSecret;
    private Guid _channelId;

    // ── 2. adım: kargo firması ──────────────────────────────────────────────────────────────────────
    private List<TrendyolCargoProviderDto> _cargoProviders = new();
    private Guid? _selectedCargoProviderId;

    // ── 3. adım: içe aktarım ────────────────────────────────────────────────────────────────────────
    private TrendyolImportResultDto? _import;

    // ── 4. adım: emtia sınıflandırması (kanal-agnostik ortak panel) ─────────────────────────────────
    private ProductCommodityClassificationPanel? _classifyPanel;
    private int _classifyPending;
    private ProductCommodityProvisionResultDto? _classifyResult;

    private bool _busy;

    private string SelectedCargoProviderName
    {
        get
        {
            return _cargoProviders.FirstOrDefault(p => p.Id == _selectedCargoProviderId)?.Name
                   ?? L["SalesChannelTrTrendyol:Wizard:NotChosen"].Value;
        }
    }

    private List<string> ImportIssueLines
    {
        get
        {
            if (_import is not { } r)
            {
                return new List<string>();
            }

            return r.Warnings
                .Concat(r.SkippedRows.Select(s => s.ToString()))
                .Concat(r.UnmatchedCategories.Select(c => $"{L["TrendyolProduct:Import:UnmatchedCategoryPrefix"]}: {c}"))
                .ToList();
        }
    }

    /// <summary>Kurulum sonrası kalan işler — sihirbaz "bitti" deyip kullanıcıyı eksik kurulumla bırakmasın.</summary>
    private List<string> RemainingWork
    {
        get
        {
            var items = new List<string>();
            if (_selectedCargoProviderId is null)
            {
                items.Add(L["SalesChannelTrTrendyol:Wizard:RemainingCargo"].Value);
            }

            if (_import?.UnmatchedCategories.Count > 0)
            {
                items.Add(L["SalesChannelTrTrendyol:Wizard:RemainingCategories", _import.UnmatchedCategories.Count].Value);
            }

            if (_import?.SkippedRows.Count > 0)
            {
                items.Add(L["SalesChannelTrTrendyol:Wizard:RemainingSkipped", _import.SkippedRows.Count].Value);
            }

            // Sınıflandırma adımı ATLANABİLİR ama SESSİZ değildir: bağlanmayan ürünlerin stoğu Sabit kalır ve
            // pazaryerinin eski adedi geçerli olmayı sürdürür.
            if (_classifyPending > 0)
            {
                items.Add(L["SalesChannelTrTrendyol:Wizard:RemainingCommodities", _classifyPending].Value);
            }

            return items;
        }
    }

    /// <summary>Sihirbaz MEVCUT bir kanal üzerinde mi çalışıyor (kimlik adımı kanal oluşturmaz).</summary>
    private bool IsExistingChannel
    {
        get { return Id is { } id && id != Guid.Empty; }
    }

    /// <summary>Kargo firmaları host-global ve kanaldan bağımsız → bir kez yüklenir. Mevcut kanal kipinde
    /// ayrıca kanal da yüklenir ki kimlik adımı hangi kanalda çalışıldığını göstersin.</summary>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _cargoProviders = await CargoProviderAppService.GetListAsync();

            if (IsExistingChannel)
            {
                var channel = await ChannelAppService.GetAsync(Id!.Value);
                _channelId = channel.Id;
                _code = channel.Code;
                _name = channel.Name;
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
    }

    /// <summary>1. adım: kanalı OLUŞTUR. Kimlik SUNUCUDA doğrulanır (ürün listesi probe'u) — geçmezse kayıt
    /// açılmaz. İDEMPOTENT: geri-ileri gezinmede ikinci kanal açılmaz.</summary>
    private async Task CreateChannelAsync(WizardStepAdvanceContext context)
    {
        if (_channelId != Guid.Empty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_code) || string.IsNullOrWhiteSpace(_name)
            || string.IsNullOrWhiteSpace(_sellerId) || string.IsNullOrWhiteSpace(_apiKey)
            || string.IsNullOrWhiteSpace(_apiSecret))
        {
            UiService.ShowErrorToast(L["SalesChannelTrTrendyol:Wizard:CredentialsRequired"]);
            context.Cancel();
            return;
        }

        try
        {
            var created = await ChannelAppService.CreateAsync(new SalesChannelTrTrendyolCreateDto
            {
                Code = _code!,
                Name = _name!,
                SellerId = _sellerId!,
                ApiKey = _apiKey!,
                ApiSecret = _apiSecret!,
            });
            _channelId = created.Id;
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            context.Cancel();
        }
    }

    /// <summary>3. adım: mağaza çekimi. Salt GET; Trendyol'a yazma yok.</summary>
    private async Task RunImportAsync()
    {
        _busy = true;
        try
        {
            _import = await ProductAppService.ImportFromMarketplaceAsync(_channelId);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>4. adım: emtia sınıflandırmasını uygular (N11 sihirbazıyla AYNI sözleşme). Karar verilmemiş
    /// ürünler dokunulmadan Draft kalır; "Atla" ile geçilirse bu metot hiç koşmaz.</summary>
    private async Task ApplyClassificationAsync(WizardStepAdvanceContext context)
    {
        if (_classifyPanel is null)
        {
            return;
        }

        _classifyResult = await _classifyPanel.ApplyAsync();
        _classifyPending = _classifyPanel.PendingCount;
    }

    private Task GoToChannelAsync()
    {
        Navigation.NavigateTo($"/sales-channels/trendyol/{_channelId}");
        return Task.CompletedTask;
    }
}
