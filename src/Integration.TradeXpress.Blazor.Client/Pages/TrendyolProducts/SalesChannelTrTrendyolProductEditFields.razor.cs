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
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolBrands;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>Attribute grid'inin satırı — Trendyol kategori attribute'u (id-bazlı) + o anki değeri. Değer editörü satır
/// tipine göre değişir (<see cref="HasValueList"/> ? value combo'su [ValueId] : serbest metin [CustomValue]). DxGrid
/// EditCell edit-model klonu için public parametresiz ctor + set'li property'ler.</summary>
public class TrendyolAttributeRow
{
    public int AttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    /// <summary>Serbest (custom) metin izinli — value listesi yerine <see cref="CustomValue"/> yazılır.</summary>
    public bool AllowCustom { get; set; }

    /// <summary>Değer listesi var + serbest-değil → combo (ValueId); aksi halde serbest metin (CustomValue).</summary>
    public bool HasValueList { get; set; }

    public List<TrendyolAttributeValueDto> Values { get; set; } = new();

    /// <summary>Combo satırında seçili value id (id-bazlı; <c>AttributeValueId</c>'ye yazılır).</summary>
    public int? SelectedValueId { get; set; }

    /// <summary>Serbest metin satırında girilen değer (<c>CustomValue</c>'ya yazılır).</summary>
    public string CustomValue { get; set; } = string.Empty;
}

/// <summary>Trendyol ürün listeleme edit alanları — kanal + kategori (server-arama) + marka (type-ahead) + kategori
/// attribute'ları (id-bazlı, on-demand) + KDV/boyutsal ağırlık/teslimat + kanal-özel varyant override (fiyat/stok/marj +
/// reçete). Listeleme drill'inin EditContent'i; kendi DxFormLayout'unu sağlar. ValueExpression'sız editörlerde dirty
/// EditContext'e elle bildirilir. N11 EditFields paritesi (id-bazlı attribute/marka farkı).</summary>
public partial class SalesChannelTrTrendyolProductEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelTrTrendyolProductDto Model { get; set; } = default!;

    /// <summary>Kanal AD çözümü beslemesi (yalnız Trendyol kanalları) — kanal HER ZAMAN salt-okunur gösterilir
    /// (create'te otomatik atanır, set-once); seçici yok.</summary>
    [Parameter] public IReadOnlyList<SalesChannelListDto> Channels { get; set; } = Array.Empty<SalesChannelListDto>();

    [Inject] private ITrendyolCategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private ITrendyolBrandAppService BrandAppService { get; set; } = default!;
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

    private List<TrendyolLeafAttributeDto> _attributeDefs = new();

    // Marka type-ahead son-arama sonuçları (server'dan; ön-yükleme YOK) — string BrandId'e projeksiyon.
    private List<BrandOption> _brandResults = new();

    // Trendyol para birimi lookup verisi (döviz cache) — yalnız varyant override fiyat para birimi için.
    private List<CurrencyUnitListDto> _units = new();
    private bool _unitsLoaded;

    // Kanal-özel varyant override drill'i (satır düzenleme aç/kapa) + reçete katalog lookup verisi (bir kez yüklenir).
    private DrillList<SalesChannelTrTrendyolProductVariantGraphDto>? _variantDrill;
    private IReadOnlyList<MetalListDto> _metals = Array.Empty<MetalListDto>();
    private IReadOnlyList<ScrapListDto> _scraps = Array.Empty<ScrapListDto>();
    private IReadOnlyList<FutureListDto> _futures = Array.Empty<FutureListDto>();
    private IReadOnlyList<JewelryListDto> _jewelries = Array.Empty<JewelryListDto>();
    private IReadOnlyList<StoneListDto> _stones = Array.Empty<StoneListDto>();
    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private IReadOnlyList<CurrentPriceDto> _priceUnits = Array.Empty<CurrentPriceDto>();
    private bool _catalogsLoaded;

    // Attribute grid satırları (def + o anki değer) — inline edit-cell; değer editörü satır tipine göre değişir.
    private List<TrendyolAttributeRow> _attributeRows = new();

    // OnParametersSetAsync her render'da çalışır → tekrarlı ağ çağrısını son-yüklenen anahtarla önle.
    private string? _loadedAttributesCategoryId;

    private string ChannelName =>
        Channels.FirstOrDefault(c => c.Id == Model.SalesChannelId)?.Code ?? Model.SalesChannelId.ToString();

    protected override async Task OnParametersSetAsync()
    {
        await EnsureCurrencyUnitsAsync();
        await EnsureRecipeCatalogsAsync();
        await EnsureAttributesAsync();
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

    // Trendyol para birimi lookup listesini bir kez yükler (döviz cache TTL + auto-invalidate).
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

    // Yaprak kategori seçiliyse attribute tanımlarını (id-bazlı, on-demand) çeker — kategori değişince tazelenir.
    private async Task EnsureAttributesAsync()
    {
        if (string.IsNullOrEmpty(Model.CategoryId) || Model.CategoryId == _loadedAttributesCategoryId)
        {
            return;
        }

        _loadedAttributesCategoryId = Model.CategoryId;
        try
        {
            // Varyant eksenleri (Varianter=true) ÜRÜN seviyesinde GÖSTERİLMEZ — onlar SKU/varyant başına gider.
            // Form sırası: ZORUNLULAR önce → ad artan.
            _attributeDefs = (await CategoryAppService.GetLeafAttributesAsync(Model.CategoryId))
                .Where(a => !a.Varianter)
                .OrderByDescending(a => a.Required)
                .ThenBy(a => a.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _attributeDefs = new List<TrendyolLeafAttributeDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }

        BuildAttributeRows();
    }

    // Def + Model.Attributes'taki mevcut değerden grid satırlarını kur (def sırası korunur; id-bazlı ön-doldurma).
    private void BuildAttributeRows()
    {
        _attributeRows = _attributeDefs.Select(def =>
        {
            var hasList = def.Values.Count > 0 && !def.AllowCustom;
            var existing = Model.Attributes.FirstOrDefault(a => a.AttributeId == def.AttributeId);
            return new TrendyolAttributeRow
            {
                AttributeId = def.AttributeId,
                Name = def.Name,
                IsMandatory = def.Required,
                AllowCustom = def.AllowCustom,
                HasValueList = hasList,
                Values = def.Values,
                SelectedValueId = hasList ? existing?.AttributeValueId : null,
                CustomValue = hasList ? string.Empty : existing?.CustomValue ?? string.Empty,
            };
        }).ToList();
    }

    // Grid Değer kolonu görüntü metni — combo: seçili value'nun adı; serbest: girilen metin.
    private string DisplayValue(TrendyolAttributeRow row)
    {
        if (row.HasValueList)
        {
            return row.Values.FirstOrDefault(v => v.ValueId == row.SelectedValueId)?.Value ?? string.Empty;
        }

        return row.CustomValue;
    }

    // EditCell: hücre editöründen çıkınca otomatik tetiklenir (Edit/Save butonu YOK) — edit-model klonunun değerini
    // orijinal satıra + Model.Attributes'a ANINDA uygula (SetAttribute → dirty).
    private void OnAttributeRowSaving(GridEditModelSavingEventArgs e)
    {
        var edited = (TrendyolAttributeRow)e.EditModel;
        if (e.DataItem is TrendyolAttributeRow original)
        {
            original.SelectedValueId = edited.SelectedValueId;
            original.CustomValue = edited.CustomValue;
        }

        SetAttribute(edited);
    }

    // Row değerini Model.Attributes ile senkronla (id-bazlı): combo → AttributeValueId, serbest → CustomValue.
    // Boş değer (id yok / metin boş) → satır kaldırılır.
    private void SetAttribute(TrendyolAttributeRow row)
    {
        var existing = Model.Attributes.FirstOrDefault(a => a.AttributeId == row.AttributeId);
        if (row.HasValueList)
        {
            if (row.SelectedValueId is not { } valueId)
            {
                if (existing != null)
                {
                    Model.Attributes.Remove(existing);
                }
            }
            else if (existing != null)
            {
                existing.AttributeValueId = valueId;
                existing.CustomValue = null;
            }
            else
            {
                Model.Attributes.Add(new SalesChannelTrTrendyolProductAttributeDto
                {
                    AttributeId = row.AttributeId,
                    AttributeValueId = valueId,
                });
            }
        }
        else if (string.IsNullOrWhiteSpace(row.CustomValue))
        {
            if (existing != null)
            {
                Model.Attributes.Remove(existing);
            }
        }
        else if (existing != null)
        {
            existing.CustomValue = row.CustomValue;
            existing.AttributeValueId = null;
        }
        else
        {
            Model.Attributes.Add(new SalesChannelTrTrendyolProductAttributeDto
            {
                AttributeId = row.AttributeId,
                CustomValue = row.CustomValue,
            });
        }

        MarkDirty(nameof(Model.Attributes));
    }

    // Yaprak kategori seçildi — modele dış-id + ad yaz, attribute'ları tazele, dirty.
    private async Task OnCategorySelectedAsync(TrendyolCategorySelection selection)
    {
        Model.CategoryId = selection.ExternalId;
        Model.CategoryName = selection.Name;
        MarkDirty(nameof(Model.CategoryId));
        await EnsureAttributesAsync();
    }

    // ── Marka type-ahead (ada göre server araması → BrandId) ─────────────────────────────────────────
    private async Task OnBrandSearchAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _brandResults = new List<BrandOption>();
            return;
        }

        try
        {
            var brands = await BrandAppService.SearchAsync(term);
            _brandResults = brands
                .Select(b => new BrandOption { BrandId = b.BrandId.ToString(), Name = b.Name })
                .ToList();
        }
        catch (Exception ex)
        {
            _brandResults = new List<BrandOption>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private void OnBrandChangedAsync(string? brandId)
    {
        Model.BrandId = brandId ?? string.Empty;
        Model.BrandName = _brandResults.FirstOrDefault(b => b.BrandId == brandId)?.Name;
        MarkDirty(nameof(Model.BrandId));
    }

    // Kanal-özel açıklama (HTML) değişti → modele yaz + dirty (DxHtmlEditor blur'da tetikler).
    private void OnDescriptionChanged(string markup)
    {
        Model.Description = markup;
        MarkDirty(nameof(Model.Description));
    }

    // ── Kanal-özel varyant override'ları (fiyat/stok/marj + reçete) ──────────────────────────────────

    // Reçete satırı eklendi/değişti/silindi → CANLI net maliyet (ERP ile ORTAK persistsiz hesap motoru) + türetilmiş
    // fiyat yeniden hesaplanır (satır maliyet alanları grid'e döner). Tam kayıt gerekmez.
    private async Task HandleVariantRecipeChangedAsync(SalesChannelTrTrendyolProductVariantGraphDto variant)
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
    private void OnVariantMarginChanged(SalesChannelTrTrendyolProductVariantGraphDto variant, decimal? margin)
    {
        variant.Margin = margin;
        RecomputeDerivedPrice(variant);
        MarkDirty(nameof(Model.Variants));
    }

    // Override fiyat para birimi (ValueExpression'sız) → değeri yaz + dirty.
    private void OnOverrideCurrencyChanged(SalesChannelTrTrendyolProductVariantGraphDto variant, Guid? currencyUnitId)
    {
        variant.OverridePriceCurrencyUnitId = currencyUnitId;
        MarkDirty(nameof(Model.Variants));
    }

    // Türetilmiş fiyat = NetCost × (1 + Marj/100) [MARKUP] — kur eksik/NetCost yoksa null (backend ile AYNI formül).
    private static void RecomputeDerivedPrice(SalesChannelTrTrendyolProductVariantGraphDto variant)
    {
        variant.DerivedPrice = variant.NetCost is { } netCost && !variant.NetCostMissingRate
            ? netCost * (1m + (variant.Margin ?? 0m) / 100m)
            : null;
    }

    // Hesaplanan satır maliyetlerini in-memory satırlara ClientKey ile uygular — in-process aynı nesne ise no-op
    // (Blazor Server; hesap satırları zaten yerinde günceller), aksi halde savunmalı kopya.
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

    /// <summary>Marka type-ahead sonucunu string BrandId'e projekte eden görünüm satırı — kanal-üründe BrandId string
    /// (Trendyol BrandId long); LookupEdit TValue string ile eşleşmesi için tek-yönlü projeksiyon.</summary>
    public class BrandOption
    {
        public string BrandId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
