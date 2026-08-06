using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Components.Shared;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>
/// N11 satış kanalı KURULUM SİHİRBAZI.
///
/// <para><b>Neden gerekti:</b> kanalı açmak yetmiyordu. İçe aktarımın ihtiyaç duyduğu iki karar — hangi kargo
/// şablonu ve hangi KDV oranı — hiçbir yerde sorulmuyordu; çekim bu yüzden KDV'si boş, kargo şablonu tahmin
/// edilmiş kayıtlar üretiyor ve kullanıcıya bir düzeltme listesi bırakıyordu. Sihirbaz kararları ÇEKİMDEN ÖNCE
/// topluyor, böylece içe aktarım eksik kayıt yerine tam kayıt üretiyor.</para>
///
/// <para><b>Kanal 1. adımın SONUNDA kaydedilir</b>, sonda değil. "Her şeyi topla, sonda tek seferde yaz" daha
/// temiz görünür ama çalışmaz: şablon senkronu da içe aktarım da <c>salesChannelId</c> ZORUNLU ister, kanal
/// olmadan hiç koşamazlar. Yarıda bırakılırsa geriye kimliği DOĞRULANMIŞ çalışan bir kanal kalır (sunucu
/// create'te doğrular; geçmezse kayıt hiç açılmaz) — kullanıcı normal formdan devam eder, çöp kayıt olmaz.</para>
///
/// <para><b>MEVCUT kanalda da açılabilir</b> (<c>/sales-channels/n11/wizard/{Id}</c>) — 2026-08-05 düzeltmesi.
/// İlk hâli yalnız "Yeni kanal" yoluna bağlıydı ve bu, sihirbazı fiilen ULAŞILAMAZ kılıyordu: sistemde her
/// türden EN FAZLA BİR kanal var, dolayısıyla kanalını bir kez kuran kullanıcı "Yeni ▾"yi ebediyen kapalı
/// görüyordu. Kurulum tek seferlik bir olay olduğundan sihirbazın da yalnız o ana bağlanması hataydı; artık
/// "kurulumu gözden geçir/tamamla" olarak da çalışıyor.</para>
///
/// <para><b>Gider (yan-maliyet) adımı BİLİNÇLİ OLARAK YOK.</b> Kanal "Giderler" formu 2026-07-28'de yalnız
/// UI'dan değil DTO katmanından da kaldırıldı — çünkü boş ayar nesnesi (<c>{"Items":[]}</c>) "kullanıcı
/// komisyon satırını sildi" anlamına gelip komisyonu sessizce fiyattan düşürüyordu. Geri getirmek kendi
/// tasarımı ve testleri olan ayrı bir iştir; sihirbaz adımı olarak aceleye getirmek aynı hatayı davet ederdi.
/// Finansal olarak asıl önemli olan komisyon zaten N11 kategori oranından otomatik geliyor; kargo maliyeti de
/// artık şablondan (3. adım). Açıkta kalan tek kalem paketleme.</para>
/// </summary>
public partial class SalesChannelTrN11Wizard : CrudComponentBase
{
    /// <summary>MEVCUT kanalın kimliği — verilirse sihirbaz "kurulumu tamamla" kipinde açılır (kanal
    /// OLUŞTURULMAZ). Boşsa yeni kanal kurulumu.</summary>
    [Parameter] public Guid? Id { get; set; }

    [Inject] private ISalesChannelTrN11AppService ChannelAppService { get; set; } = default!;
    [Inject] private IN11CategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IN11ShipmentCompanyAppService ShipmentCompanyAppService { get; set; } = default!;
    [Inject] private IN11ShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] private ISalesChannelTrN11ProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // ── 1. adım: kimlik ─────────────────────────────────────────────────────────────────────────────
    private string? _code;
    private string? _name;
    private string? _appKey;
    private string? _appSecret;
    private Guid _channelId;

    // ── 2. adım: referans verisi ────────────────────────────────────────────────────────────────────
    private int? _categoryCount;
    private int? _carrierCount;

    // ── 3. adım: kargo şablonu ──────────────────────────────────────────────────────────────────────
    private List<N11ShipmentTemplateDto> _templates = new();
    private Guid? _selectedTemplateId;
    private decimal? _estimatedCost;

    // ── 4. adım: KDV ────────────────────────────────────────────────────────────────────────────────
    private int? _defaultVatRate;

    // ── 5. adım: içe aktarım ────────────────────────────────────────────────────────────────────────
    private N11ImportResultDto? _import;

    // ── 6. adım: emtia sınıflandırması ──────────────────────────────────────────────────────────────
    private ProductCommodityClassificationPanel? _classifyPanel;
    private int _classifyPending;
    private ProductCommodityProvisionResultDto? _classifyResult;

    private bool _busy;

    /// <summary>Sihirbaz MEVCUT bir kanal üzerinde mi çalışıyor (kimlik adımı kanal oluşturmaz).</summary>
    private bool IsExistingChannel
    {
        get { return Id is { } id && id != Guid.Empty; }
    }

    /// <summary>Mevcut kanal kipinde kanalı yükler — kimlik adımı hangi kanalda çalıştığını göstersin ve
    /// sonraki adımlar doğrudan onun üzerinde koşsun.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (!IsExistingChannel)
        {
            return;
        }

        try
        {
            var channel = await ChannelAppService.GetAsync(Id!.Value);
            _channelId = channel.Id;
            _code = channel.Code;
            _name = channel.Name;
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
    }

    /// <summary>KDV seçenekleri — küme SSOT'u pazaryeri sabitidir, bu ekran tanımlamaz.</summary>
    private static List<VatRateOption> VatOptions
    {
        get { return VatRateOption.From(N11ProductConsts.AllowedVatRates); }
    }

    private string SelectedTemplateName
    {
        get
        {
            return _templates.FirstOrDefault(t => t.Id == _selectedTemplateId)?.TemplateName
                   ?? L["SalesChannelTrN11:Wizard:NotChosen"].Value;
        }
    }

    private string VatLabel
    {
        get
        {
            return _defaultVatRate is { } rate
                ? VatOptions.First(o => o.Rate == rate).DisplayText
                : L["SalesChannelTrN11:Wizard:NotChosen"].Value;
        }
    }

    /// <summary>İçe aktarım raporunun sorunlu satırları — özet ekranına taşınmaz, çekim adımında gösterilir.</summary>
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
                .Concat(r.UnmatchedCategories.Select(c => $"{L["N11Product:Import:UnmatchedCategories"]}: {c}"))
                .ToList();
        }
    }

    /// <summary>Kurulum sonrası KALAN iş — sihirbaz "bitti" deyip kullanıcıyı eksik bir kurulumla baş başa
    /// bırakmasın. Her madde, o adımda atlanan/eksik kalan somut bir karardır.</summary>
    private List<string> RemainingWork
    {
        get
        {
            var items = new List<string>();
            if (_defaultVatRate is null)
            {
                items.Add(L["SalesChannelTrN11:Wizard:RemainingVat"].Value);
            }

            if (_selectedTemplateId is null || _estimatedCost is null)
            {
                items.Add(L["SalesChannelTrN11:Wizard:RemainingShipmentCost"].Value);
            }

            if (_import?.UnmatchedCategories.Count > 0)
            {
                items.Add(L["SalesChannelTrN11:Wizard:RemainingCategories", _import.UnmatchedCategories.Count].Value);
            }

            if (_import?.SkippedRows.Count > 0)
            {
                items.Add(L["SalesChannelTrN11:Wizard:RemainingSkipped", _import.SkippedRows.Count].Value);
            }

            // Sınıflandırma adımı ATLANABİLİR ama SESSİZ değildir: bağlanmayan ürünlerin stoğu Sabit kalır ve
            // pazaryerinin eski adedi geçerli olmayı sürdürür — kullanıcı bunu bilerek bırakmalı.
            if (_classifyPending > 0)
            {
                items.Add(L["SalesChannelTrN11:Wizard:RemainingCommodities", _classifyPending].Value);
            }

            return items;
        }
    }

    // ── Adım işleri ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>1. adım: kanalı OLUŞTUR. Kimlik doğrulaması SUNUCUDA yapılır (verifier) — geçmezse kayıt hiç
    /// açılmaz ve sihirbaz ilerlemez. İDEMPOTENT: kullanıcı geri gelip tekrar ileri derse ikinci kanal açılmaz.</summary>
    private async Task CreateChannelAsync(WizardStepAdvanceContext context)
    {
        // Mevcut kanal kipinde OLUŞTURMA YOK; zaten oluşturulduysa da (geri-ileri gezinme) ikinci kayıt açma.
        if (_channelId != Guid.Empty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_code) || string.IsNullOrWhiteSpace(_name)
            || string.IsNullOrWhiteSpace(_appKey) || string.IsNullOrWhiteSpace(_appSecret))
        {
            UiService.ShowErrorToast(L["SalesChannelTrN11:Wizard:CredentialsRequired"]);
            context.Cancel();
            return;
        }

        try
        {
            // IsActive create DTO'sunda YOK — entity aktif doğar (kanal kurulur kurulmaz kullanılabilir olmalı).
            var created = await ChannelAppService.CreateAsync(new SalesChannelTrN11CreateDto
            {
                Code = _code!,
                Name = _name!,
                AppKey = _appKey!,
                AppSecret = _appSecret!,
            });
            _channelId = created.Id;
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            context.Cancel();
        }
    }

    /// <summary>2. adım: host-global referans verisi. Kategori ağacı ve kargo firmaları TÜM tenant'ların
    /// paylaştığı taksonomilerdir; bir kez çekilir. Başarısızlık kurulumu DURDURMAZ (ürünler ham kategori
    /// id'siyle yine içe alınır) ama sessiz de geçilmez.</summary>
    private async Task SyncReferenceDataAsync(WizardStepAdvanceContext context)
    {
        try
        {
            _categoryCount ??= await CategoryAppService.SyncCategoriesAsync();
            _carrierCount ??= await ShipmentCompanyAppService.SyncAsync();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            context.Cancel();
        }
    }

    /// <summary>N11'deki kargo şablonlarını çeker (senkron) ve listeler. Tek şablon varsa kendiliğinden seçilir —
    /// kullanıcıyı tek seçenekli bir combo'ya tıklatmanın anlamı yok.</summary>
    private async Task PullTemplatesAsync()
    {
        _busy = true;
        try
        {
            await ShipmentTemplateAppService.SyncAsync(_channelId);
            _templates = await ShipmentTemplateAppService.GetListAsync(_channelId);
            _selectedTemplateId ??= _templates.Count == 1 ? _templates[0].Id : null;
            _estimatedCost ??= _templates.FirstOrDefault(t => t.Id == _selectedTemplateId)?.EstimatedCost;
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

    /// <summary>3. adım: seçilen şablona tahmini maliyeti yazar. Şablon/tutar seçilmemişse ilerlemeye İZİN VERİLİR
    /// (zorunlu değil) — eksik kalan iş özet ekranında listelenir. Yerel yazma; N11'e push yok.</summary>
    private async Task SaveShipmentCostAsync(WizardStepAdvanceContext context)
    {
        if (_selectedTemplateId is not { } templateId || _estimatedCost is null)
        {
            return;
        }

        try
        {
            await ShipmentTemplateAppService.SetEstimatedCostAsync(templateId, _estimatedCost, currencyUnitId: null);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            context.Cancel();
        }
    }

    /// <summary>5. adım: mağaza çekimi. Seçilen KDV oranı YENİ kayıtlara damgalanır. Salt GET; N11'e yazma yok.</summary>
    private async Task RunImportAsync()
    {
        _busy = true;
        try
        {
            _import = await ProductAppService.ImportFromMarketplaceAsync(_channelId, _defaultVatRate);
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

    /// <summary>6. adım: emtia sınıflandırmasını uygular. Karar verilmemiş ürünler DOKUNULMADAN kalır (Draft)
    /// ve özet ekranında sayılır — adım "Atla" ile geçilirse bu metot HİÇ koşmaz (WizardStep sözleşmesi),
    /// dolayısıyla atlamak da kararsız ürün bırakmakla aynı sonuca varır: satışa çıkmazlar.</summary>
    private async Task ApplyClassificationAsync(WizardStepAdvanceContext context)
    {
        if (_classifyPanel is null)
        {
            return;
        }

        _classifyResult = await _classifyPanel.ApplyAsync();
        _classifyPending = _classifyPanel.PendingCount;
    }

    /// <summary>Bitir → kurulan kanalın normal edit formuna geç (kullanıcı kaldığı yerden yönetmeye devam etsin).</summary>
    private Task GoToChannelAsync()
    {
        Navigation.NavigateTo($"/sales-channels/n11/{_channelId}");
        return Task.CompletedTask;
    }

    private string StatusText(int? count)
    {
        return count is { } value
            ? L["SalesChannelTrN11:Wizard:Synced", value].Value
            : L["SalesChannelTrN11:Wizard:NotSyncedYet"].Value;
    }
}
