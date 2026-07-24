using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Shipments;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Volo.Abp;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product edit host code-behind — coordinator kurulumu + yeni-kayıt varsayılanları + reçete drill'inin
/// katalog lookup verisi (host yükler; DUMB layout servis çağırmaz). Varyant ana değişmezi sunucuda
/// (ProductVariantManager); yeni ürün varyantsız açılır, kaydedince main otomatik doğar.</summary>
public partial class ProductEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IProductAppService ProductAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] protected IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] protected IFutureAppService FutureAppService { get; set; } = default!;
    [Inject] protected IJewelryAppService JewelryAppService { get; set; } = default!;
    [Inject] protected IGoodAppService GoodAppService { get; set; } = default!;
    [Inject] protected IStoneAppService StoneAppService { get; set; } = default!;
    [Inject] protected IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] protected IEffectivePriceAppService EffectivePriceAppService { get; set; } = default!;
    [Inject] protected ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] protected IAddOnAppService AddOnAppService { get; set; } = default!;
    [Inject] protected IShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] protected ISubstitutionGroupAppService SubstitutionGroupAppService { get; set; } = default!;
    [Inject] protected ISubstitutionCalculationAppService SubstitutionCalculationAppService { get; set; } = default!;
    [Inject] protected IServiceProvider ServiceProvider { get; set; } = default!;

    private ICommitCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto>? _coordinator;
    private bool _ready;

    // Reçete katalog lookup verisi — açılışta bir kez yüklenir (varyant reçete drill'lerinin ortak beslemesi).
    protected IReadOnlyList<MetalListDto> Metals { get; private set; } = Array.Empty<MetalListDto>();
    protected IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; private set; } = Array.Empty<MetalVariantLookupDto>();
    protected IReadOnlyList<ScrapListDto> Scraps { get; private set; } = Array.Empty<ScrapListDto>();
    protected IReadOnlyList<FutureListDto> Futures { get; private set; } = Array.Empty<FutureListDto>();
    protected IReadOnlyList<JewelryListDto> Jewelries { get; private set; } = Array.Empty<JewelryListDto>();
    protected IReadOnlyList<GoodListDto> Goods { get; private set; } = Array.Empty<GoodListDto>();
    protected IReadOnlyList<StoneListDto> Stones { get; private set; } = Array.Empty<StoneListDto>();
    protected IReadOnlyList<ServiceListDto> Services { get; private set; } = Array.Empty<ServiceListDto>();
    protected IReadOnlyList<CurrentPriceDto> Units { get; private set; } = Array.Empty<CurrentPriceDto>();

    // Varsayılan para birimi lookup verisi — inline ekle/düzelt sonrası ReloadCurrencyUnitsAsync ile tazelenir.
    protected IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; private set; } = Array.Empty<CurrencyUnitListDto>();

    // Eklenti katalogu lookup verisi ("Seçenekler" sekmesi) — inline ekle/düzelt sonrası ReloadAddOnsAsync ile tazelenir.
    protected IReadOnlyList<AddOnListDto> AddOns { get; private set; } = Array.Empty<AddOnListDto>();

    // Kargo şablonu lookup verisi (varsayılan kargo şablonu ataması) — inline ekle/düzelt sonrası ReloadShipmentTemplatesAsync ile tazelenir.
    protected IReadOnlyList<ShipmentTemplateListDto> ShipmentTemplates { get; private set; } = Array.Empty<ShipmentTemplateListDto>();

    // ── Muadil (Substitution) modu durumu (Dilim-3) — grup lookup'u + seçili grubun kalemleri (override
    //    ağacının devralınan-küme referansı) + son hesap sonucu. Layout DUMB; iş burada. ──
    protected IReadOnlyList<SubstitutionGroupListDto> SubstitutionGroups { get; private set; } = Array.Empty<SubstitutionGroupListDto>();
    protected List<SubstitutionGroupItemGraphDto> SubstitutionGroupItems { get; private set; } = new();
    protected SubstitutionCalculationResultDto? SubstitutionResult { get; private set; }
    protected bool SubstitutionBusy { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto, ProductCreateDto, ProductUpdateDto>(
            ProductAppService, Mapper);

        await LoadRecipeCatalogsAsync();
        _ready = true;
    }

    // Reçete satırlarının lookup beslemesi (aktif katalog kayıtları + birimler). Server working-company ile scope'lar.
    private async Task LoadRecipeCatalogsAsync()
    {
        Metals = await MetalAppService.GetPickerListAsync();
        MetalVariants = await MetalAppService.GetVariantLookupAsync();
        Scraps = await ScrapAppService.GetPickerListAsync();
        Futures = await FutureAppService.GetPickerListAsync();
        Jewelries = await JewelryAppService.GetPickerListAsync();
        Goods = await GoodAppService.GetPickerListAsync();
        Stones = await StoneAppService.GetPickerListAsync();
        Services = await ServiceAppService.GetPickerListAsync();
        Units = await EffectivePriceAppService.GetCurrentPricesAsync();
        CurrencyUnits = await CurrencyLookup.GetAsync();
        AddOns = await AddOnAppService.GetPickerListAsync();
        ShipmentTemplates = await ShipmentTemplateAppService.GetPickerListAsync();
        SubstitutionGroups = await LoadActiveSubstitutionGroupsAsync();
    }

    // Yalnız AKTİF gruplar seçilebilir (pasif grup sunucuda da fail-fast — hesaplama sayfası deseni).
    private async Task<IReadOnlyList<SubstitutionGroupListDto>> LoadActiveSubstitutionGroupsAsync()
    {
        var result = await SubstitutionGroupAppService.GetListAsync(
            new SubstitutionGroupListRequestDto { IsActive = true, MaxResultCount = 200 });
        return result.Items.ToList();
    }

    // Inline muadil grubu ekle/düzelt sonrası lookup listesini tazeler (yeni grup anında combo'ya düşsün).
    private async Task ReloadSubstitutionGroupsAsync()
    {
        SubstitutionGroups = await LoadActiveSubstitutionGroupsAsync();
        StateHasChanged();
    }

    /// <summary>Grup seçimi değişti / ilk yükleme — seçili grubun kalemleri (override ağacının devralınan-küme
    /// referansı) yüklenir; eski hesap sonucu bayatladı → temizlenir. Grup bulunamazsa (silinmiş) boş kalır.</summary>
    private async Task OnSubstitutionGroupChangedAsync(Guid? groupId)
    {
        SubstitutionResult = null;
        if (groupId is not { } id)
        {
            SubstitutionGroupItems = new List<SubstitutionGroupItemGraphDto>();
            StateHasChanged();
            return;
        }

        try
        {
            SubstitutionGroupItems = (await SubstitutionGroupAppService.GetAsync(id)).Items;
        }
        catch (Exception ex)
        {
            // Grup artık yok/görünmez (başka oturumda silinmiş olabilir) — ağaç boş kalır, kaydetme sunucuda
            // doğrulanır; neden GİZLENMEZ (toast — sessiz yutma yok).
            SubstitutionGroupItems = new List<SubstitutionGroupItemGraphDto>();
            UiService.ShowErrorToast(
                CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }

        StateHasChanged();
    }

    /// <summary>Varyant modu değişim isteği — MultiVariant'tan çıkışta kaybolacak veri (nitelikler + çoklu
    /// varyantlar) varsa ONAY istenir: kaydetmede sunucu nitelik grafını boşaltır, synchronizer tek ana varyanta
    /// indirir (VERİ SİLİNİR). Onaylanmazsa model değişmez (combo eski değere geri çizilir).</summary>
    private async Task HandleVariantModeChangeAsync(ProductGetDto model, ProductVariantMode newMode)
    {
        if (model.VariantMode == newMode)
        {
            return;
        }

        var losesVariants = model.VariantMode == ProductVariantMode.MultiVariant
            && (model.Attributes.Any(a => !a.IsDeleted) || model.Variants.Count(v => !v.IsDeleted) > 1);
        if (losesVariants)
        {
            var confirm = await UiService.ConfirmAsync(
                L["Product:VariantModeCollapseWarning"].Value,
                L["Product:VariantModeChangeTitle"].Value,
                L["Product:VariantModeCollapseYes"].Value,
                L["Cancel"].Value,
                showCancel: false,
                defaultYes: false);
            if (confirm != ConfirmDialogResult.Yes)
            {
                StateHasChanged();
                return;
            }
        }

        model.VariantMode = newMode;
        if (newMode != ProductVariantMode.Substitution)
        {
            SubstitutionResult = null;
        }
    }

    /// <summary>"Kombinasyon Hesapla" — ürünün kalıcı muadil konfigürasyonuyla (grup + hedef + tolerans override +
    /// varyant override kümesi) CalculateAsync koşulur. Hata yolu hesaplama sayfasıyla aynı (CrudErrorPresenter).</summary>
    private async Task CalculateSubstitutionAsync(ProductGetDto model)
    {
        if (model.SubstitutionGroupId is not { } groupId
            || model.SubstitutionTargetQuantity is not { } target
            || target <= 0m)
        {
            return;
        }

        SubstitutionBusy = true;
        try
        {
            SubstitutionResult = await SubstitutionCalculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId    = groupId,
                TargetQuantity         = target,
                ToleranceTypeOverride  = model.SubstitutionToleranceType,
                ToleranceValueOverride = model.SubstitutionToleranceValue,
                OverrideVariantIds     = model.SubstitutionOverrideVariantIds.ToList(),
            });
        }
        catch (Exception ex)
        {
            // BusinessException'ı error boundary'e düşürme — in-process mesaj lokalize gelmez,
            // CrudErrorPresenter kodu çevirir (hesaplama sayfası deseni).
            UiService.ShowErrorToast(
                CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["Substitution:CalculationFailed"].Value);
        }
        finally
        {
            SubstitutionBusy = false;
            StateHasChanged();
        }
    }

    /// <summary>Seçilen BAŞARILI kombinasyonu ana varyantın reçetesine uygular — kombinasyon reçetenin SAHİBİDİR
    /// (kanal köprüsü ReplaceChannelRecipeLinesAsync semantiği): mevcut satırlar temizlenir (DB'liler IsDeleted —
    /// graf-save siler), kombinasyon satırları TAZE eklenir. Persist ürün Kaydet'iyle (SaveRecipeLinesAsync yolu);
    /// satır kurulumu sunucu BuildRecipeLineDtos'un lookup'lı istemci karşılığıdır (aynı alan kümesi).</summary>
    private async Task ApplySubstitutionTrialAsync(ProductGetDto model, SubstitutionTrialDto trial)
    {
        var variant = model.Variants.FirstOrDefault(v => !v.IsDeleted && v.IsMain)
            ?? model.Variants.FirstOrDefault(v => !v.IsDeleted);
        if (variant is null || !trial.Success)
        {
            return;
        }

        foreach (var line in variant.RecipeLines.Where(l => !l.IsDeleted).ToList())
        {
            if (line.Id == Guid.Empty)
            {
                variant.RecipeLines.Remove(line);   // henüz DB'de yok → listeden çıkar
            }
            else
            {
                line.IsDeleted = true;              // DB'de var → graf-save siler (Id + IsDeleted diff)
            }
        }

        var order = 0;
        foreach (var trialLine in trial.Lines)
        {
            variant.RecipeLines.Add(BuildTrialRecipeLine(trialLine, order++));
        }

        await RecalcVariantCostAsync(variant);
        UiService.ShowSuccessToast(L["Product:TrialAppliedToRecipe"].Value);
    }

    /// <summary>Kombinasyon satırı → reçete graf satırı (metal bacağı Quantity/Amount/Factor/doğal birim + işçilik
    /// bacağı EntryLabor@birim) — SubstitutionChannelPlanProvider.BuildRecipeLineDtos ile AYNI alan kurulumu;
    /// katalog verisi host'un yüklü lookup'larından (Metals + MetalVariants) çözülür.</summary>
    private ProductRecipeLineGraphDto BuildTrialRecipeLine(SubstitutionTrialLineDto trialLine, int order)
    {
        var metal = Metals.FirstOrDefault(m => m.Id == trialLine.MetalId);
        var metalVariant = trialLine.VariantId is { } variantId
            ? MetalVariants.FirstOrDefault(v => v.VariantId == variantId)
            : null;

        return new ProductRecipeLineGraphDto
        {
            LineOrder            = order,
            ComponentType        = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId          = trialLine.MetalId,
            CommodityVariantId   = trialLine.VariantId,
            Quantity             = trialLine.Count,
            Amount               = trialLine.Count * trialLine.PieceWeight,
            Factor               = metal?.Factor ?? 1m,
            ValuationUnitId      = metal?.FollowingUnitId,
            PaymentType          = ProcessPaymentType.Normal,
            PayFactor            = metalVariant?.EntryLabor ?? 0m,
            PayUnitId            = metalVariant?.EntryLaborUnitId ?? metal?.FollowingUnitId,
        };
    }

    // Inline döviz ekle/düzelt sonrası lookup listesini tazeler (yeni birim anında combo'ya düşsün).
    private async Task ReloadCurrencyUnitsAsync()
    {
        CurrencyLookup.Invalidate();
        CurrencyUnits = await CurrencyLookup.GetAsync();
        StateHasChanged();
    }

    // Inline eklenti ekle/düzelt sonrası katalog listesini tazeler (yeni eklenti anında combo'ya düşsün).
    private async Task ReloadAddOnsAsync()
    {
        AddOns = await AddOnAppService.GetPickerListAsync();
        StateHasChanged();
    }

    // Inline kargo şablonu ekle/düzelt sonrası lookup listesini tazeler (yeni şablon anında combo'ya düşsün).
    private async Task ReloadShipmentTemplatesAsync()
    {
        ShipmentTemplates = await ShipmentTemplateAppService.GetPickerListAsync();
        StateHasChanged();
    }

    // Yeni kayıt: aktif + gridde görünsün diye base ANA VARYANT satırı seed'lenir (SABİT kimlik ANAVARYANT/Ana
    // Varyant; ProductConsts SSOT). Bu satır kaydedince sunucunun ProductVariantManager ile yarattığı DB main'e
    // eşlenir (AppService ResolveTargetVariant: Id yok + IsMain + kombinasyon yok → DB main) → Yeni'de girilen
    // reçete ana varyanta yazılır. Attribute'lu üretimde bu satır synchronizer tarafından kombinasyonlarla değişir.
    private void ApplyNew(ProductGetDto m)
    {
        m.IsActive = true;
        m.Variants.Add(new ProductVariantGraphDto
        {
            IsMain = true,
            IsActive = true,
            Code = ProductConsts.MainVariantCode,
            Name = ProductConsts.MainVariantName,
        });
    }

    // "Varyantları Oluştur" — layout DUMB kalır (servis inject etmez), çağrıyı host yapar. PERSISTSİZ önizleme:
    // sunucu nitelik grafından kartezyeni hesaplar, dönen graf Model.Variants'a yazılır (kalıcılaşma Save'de).
    private async Task GenerateVariantsAsync(ProductGetDto model)
    {
        // Mod kapısı (Dilim-3): SingleVariant/Muadil'de nitelik-tabanlı üretim BYPASS — host guard (buton zaten
        // görünmez; savunma). Sunucu kapısı AYRICA ŞART (client güven sınırı değildir — SaveVariantGraphAsync).
        if (model.VariantMode != ProductVariantMode.MultiVariant)
        {
            return;
        }

        // Değeri olmayan (silinmemiş) nitelik varsa kartezyen tanımsız (kullanıcı hâlâ değer ekliyor) → otomatik regen ATLA.
        if (VariantGraphMerge.HasIncompleteAttribute(model.Attributes))
        {
            return;
        }

        try
        {
            var generated = await ProductAppService.GenerateVariantsAsync(new ProductVariantGenerateRequestDto
            {
                ProductName = model.Name,
                Attributes = model.Attributes,
            });

            // MERGE: mevcut varyantların kullanıcı düzenlemeleri (fiyat/reçete/barkod/Id + uzantı alanları) CombinationKey
            // ile KORUNUR; yalnız türetilen alanlar (Kod/Ad/Özet/IsMain) tazelenir → otomatik senkron veri kaybetmez (Good deseni).
            VariantGraphMerge.Apply(model.Variants, generated);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir
            // (ör. TradeXpress:EntityAttribute:ValueRequired); anahtar yoksa kodun kendisi görünür.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }
    }

    // Reçete değişince CANLI maliyet (TAM KAYIT gerekmez): varyantın satırlarını PERSISTSİZ hesaplat → varyantı
    // (NetCost + satır maliyet alanları) güncelle → yeniden çiz. Dumb layout servis çağırmaz; iş host'ta.
    private async Task RecalcVariantCostAsync(ProductVariantGraphDto variant)
    {
        var result = await ProductAppService.CalculateRecipeCostAsync(
            new ProductRecipeCostRequestDto { Lines = variant.RecipeLines });

        variant.NetCost = result.NetCost;
        variant.NetCostCurrency = result.NetCostCurrency;
        variant.NetCostMissingRate = result.NetCostMissingRate;
        ApplyLineCosts(variant.RecipeLines, result.Lines);
        StateHasChanged();
    }

    // Hesaplanan satır maliyetlerini in-memory satırlara ClientKey ile uygular — in-process aynı nesne ise no-op
    // (Blazor Server; PopulateRecipeCostsAsync satırları zaten yerinde günceller), aksi halde savunmalı kopya.
    private static void ApplyLineCosts(
        List<ProductRecipeLineGraphDto> target, List<ProductRecipeLineGraphDto> computed)
    {
        var byKey = computed.ToDictionary(l => l.ClientKey);
        foreach (var l in target)
        {
            if (!byKey.TryGetValue(l.ClientKey, out var r) || ReferenceEquals(l, r))
            {
                continue;
            }

            l.LineCost = r.LineCost;
            l.LineCostMissingRate = r.LineCostMissingRate;
            l.Total = r.Total;
            l.PayTotal = r.PayTotal;
            l.AppliedBase = r.AppliedBase;
            l.RunningSubtotal = r.RunningSubtotal;
            l.MainUnitCode = r.MainUnitCode;
            l.PayUnitCode = r.PayUnitCode;
        }
    }
}
