using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Kategori attribute grid'inin satırı — HÜCRE-İÇİ düzenleme (Trendyol EditCell deseniyle AYNI). Değer
/// listeli attribute'larda <see cref="SelectedValues"/> (DxTagBox ÇOKLU seçim — N11 WSDL "aynı isim, ayrı eleman"
/// temsiliyle birebir örtüşür); serbest-metin attribute'larda tek değerli <see cref="CustomValue"/>. DxGrid edit-model
/// klonu için public parametresiz ctor + set'li property'ler.</summary>
public class N11AttributeCellRow
{
    public string AttributeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    /// <summary>Değer listesi var + serbest-değil → DxTagBox (çoklu); aksi halde serbest metin (tekli).</summary>
    public bool HasValueList { get; set; }

    public List<N11CategoryAttributeValueDto> DefinitionValues { get; set; } = new();
    public List<string> SelectedValues { get; set; } = new();
    public string CustomValue { get; set; } = string.Empty;
}

/// <summary>N11 ürün listeleme edit alanları — kanal + kategori (kademeli) + kategori attribute'ları (on-demand) +
/// kargo şablonu + condition + Seyahat özel bilgileri + N11 senkron durumu. Listeleme drill'inin EditContent'i;
/// kendi DxFormLayout'unu sağlar. ValueExpression'sız editörlerde dirty EDitContext'e elle bildirilir.</summary>
public partial class SalesChannelTrN11ProductEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelTrN11ProductDto Model { get; set; } = default!;

    /// <summary>Kanal AD çözümü beslemesi (yalnız N11 kanalları) — kanal HER ZAMAN salt-okunur gösterilir
    /// (create'te otomatik atanır, set-once); seçici yok.</summary>
    [Parameter] public IReadOnlyList<SalesChannelListDto> Channels { get; set; } = Array.Empty<SalesChannelListDto>();

    [Inject] private IN11ShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] private IN11CategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private ISalesChannelTrN11ProductAppService ProductAppService { get; set; } = default!;
    [Inject] private ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Reçete drill'inin katalog lookup beslemesi + persistsiz maliyet motoru (ERP ile ORTAK: aynı entity-agnostik
    // hesap uçları; kanal varyant reçetesi de ProductRecipeLineGraphDto olduğundan Product AppService yeniden kullanılır).
    [Inject] private IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] private IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] private IFutureAppService FutureAppService { get; set; } = default!;
    [Inject] private IJewelryAppService JewelryAppService { get; set; } = default!;
    [Inject] private IStoneAppService StoneAppService { get; set; } = default!;
    [Inject] private IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService EffectivePriceAppService { get; set; } = default!;
    [Inject] private IProductAppService RecipeCostAppService { get; set; } = default!;

    [CascadingParameter] private EditContext? EditContext { get; set; }

    private List<N11ShipmentTemplateDto> _templates = new();
    private List<N11CategoryAttributeDto> _attributeDefs = new();

    // N11 para birimi lookup verisi (döviz cache; inline ekle/düzelt sonrası ReloadCurrencyUnitsAsync ile tazelenir).
    private List<CurrencyUnitListDto> _units = new();
    private bool _unitsLoaded;

    // Kanal-özel varyant override drill'i (satır düzenleme aç/kapa) + reçete katalog lookup verisi (bir kez yüklenir).
    private DrillList<SalesChannelTrN11ProductVariantGraphDto>? _variantDrill;
    private IReadOnlyList<MetalListDto> _metals = Array.Empty<MetalListDto>();
    private IReadOnlyList<ScrapListDto> _scraps = Array.Empty<ScrapListDto>();
    private IReadOnlyList<FutureListDto> _futures = Array.Empty<FutureListDto>();
    private IReadOnlyList<JewelryListDto> _jewelries = Array.Empty<JewelryListDto>();
    private IReadOnlyList<StoneListDto> _stones = Array.Empty<StoneListDto>();
    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private IReadOnlyList<CurrentPriceDto> _priceUnits = Array.Empty<CurrentPriceDto>();
    private bool _catalogsLoaded;

    // Kategori attribute grid satırları (def + o anki değerler) — hücre-içi düzenleme, paging yok.
    private List<N11AttributeCellRow> _attributeRows = new();

    // Nitelik Eksenleri drill'i (varyant ÜRETİMİ amaçlı — kategori-attribute-push'tan AYRI) — üst = eksen
    // (Model.AttributeAxes, ilk açılışta ERP'den klonlanmış taslak gelir), alt = eksen değerleri. İkisi de serbest
    // ekle/sil (klon-sonra-ayrış felsefesi).
    private DrillList<SalesChannelTrN11ProductAttributeAxisDto>? _axisDrill;
    private DrillList<SalesChannelTrN11ProductAttributeAxisValueDto>? _axisValueDrill;

    // OnParametersSetAsync her render'da çalışır → tekrarlı ağ çağrısını son-yüklenen anahtarla önle.
    private Guid _loadedTemplatesChannelId;
    private string? _loadedAttributesCategoryId;

    // Push önizlemesi (read-only) — N11'e gidecek varyantlar + görseller (kaynak ERP ürünü, SSOT).
    private N11PushPreviewDto? _preview;
    private Guid _loadedPreviewId;

    private string ChannelName =>
        Channels.FirstOrDefault(c => c.Id == Model.SalesChannelId)?.Code ?? Model.SalesChannelId.ToString();

    protected override async Task OnParametersSetAsync()
    {
        await EnsureCurrencyUnitsAsync();
        await EnsureRecipeCatalogsAsync();
        await EnsureTemplatesAsync();
        await EnsureAttributesAsync();
        await EnsurePreviewAsync();
    }

    // Reçete satırı lookup beslemesi (aktif katalog + birimler) — bir kez yüklenir; server working-company ile scope'lar.
    private async Task EnsureRecipeCatalogsAsync()
    {
        if (_catalogsLoaded)
        {
            return;
        }

        _catalogsLoaded = true;
        try
        {
            _metals = await MetalAppService.GetPickerListAsync();
            _scraps = await ScrapAppService.GetPickerListAsync();
            _futures = await FutureAppService.GetPickerListAsync();
            _jewelries = await JewelryAppService.GetPickerListAsync();
            _stones = await StoneAppService.GetPickerListAsync();
            _services = await ServiceAppService.GetPickerListAsync();
            _priceUnits = await EffectivePriceAppService.GetCurrentPricesAsync();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // N11 para birimi lookup listesini bir kez yükler (döviz cache TTL + auto-invalidate).
    private async Task EnsureCurrencyUnitsAsync()
    {
        if (_unitsLoaded)
        {
            return;
        }

        _unitsLoaded = true;
        try
        {
            _units = new List<CurrencyUnitListDto>(await CurrencyLookup.GetAsync());
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Inline döviz ekle/düzelt sonrası lookup listesini tazeler (yeni birim anında combo'ya düşsün).
    private async Task ReloadCurrencyUnitsAsync()
    {
        CurrencyLookup.Invalidate();
        _units = new List<CurrencyUnitListDto>(await CurrencyLookup.GetAsync());
        StateHasChanged();
    }

    // Özel bilgi satırı kaydetme engeli — key boşsa satır kabul edilmez (SetSpecialInfo sunucuda da boş key eler).
    private string? SpecialInfoSaveGuard(SalesChannelTrN11ProductSpecialInfoDto item)
    {
        return string.IsNullOrWhiteSpace(item.Key) ? L["N11Product:SpecialInfoKeyRequired"].Value : null;
    }

    // N11'e gidecek varyant/görsel önizlemesi — yalnız KAYDEDİLMİŞ kayıtta (ProductId server'da çözülür).
    private async Task EnsurePreviewAsync()
    {
        if (Model.Id == Guid.Empty || Model.Id == _loadedPreviewId)
        {
            return;
        }

        _loadedPreviewId = Model.Id;
        try
        {
            _preview = await ProductAppService.GetPushPreviewAsync(Model.Id);
        }
        catch (Exception ex)
        {
            _preview = null;
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Kanalın kargo şablonlarını (dropdown) yükler — kanal değişince tazelenir.
    private async Task EnsureTemplatesAsync()
    {
        if (Model.SalesChannelId == Guid.Empty || Model.SalesChannelId == _loadedTemplatesChannelId)
        {
            return;
        }

        _loadedTemplatesChannelId = Model.SalesChannelId;
        try
        {
            _templates = await ShipmentTemplateAppService.GetListAsync(Model.SalesChannelId);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yaprak kategori seçiliyse attribute tanımlarını (on-demand) çeker — kategori değişince tazelenir.
    private async Task EnsureAttributesAsync()
    {
        if (string.IsNullOrEmpty(Model.CategoryExternalId) || Model.CategoryExternalId == _loadedAttributesCategoryId)
        {
            return;
        }

        _loadedAttributesCategoryId = Model.CategoryExternalId;
        try
        {
            // Varyant eksenleri (isVariant=true) ÜRÜN seviyesinde GÖSTERİLMEZ — onlar varyantlarla (stockItems)
            // gider; push validasyonu da ürün-seviyesinde göndermeyi reddeder (Faz 1).
            // Form sırası: ZORUNLULAR önce → içlerinde MARKA en başta → N11 önceliği artan (SOAP null → sona) → ad artan.
            _attributeDefs = (await CategoryAppService.GetLeafAttributesAsync(Model.CategoryExternalId))
                .Where(a => !a.IsVariant)
                .OrderByDescending(a => a.IsMandatory)
                .ThenByDescending(a => string.Equals(a.Name, "Marka", StringComparison.OrdinalIgnoreCase))
                .ThenBy(a => a.Priority ?? double.MaxValue)
                .ThenBy(a => a.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _attributeDefs = new List<N11CategoryAttributeDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }

        BuildAttributeRows();
    }

    // Def + Model.Attributes'taki mevcut değerlerden grid satırlarını kur (def sırası korunur) — bir Name'e ait
    // TÜM mevcut Attribute girdileri (N11 tel formatı zaten "aynı isim, ayrı eleman") değer-listeli attribute'ta
    // SelectedValues'a toplanır, serbest-metin attribute'ta ilk (tek) değer CustomValue'ya alınır.
    private void BuildAttributeRows()
    {
        _attributeRows = _attributeDefs.Select(def =>
        {
            var hasValueList = def.Values.Count > 0 && !def.IsCustomValue;
            var existingValues = Model.Attributes.Where(a => a.Name == def.Name).Select(a => a.Value).ToList();
            return new N11AttributeCellRow
            {
                AttributeId = def.AttributeId,
                Name = def.Name,
                IsMandatory = def.IsMandatory,
                HasValueList = hasValueList,
                DefinitionValues = def.Values,
                SelectedValues = hasValueList ? existingValues : new List<string>(),
                CustomValue = hasValueList ? string.Empty : (existingValues.FirstOrDefault() ?? string.Empty),
            };
        }).ToList();
    }

    // Hücre düzenlemesi kapanınca (EditCell — ayrı kaydet/düzenle tuşu yok) edit-model klonunun değerini orijinal
    // satıra + gerçek Model.Attributes'a ANINDA uygular: o Name'e ait TÜM eski girdiler silinip yeniden kurulur
    // (değer-listeli → SelectedValues'taki her seçim ayrı (Name,Value) çifti; serbest-metin → tek CustomValue).
    private void OnAttributeRowSaving(GridEditModelSavingEventArgs e)
    {
        var edited = (N11AttributeCellRow)e.EditModel;
        if (e.DataItem is N11AttributeCellRow original)
        {
            original.SelectedValues = edited.SelectedValues;
            original.CustomValue = edited.CustomValue;
        }

        Model.Attributes.RemoveAll(a => a.Name == edited.Name);
        var values = edited.HasValueList
            ? edited.SelectedValues.Where(v => !string.IsNullOrWhiteSpace(v))
            : (string.IsNullOrWhiteSpace(edited.CustomValue) ? Enumerable.Empty<string>() : new[] { edited.CustomValue });
        foreach (var value in values)
        {
            Model.Attributes.Add(new SalesChannelTrN11ProductAttributeDto { Name = edited.Name, Value = value });
        }

        MarkDirty(nameof(Model.Attributes));
    }

    // Yaprak kategori seçildi — modele dış-id + ad yaz, attribute'ları tazele, dirty.
    private async Task OnCategorySelectedAsync(N11CategorySelection selection)
    {
        Model.CategoryExternalId = selection.ExternalId;
        Model.CategoryName = selection.Name;
        MarkDirty(nameof(Model.CategoryExternalId));
        await EnsureAttributesAsync();
    }

    private void OnShipmentTemplateChanged(string? templateName)
    {
        Model.ShipmentTemplateName = templateName ?? string.Empty;
        MarkDirty(nameof(Model.ShipmentTemplateName));
    }

    // ── Nitelik Eksenleri (varyant üretimi) — sıra no + boş-alan guard'ları (Product ProductAttributeGraphDto
    // deseniyle AYNI: silinmemişlerin max sırası + 1).
    private static int NextAxisOrder(IEnumerable<SalesChannelTrN11ProductAttributeAxisDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private static int NextAxisValueOrder(IEnumerable<SalesChannelTrN11ProductAttributeAxisValueDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private string? AxisSaveGuard(SalesChannelTrN11ProductAttributeAxisDto item)
    {
        return string.IsNullOrWhiteSpace(item.Name) ? L["N11Product:AttributeAxisNameRequired"].Value : null;
    }

    private string? AxisValueSaveGuard(SalesChannelTrN11ProductAttributeAxisValueDto item)
    {
        return string.IsNullOrWhiteSpace(item.Value) ? L["N11Product:AttributeValueRequired"].Value : null;
    }

    // Eksen/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — yalnız KAYDEDİLMİŞ (Id'li) kayıtta.
    // Tüm ürünü kaydetmeye gerek yok (RegenerateVariantsAsync, Full Update ile AYNI reconcile mekanizmasını kullanır).
    private async Task RegenerateVariantsAsync()
    {
        if (Model.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var result = await ProductAppService.RegenerateVariantsAsync(Model.Id, Model.AttributeAxes);
            Model.AttributeAxes = result.AttributeAxes;
            Model.Variants = result.Variants;
            MarkDirty(nameof(Model.Variants));
            UiService.ShowSuccessToast(string.Format(L["N11Product:VariantsRegenerated"].Value, Model.Variants.Count));
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Varyant grid/edit'te kod hücresi — ERP-backed satırda ERP kodu, N11-only satırda (ERP karşılığı yok)
    // eksen-değer özeti (CombinationLabel); ikisi de boşsa "-" (henüz reconcile edilmemiş/legacy taslak).
    private static string VariantCodeOrLabel(SalesChannelTrN11ProductVariantGraphDto variant)
    {
        if (!string.IsNullOrEmpty(variant.VariantCode))
        {
            return variant.VariantCode;
        }

        return string.IsNullOrEmpty(variant.CombinationLabel) ? "-" : variant.CombinationLabel;
    }

    // ── Kanal-özel varyant override'ları (fiyat/stok/marj + reçete) ──────────────────────────────────

    // Reçete satırı eklendi/değişti/silindi → CANLI net maliyet (ERP ile ORTAK persistsiz hesap motoru) + türetilmiş
    // fiyat yeniden hesaplanır (satır maliyet alanları grid'e döner). Tam kayıt gerekmez.
    private async Task HandleVariantRecipeChangedAsync(SalesChannelTrN11ProductVariantGraphDto variant)
    {
        var result = await RecipeCostAppService.CalculateRecipeCostAsync(
            new ProductRecipeCostRequestDto { Lines = variant.RecipeLines });

        variant.NetCost = result.NetCost;
        variant.NetCostCurrency = result.NetCostCurrency;
        variant.NetCostMissingRate = result.NetCostMissingRate;
        ApplyLineCosts(variant.RecipeLines, result.Lines);
        RecomputeDerivedPrice(variant);
        MarkDirty(nameof(Model.Variants));
        StateHasChanged();
    }

    // Marj değişti → türetilmiş fiyatı ANINDA güncelle (NetCost sunucu çağrısı gerekmez; markup salt aritmetik) + dirty.
    private void OnVariantMarginChanged(SalesChannelTrN11ProductVariantGraphDto variant, decimal? margin)
    {
        variant.Margin = margin;
        RecomputeDerivedPrice(variant);
        MarkDirty(nameof(Model.Variants));
    }

    // Override fiyat para birimi (ValueExpression'sız) → değeri yaz + dirty.
    private void OnOverrideCurrencyChanged(SalesChannelTrN11ProductVariantGraphDto variant, Guid? currencyUnitId)
    {
        variant.OverridePriceCurrencyUnitId = currencyUnitId;
        MarkDirty(nameof(Model.Variants));
    }

    // Türetilmiş fiyat = NetCost × (1 + Marj/100) [MARKUP] — kur eksik/NetCost yoksa null (backend ile AYNI formül).
    private static void RecomputeDerivedPrice(SalesChannelTrN11ProductVariantGraphDto variant)
    {
        variant.DerivedPrice = variant.NetCost is { } netCost && !variant.NetCostMissingRate
            ? netCost * (1m + (variant.Margin ?? 0m) / 100m)
            : null;
    }

    // Hesaplanan satır maliyetlerini in-memory satırlara ClientKey ile uygular — in-process aynı nesne ise no-op
    // (Blazor Server; PopulateRecipeCostsAsync satırları zaten yerinde günceller), aksi halde savunmalı kopya.
    private static void ApplyLineCosts(List<ProductRecipeLineGraphDto> target, List<ProductRecipeLineGraphDto> computed)
    {
        var byKey = computed.ToDictionary(l => l.ClientKey);
        foreach (var line in target)
        {
            if (!byKey.TryGetValue(line.ClientKey, out var r) || ReferenceEquals(line, r))
            {
                continue;
            }

            line.LineCost = r.LineCost;
            line.LineCostMissingRate = r.LineCostMissingRate;
            line.Total = r.Total;
            line.PayTotal = r.PayTotal;
            line.AppliedBase = r.AppliedBase;
            line.RunningSubtotal = r.RunningSubtotal;
            line.MainUnitCode = r.MainUnitCode;
            line.PayUnitCode = r.PayUnitCode;
        }
    }

    // ValueExpression'sız editörler EditContext'e bildirmez → dirty ELLE tetiklenir (DrillList Save aktifliği).
    private void MarkDirty(string fieldName)
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, fieldName));
    }
}
