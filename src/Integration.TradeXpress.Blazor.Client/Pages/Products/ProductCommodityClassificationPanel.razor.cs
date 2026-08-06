using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// İÇE AKTARILAN ÜRÜNLERİ EMTİAYA BAĞLAMA PANELİ — satış kanalı sihirbazlarının ORTAK adımı
/// (2026-08-05 Hakan kararları).
///
/// <para><b>Neden ortak bileşen:</b> adımın başlığı kanal diline göre değişir ama İŞİ aynıdır. Kanal başına
/// kopyalamak, ilk düzeltmede üç yerden ikisinin unutulmasıyla biterdi.</para>
///
/// <para><b>Kanal parametresi ALMAZ:</b> aday listesi "çalışılan şirkette reçetesiz ürünler" sorgusundan
/// gelir. Yalnız "bu turda içe aktarılanlar"a bakmak, geçmişte atlanmış ürünleri sonsuza dek görünmez
/// kılardı — oysa asıl mağdur onlar (canlıda 103 ürün bu durumda).</para>
///
/// <para><b>Sınıflandırma MANUELDİR:</b> yazılım "bu bilezik hangi madenden" sorusunu tahmin etmez. Toplu
/// atama kullanıcının bilinçli eylemidir (seç → karar kur → uygula), otomatik varsayılan DEĞİLDİR.</para>
///
/// <para><b>Atlanabilir adım:</b> atlanan ürünler <c>Draft</c> kalır ve satışa çıkmaz — güvenlik
/// zorunluluktan değil STATÜDEN gelir. Ama sessiz de değildir: sihirbazın özet adımı kalan sayıyı yazar.</para>
/// </summary>
public partial class ProductCommodityClassificationPanel : CrudComponentBase
{
    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] private IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] private IFutureAppService FutureAppService { get; set; } = default!;
    [Inject] private IJewelryAppService JewelryAppService { get; set; } = default!;
    [Inject] private IStoneAppService StoneAppService { get; set; } = default!;
    [Inject] private IGoodAppService GoodAppService { get; set; } = default!;
    [Inject] private IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>Karar verilmemiş aday sayısı değiştiğinde bildirir. Sihirbaz bu sayıyı SAKLAR: adım pasifken
    /// bileşen hiç render edilmediğinden (WizardStep sözleşmesi) özet ekranı paneli sorgulayamaz.</summary>
    [Parameter] public EventCallback<int> PendingChanged { get; set; }

    private List<ProductCommodityCandidateDto> _candidates = new();
    private IReadOnlyList<object> _selected = new List<object>();

    /// <summary>Ürün-başına verilmiş karar. Kararı OLMAYAN aday sunucuya GÖNDERİLMEZ.</summary>
    private readonly Dictionary<Guid, ProductCommodityProvisionItemDto> _decisions = new();

    private readonly List<string> _issues = new();

    // ── Araç çubuğu kararı (seçilenlere basılacak şablon) ───────────────────────────────────────────
    private ProcessType _family = ProcessType.Good;
    private ProductCommodityProvisionMode _mode = ProductCommodityProvisionMode.CreateNew;
    private Guid? _existingCommodityId;
    private Guid? _followingUnitId;
    private decimal _amount;
    private decimal _quantity = 1m;

    private List<CommodityOption> _existingCommodities = new();
    private List<CommodityOption> _currencyUnits = new();

    /// <summary>Karar verilmemiş aday sayısı — sihirbazın "kalan iş" listesinin kaynağı.</summary>
    public int PendingCount
    {
        get { return _candidates.Count(c => !_decisions.ContainsKey(c.ProductId)); }
    }

    /// <summary>Hiç aday var mı — sihirbaz adımı boşsa kullanıcıya "her şey bağlı" der.</summary>
    public int CandidateCount
    {
        get { return _candidates.Count; }
    }

    private bool RequiresFollowingUnit
    {
        get
        {
            return _mode == ProductCommodityProvisionMode.CreateNew
                   && _family is ProcessType.Metal or ProcessType.Scrap or ProcessType.Future;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
    }

    /// <summary>Aday listesini (ve gerekiyorsa lookup'ları) tazeler. Sihirbaz içe aktarımdan SONRA çağırır —
    /// yeni gelen ürünler listede belirsin.</summary>
    public async Task ReloadAsync()
    {
        try
        {
            _candidates = await ProductAppService.GetUnclassifiedProductsAsync();
            _selected = new List<object>();
            _decisions.Keys.Except(_candidates.Select(c => c.ProductId)).ToList()
                .ForEach(id => _decisions.Remove(id));

            if (_currencyUnits.Count == 0)
            {
                var units = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 200 });
                _currencyUnits = units.Items
                    .Select(u => new CommodityOption(u.Id, u.Code))
                    .ToList();
            }

            await LoadExistingCommoditiesAsync();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }

        await NotifyPendingAsync();
        StateHasChanged();
    }

    private async Task NotifyPendingAsync()
    {
        if (PendingChanged.HasDelegate)
        {
            await PendingChanged.InvokeAsync(PendingCount);
        }
    }

    /// <summary>Kararları sunucuya uygular. Karar verilmemiş ürünler DOKUNULMADAN kalır (Draft) — sihirbaz
    /// onları özet ekranında sayar.</summary>
    public async Task<ProductCommodityProvisionResultDto?> ApplyAsync()
    {
        _issues.Clear();
        if (_decisions.Count == 0)
        {
            return null;
        }

        try
        {
            var result = await ProductAppService.ProvisionCommoditiesAsync(new ProductCommodityProvisionInputDto
            {
                Items = _decisions.Values.ToList(),
            });

            _issues.AddRange(result.Issues);
            await ReloadAsync();
            return result;
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            return null;
        }
    }

    // ── Araç çubuğu ─────────────────────────────────────────────────────────────────────────────────

    private async Task OnFamilyChanged(ProcessType value)
    {
        _family = value;
        _existingCommodityId = null;
        await LoadExistingCommoditiesAsync();
    }

    private async Task OnModeChanged(ProductCommodityProvisionMode value)
    {
        _mode = value;
        await LoadExistingCommoditiesAsync();
    }

    /// <summary>Seçili satırlara araç çubuğundaki kararı basar. EKSİK karar KAYDEDİLMEZ: zorunlu alanı
    /// boş bir satırı "karar verilmiş" saymak, sunucuda atlanıp kullanıcının haberi olmadan Draft kalmasına
    /// yol açardı.</summary>
    private async Task ApplyToSelected()
    {
        if (_mode == ProductCommodityProvisionMode.UseExisting && _existingCommodityId is null)
        {
            UiService.ShowErrorToast(L["Product:Classify:ExistingRequired"]);
            return;
        }

        if (RequiresFollowingUnit && _followingUnitId is null)
        {
            UiService.ShowErrorToast(L["Product:Classify:FollowingUnitRequired"]);
            return;
        }

        // Hizmet DIŞINDAKİ ailelerde en az bir boyut kısıt getirmeli; ikisi de 0 ise reçete satırı stoğu
        // hiç kısıtlamaz ve ürün "sınırsız üretilebilir" görünür — sessiz yanlış rakam.
        if (_family != ProcessType.Service && _amount <= 0m && _quantity <= 0m)
        {
            UiService.ShowErrorToast(L["Product:Classify:RequirementRequired"]);
            return;
        }

        foreach (var candidate in SelectedCandidates())
        {
            _decisions[candidate.ProductId] = new ProductCommodityProvisionItemDto
            {
                ProductId           = candidate.ProductId,
                Family              = _family,
                Mode                = _mode,
                ExistingCommodityId = _mode == ProductCommodityProvisionMode.UseExisting ? _existingCommodityId : null,
                FollowingUnitId     = RequiresFollowingUnit ? _followingUnitId : null,
                Code                = candidate.Code,
                Name                = candidate.Name,
                Amount              = _family == ProcessType.Service ? 0m : _amount,
                Quantity            = _family == ProcessType.Service ? 0m : _quantity,
            };
        }

        await NotifyPendingAsync();
    }

    private async Task ClearSelected()
    {
        foreach (var candidate in SelectedCandidates())
        {
            _decisions.Remove(candidate.ProductId);
        }

        await NotifyPendingAsync();
    }

    private IEnumerable<ProductCommodityCandidateDto> SelectedCandidates()
    {
        return _selected.OfType<ProductCommodityCandidateDto>().ToList();
    }

    // ── Görünüm ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Satırın kararı — karar yoksa "bağlanmadı" der. Boş bırakmak, kullanıcıya kararlı satırla
    /// kararsızı aynı gösterirdi.</summary>
    private string DecisionText(ProductCommodityCandidateDto candidate)
    {
        if (!_decisions.TryGetValue(candidate.ProductId, out var decision))
        {
            return L["Product:Classify:NotDecided"].Value;
        }

        var familyLabel = L[$"Enum:ProcessType:{decision.Family}"].Value;
        if (decision.Family == ProcessType.Service)
        {
            return $"{familyLabel} — {L["Product:Classify:UnlimitedStock"]}";
        }

        var target = decision.Mode == ProductCommodityProvisionMode.UseExisting
            ? _existingCommodities.FirstOrDefault(c => c.Id == decision.ExistingCommodityId)?.Label
              ?? L["Product:Classify:ExistingCommodity"].Value
            : L["Product:Classify:NewCommodity"].Value;

        return $"{familyLabel} · {target} · {decision.Amount:N3} / {decision.Quantity:N0}";
    }

    private async Task LoadExistingCommoditiesAsync()
    {
        if (_mode != ProductCommodityProvisionMode.UseExisting)
        {
            _existingCommodities = new List<CommodityOption>();
            return;
        }

        try
        {
            _existingCommodities = _family switch
            {
                ProcessType.Metal   => (await MetalAppService.GetListAsync(new MetalListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Scrap   => (await ScrapAppService.GetListAsync(new ScrapListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Future  => (await FutureAppService.GetListAsync(new FutureListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Jewelry => (await JewelryAppService.GetListAsync(new JewelryListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Stone   => (await StoneAppService.GetListAsync(new StoneListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Good    => (await GoodAppService.GetListAsync(new GoodListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                ProcessType.Service => (await ServiceAppService.GetListAsync(new ServiceListRequestDto { MaxResultCount = 500 }))
                    .Items.Select(x => new CommodityOption(x.Id, $"{x.Code} — {x.Name}")).ToList(),
                _                   => new List<CommodityOption>(),
            };
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
            _existingCommodities = new List<CommodityOption>();
        }
    }

    /// <summary>Sınıflandırılabilir aileler — YEDİSİ DE (2026-08-05 Hakan: <i>"Sadece 4 emtia ile sınırlamak
    /// doğru olmaz"</i>).</summary>
    private List<CommodityFamilyOption> FamilyOptions
    {
        get
        {
            return new List<CommodityFamilyOption>
            {
                new(ProcessType.Metal,   L["Enum:ProcessType:Metal"].Value),
                new(ProcessType.Scrap,   L["Enum:ProcessType:Scrap"].Value),
                new(ProcessType.Future,  L["Enum:ProcessType:Future"].Value),
                new(ProcessType.Jewelry, L["Enum:ProcessType:Jewelry"].Value),
                new(ProcessType.Stone,   L["Enum:ProcessType:Stone"].Value),
                new(ProcessType.Good,    L["Enum:ProcessType:Good"].Value),
                new(ProcessType.Service, L["Enum:ProcessType:Service"].Value),
            };
        }
    }

    private List<ProvisionModeOption> ModeOptions
    {
        get
        {
            return new List<ProvisionModeOption>
            {
                new(ProductCommodityProvisionMode.CreateNew,   L["Product:Classify:Mode:CreateNew"].Value),
                new(ProductCommodityProvisionMode.UseExisting, L["Product:Classify:Mode:UseExisting"].Value),
            };
        }
    }

    /// <summary>Combo seçeneği — aile.</summary>
    public sealed record CommodityFamilyOption(ProcessType Family, string Label);

    /// <summary>Combo seçeneği — mod.</summary>
    public sealed record ProvisionModeOption(ProductCommodityProvisionMode Mode, string Label);

    /// <summary>Combo seçeneği — emtia ya da birim (id + görünen etiket).</summary>
    public sealed record CommodityOption(Guid Id, string Label);
}
