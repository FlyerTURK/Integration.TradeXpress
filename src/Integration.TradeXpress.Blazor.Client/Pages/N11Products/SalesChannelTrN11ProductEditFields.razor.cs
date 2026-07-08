using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Attribute grid'inin satırı — N11 kategori attribute'u + o anki değeri. Değer editörü satır tipine göre
/// değişir (<see cref="HasValueList"/> ? değer combo'su : serbest metin). DxGrid EditRow edit-model klonu için
/// public parametresiz ctor + set'li property'ler.</summary>
public class N11AttributeRow
{
    public string AttributeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    /// <summary>Değer listesi var + serbest-değil → combo; aksi halde serbest metin.</summary>
    public bool HasValueList { get; set; }

    public List<N11CategoryAttributeValueDto> Values { get; set; } = new();
    public string Value { get; set; } = string.Empty;
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

    [CascadingParameter] private EditContext? EditContext { get; set; }

    private List<N11ShipmentTemplateDto> _templates = new();
    private List<N11CategoryAttributeDto> _attributeDefs = new();

    // N11 para birimi lookup verisi (döviz cache; inline ekle/düzelt sonrası ReloadCurrencyUnitsAsync ile tazelenir).
    private List<CurrencyUnitListDto> _units = new();
    private bool _unitsLoaded;

    // Attribute grid satırları (def + o anki değer) — inline edit-row; değer editörü satır tipine göre değişir.
    private List<N11AttributeRow> _attributeRows = new();

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
        await EnsureTemplatesAsync();
        await EnsureAttributesAsync();
        await EnsurePreviewAsync();
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

    // Def + Model.Attributes'taki mevcut değerden grid satırlarını kur (def sırası korunur).
    private void BuildAttributeRows()
    {
        _attributeRows = _attributeDefs.Select(def => new N11AttributeRow
        {
            AttributeId = def.AttributeId,
            Name = def.Name,
            IsMandatory = def.IsMandatory,
            HasValueList = def.Values.Count > 0 && !def.IsCustomValue,
            Values = def.Values,
            Value = GetAttribute(def.Name),
        }).ToList();
    }

    // EditCell: hücre editöründen çıkınca otomatik tetiklenir (Edit/Save butonu YOK) — edit-model klonunun değerini
    // orijinal satıra + Model.Attributes'a ANINDA uygula (SetAttribute → dirty).
    private void OnAttributeRowSaving(GridEditModelSavingEventArgs e)
    {
        var edited = (N11AttributeRow)e.EditModel;
        if (e.DataItem is N11AttributeRow original)
        {
            original.Value = edited.Value;
        }

        SetAttribute(edited.Name, edited.Value);
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

    // ── Kategori attribute değerleri (name/value) — Model.Attributes ile senkron ──
    private string GetAttribute(string name)
    {
        return Model.Attributes.FirstOrDefault(a => a.Name == name)?.Value ?? string.Empty;
    }

    private void SetAttribute(string name, string? value)
    {
        var existing = Model.Attributes.FirstOrDefault(a => a.Name == name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing != null)
            {
                Model.Attributes.Remove(existing);
            }
        }
        else if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            Model.Attributes.Add(new SalesChannelTrN11ProductAttributeDto { Name = name, Value = value });
        }

        MarkDirty(nameof(Model.Attributes));
    }

    // ValueExpression'sız editörler EditContext'e bildirmez → dirty ELLE tetiklenir (DrillList Save aktifliği).
    private void MarkDirty(string fieldName)
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, fieldName));
    }
}
