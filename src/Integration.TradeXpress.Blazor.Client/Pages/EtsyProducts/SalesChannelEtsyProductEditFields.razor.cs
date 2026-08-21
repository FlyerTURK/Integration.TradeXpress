using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.EtsyProducts;

/// <summary>Taksonomi-güdümlü listeleme nitelik grid'inin satırı — HÜCRE-İÇİ düzenleme (N11 <c>N11AttributeCellRow</c>
/// ikizi). Kategori (TaxonomyId) seçilince Etsy <c>getPropertiesByTaxonomyId</c> tanımları yüklenir; her tanım BİR satır
/// olur. Bu iterasyonda her nitelik TEK DEĞERLİDİR (çoklu-seçim SONRAKİ iş — bkz. <see cref="IsMultivalued"/>): değer
/// listeli nitelik → tek-seçim DxComboBox, serbest nitelik → DxTextBox; ikisi de değeri <see cref="CustomValue"/>'da
/// tutar. DxGrid edit-model klonu için public parametresiz ctor + set'li property'ler.</summary>
public sealed class EtsyPropertyCellRow
{
    public long PropertyId { get; set; }

    /// <summary>Kullanıcıya gösterilen ad — API <c>display_name</c> (yoksa Name'e düşer). Model.ListingAttributes'ta
    /// Name olarak bu kullanılır (eşleşme anahtarı).</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    /// <summary>Varyant ekseni olabilir mi — bu iterasyonda yalnız İŞARETLENİR (SKU-başına değer ATANMAZ; gizlenmez).</summary>
    public bool SupportsVariations { get; set; }

    /// <summary>Birden çok değer seçilebilir mi → değer editörü DxTagBox (çoklu-seçim); değerler
    /// <see cref="SelectedValues"/>'da tutulur ve Model.ListingAttributes'a HER değer AYRI satır olarak yazılır
    /// (ayraçla birleştirme YOK — ayraç çakışması + push dostu değil). Tek-değerli (false) satırlar combo/textbox +
    /// <see cref="CustomValue"/>.</summary>
    public bool IsMultivalued { get; set; }

    /// <summary>İzinli maksimum değer sayısı (çoklu için; yoksa null → sınırsız). Aşım TagBox seçiminde kırpılır.</summary>
    public int? MaxValuesAllowed { get; set; }

    /// <summary>Önerilen değer listesi dolu → tek-seçim DxComboBox; aksi halde serbest metin DxTextBox (ör. ölçü/miktar).
    /// Çoklu satırlarda editör her hâlde DxTagBox (bu bayrak yalnız tek-değer editör seçimini yönetir).</summary>
    public bool HasValueList { get; set; }

    public List<EtsyTaxonomyPropertyValueDto> DefinitionValues { get; set; } = new();

    /// <summary>Seçili/yazılı tek değer (tek-değerli satır: combo seçimi ya da serbest metin — hepsi buraya).</summary>
    public string CustomValue { get; set; } = string.Empty;

    /// <summary>Seçili değerler (çoklu-değerli satır: DxTagBox seçimi; her biri ayrı ListingAttributes satırı olur).</summary>
    public List<string> SelectedValues { get; set; } = new();
}

/// <summary>Etsy ürün listeleme edit alanları — form geneli (kanal salt-okunur + taksonomi/listeleme türü/kargo profili/
/// işleme süresi/kişiselleştirme) üstte; ekranda yer kaplayan listeler (açıklama, etiket, malzeme, listeleme attribute'ları,
/// özellikler, kombinasyonlar, özel bilgi, SKU'lar, senkron durumu) DrillTabs sekmelerine ayrılır. N11
/// <c>SalesChannelTrN11ProductEditFields</c> ikizi (Etsy alan delta'sıyla). Bu dilimde push/sync/regenerate uçları YOK
/// (Etsy AppService CRUD-only) → ilgili butonlar/önizleme yer almaz; StockItems/ProductAttributes ürün 'Kaydet'inde
/// sunucuda reconcile edilir. Listeleme drill'inin EditContent'i; kendi DxFormLayout'unu sağlar.</summary>
public partial class SalesChannelEtsyProductEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelEtsyProductDto Model { get; set; } = default!;

    /// <summary>Kanal AD çözümü beslemesi (yalnız Etsy kanalları) — kanal HER ZAMAN salt-okunur gösterilir
    /// (create'te otomatik atanır, set-once); seçici yok.</summary>
    [Parameter] public IReadOnlyList<SalesChannelListDto> Channels { get; set; } = Array.Empty<SalesChannelListDto>();

    [Inject] private ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] private IEtsyTaxonomyAppService TaxonomyAppService { get; set; } = default!;
    [Inject] private ISalesChannelEtsyProductAppService EtsyProductAppService { get; set; } = default!;

    // Reçete drill'inin katalog lookup beslemesi + persistsiz maliyet motoru (ERP ile ORTAK: aynı entity-agnostik
    // hesap uçları; kanal varyant reçetesi de ProductRecipeLineGraphDto olduğundan Product AppService yeniden kullanılır).
    [Inject] private IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] private IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] private IFutureAppService FutureAppService { get; set; } = default!;
    [Inject] private IJewelryAppService JewelryAppService { get; set; } = default!;
    [Inject] private IGoodAppService GoodAppService { get; set; } = default!;
    [Inject] private IStoneAppService StoneAppService { get; set; } = default!;
    [Inject] private IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService EffectivePriceAppService { get; set; } = default!;
    [Inject] private IProductAppService RecipeCostAppService { get; set; } = default!;

    [CascadingParameter] private EditContext? EditContext { get; set; }

    // Etsy para birimi lookup verisi (tüm sistem dövizleri; Etsy'de N11 gibi TRY/USD/EUR kısıtı yok).
    private List<CurrencyUnitListDto> _units = new();
    private bool _unitsLoaded;

    // DxTagBox serbest-metin (AllowCustomTags) modunda Data zorunlu ama boş yeter — kullanıcı liste-dışı etiket/malzeme yazar.
    private static readonly string[] _emptyStrings = Array.Empty<string>();

    // Kanal-özel varyant override drill'i (satır düzenleme aç/kapa) + reçete katalog lookup verisi (bir kez yüklenir).
    private DrillList<SalesChannelEtsyProductStockItemGraphDto>? _stockItemDrill;
    private IReadOnlyList<MetalListDto> _metals = Array.Empty<MetalListDto>();
    // Varyant-farkındalıklı MADEN lookup'ı (ProductRecipePanel.MetalVariants) — beslenmezse combo BOŞ kalır.
    private IReadOnlyList<MetalVariantLookupDto> _metalVariants = Array.Empty<MetalVariantLookupDto>();
    private IReadOnlyList<ScrapListDto> _scraps = Array.Empty<ScrapListDto>();
    private IReadOnlyList<FutureListDto> _futures = Array.Empty<FutureListDto>();
    private IReadOnlyList<JewelryListDto> _jewelries = Array.Empty<JewelryListDto>();
    private IReadOnlyList<GoodListDto> _goods = Array.Empty<GoodListDto>();
    private IReadOnlyList<StoneListDto> _stones = Array.Empty<StoneListDto>();
    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private IReadOnlyList<CurrentPriceDto> _priceUnits = Array.Empty<CurrentPriceDto>();
    private bool _catalogsLoaded;

    // Özellikler drill'i (kombinasyon ÜRETİMİ amaçlı) — üst = özellik (Model.ProductAttributes, ilk açılışta ERP'den
    // klonlanmış taslak gelir), alt = özellik değerleri. İkisi de serbest ekle/sil (klon-sonra-ayrış felsefesi).
    private DrillList<SalesChannelEtsyProductAttributeDto>? _attributeDrill;
    private DrillList<SalesChannelEtsyProductAttributeValueDto>? _attributeValueDrill;

    // Taksonomi-güdümlü listeleme nitelik grid'i (N11 kategori-attribute grid ikizi): tanımlar on-demand yüklenir,
    // her tanım bir satır → hücre-içi düzenlenir → seçilen değerler Model.ListingAttributes'a (Name/Value) yazılır.
    // _loadedPropertiesTaxonomyId: son yüklenen taksonomi anahtarı (kategori değişince tazele, aynıysa ağ çağrısı atma).
    private List<EtsyTaxonomyPropertyDto> _propertyDefs = new();
    private List<EtsyPropertyCellRow> _propertyRows = new();
    private long? _loadedPropertiesTaxonomyId;

    // Kargo profili picker beslemesi (on-demand; KALICI TABLO YOK) — mağazanın getShopShippingProfiles listesi bir kez
    // yüklenir. _shippingProfilesLoadFailed: ağ hatası ile "mağazada yok"u ayırmak için (hata → bayat DEME, çekemedik).
    private List<EtsyShippingProfileDto> _shippingProfiles = new();
    private bool _shippingProfilesLoaded;
    private bool _shippingProfilesLoadFailed;

    // Saklı ShippingProfileId dolu AMA taze listede yoksa (mağazadan silinmiş/değişmiş) → "yeniden seç" uyarısı
    // (taksonomi IsStale deseninin ikizi). Yükleme başarısızsa uyarı GÖSTERİLMEZ. Computed → kullanıcı geçerli profil
    // seçince kendiliğinden kapanır (re-render).
    private bool ShippingProfileStale =>
        _shippingProfilesLoaded
        && !_shippingProfilesLoadFailed
        && Model.ShippingProfileId is { } id
        && _shippingProfiles.All(p => p.Id != id);

    // İade politikası picker beslemesi (on-demand; KALICI TABLO YOK) — kargo profili deseninin ikizi. LoadFailed:
    // ağ hatası ile "mağazada yok"u ayırır (hata → bayat DEME, çekemedik).
    private List<EtsyReturnPolicyDto> _returnPolicies = new();
    private bool _returnPoliciesLoaded;
    private bool _returnPoliciesLoadFailed;

    // Saklı ReturnPolicyId dolu AMA taze listede yoksa (mağazadan silinmiş/değişmiş) → "yeniden seç" uyarısı
    // (ShippingProfileStale ikizi). Yükleme başarısızsa uyarı GÖSTERİLMEZ.
    private bool ReturnPolicyStale =>
        _returnPoliciesLoaded
        && !_returnPoliciesLoadFailed
        && Model.ReturnPolicyId is { } id
        && _returnPolicies.All(p => p.Id != id);

    // Dükkân bölümü picker beslemesi (on-demand; KALICI TABLO YOK) — kargo profili deseninin ikizi.
    private List<EtsyShopSectionDto> _shopSections = new();
    private bool _shopSectionsLoaded;
    private bool _shopSectionsLoadFailed;

    private bool ShopSectionStale =>
        _shopSectionsLoaded
        && !_shopSectionsLoadFailed
        && Model.ShopSectionId is { } id
        && _shopSections.All(s => s.Id != id);

    // Dükkân bölümü ekle/düzenle popup durumu — Etsy'ye YAZMA yalnız SaveShopSectionAsync (Kaydet) ile. EditId null=ekle.
    private bool _shopSectionPopupVisible;
    private long? _shopSectionEditId;
    private string _shopSectionTitle = string.Empty;
    private bool _shopSectionSaving;

    // İade politikası ekle/düzenle popup durumu — Etsy'ye YAZMA yalnız SaveReturnPolicyAsync ile. EditId null=ekle.
    private bool _returnPolicyPopupVisible;
    private long? _returnPolicyEditId;
    private bool _returnPolicyAcceptsReturns;
    private bool _returnPolicyAcceptsExchanges;
    private int? _returnPolicyDeadlineDays;
    private bool _returnPolicySaving;

    private string ChannelName =>
        Channels.FirstOrDefault(c => c.Id == Model.SalesChannelId)?.Code ?? Model.SalesChannelId.ToString();

    /// <summary>DxHtmlEditor için null-güvenli <c>Model.DescriptionOverride</c> property'si (Markup non-null string bekler; DTO alanı nullable).</summary>
    private string DescriptionMarkup
    {
        get => Model.DescriptionOverride ?? string.Empty;
        set => Model.DescriptionOverride = value;
    }

    protected override async Task OnParametersSetAsync()
    {
        await EnsureCurrencyUnitsAsync();
        await EnsureRecipeCatalogsAsync();
        await EnsurePropertiesAsync();
        await EnsureShippingProfilesAsync();
        await EnsureReturnPoliciesAsync();
        await EnsureShopSectionsAsync();
    }

    // Kanalın kargo profillerini (getShopShippingProfiles) bir kez on-demand yükler — kanal biliniyorsa (create'te
    // set-once atanır). Hata → boş liste + dostane toast (form KIRILMASIN; taksonomi EnsurePropertiesAsync deseni).
    // SalesChannelId henüz boşsa yükleme ertelenir (kanal geldiğinde sonraki OnParametersSet'te tekrar denenir).
    private async Task EnsureShippingProfilesAsync()
    {
        if (_shippingProfilesLoaded || Model.SalesChannelId == Guid.Empty)
        {
            return;
        }

        _shippingProfilesLoaded = true;
        try
        {
            _shippingProfiles = await EtsyProductAppService.GetShippingProfilesAsync(Model.SalesChannelId);
        }
        catch (Exception ex)
        {
            _shippingProfiles = new List<EtsyShippingProfileDto>();
            _shippingProfilesLoadFailed = true;   // çekilemedi → bayat-uyarısı bastırılır (silinmiş ≠ ulaşılamadı)
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyProduct:ShippingProfilesLoadFailed"].Value);
        }
    }

    // Kanalın iade politikalarını (getShopReturnPolicies) bir kez on-demand yükler — kargo profili EnsureShippingProfilesAsync
    // deseninin ikizi. Hata → boş liste + dostane toast (form KIRILMASIN). SalesChannelId henüz boşsa yükleme ertelenir.
    private async Task EnsureReturnPoliciesAsync()
    {
        if (_returnPoliciesLoaded || Model.SalesChannelId == Guid.Empty)
        {
            return;
        }

        _returnPoliciesLoaded = true;
        try
        {
            _returnPolicies = await EtsyProductAppService.GetReturnPoliciesAsync(Model.SalesChannelId);
        }
        catch (Exception ex)
        {
            _returnPolicies = new List<EtsyReturnPolicyDto>();
            _returnPoliciesLoadFailed = true;   // çekilemedi → bayat-uyarısı bastırılır (silinmiş ≠ ulaşılamadı)
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyProduct:ReturnPoliciesLoadFailed"].Value);
        }
    }

    // Kanalın dükkân bölümlerini (getShopSections) bir kez on-demand yükler — kargo profili deseninin ikizi.
    private async Task EnsureShopSectionsAsync()
    {
        if (_shopSectionsLoaded || Model.SalesChannelId == Guid.Empty)
        {
            return;
        }

        _shopSectionsLoaded = true;
        try
        {
            _shopSections = await EtsyProductAppService.GetShopSectionsAsync(Model.SalesChannelId);
        }
        catch (Exception ex)
        {
            _shopSections = new List<EtsyShopSectionDto>();
            _shopSectionsLoadFailed = true;   // çekilemedi → bayat-uyarısı bastırılır (silinmiş ≠ ulaşılamadı)
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyProduct:ShopSectionsLoadFailed"].Value);
        }
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
            _metalVariants = await MetalAppService.GetVariantLookupAsync();
            _scraps = await ScrapAppService.GetPickerListAsync();
            _futures = await FutureAppService.GetPickerListAsync();
            _jewelries = await JewelryAppService.GetPickerListAsync();
            _goods = await GoodAppService.GetPickerListAsync();
            _stones = await StoneAppService.GetPickerListAsync();
            _services = await ServiceAppService.GetPickerListAsync();
            _priceUnits = await EffectivePriceAppService.GetCurrentPricesAsync();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Etsy para birimi lookup listesini bir kez yükler (döviz cache TTL + auto-invalidate).
    private async Task EnsureCurrencyUnitsAsync()
    {
        if (_unitsLoaded)
        {
            return;
        }

        _unitsLoaded = true;
        try
        {
            _units = (await CurrencyLookup.GetAsync()).ToList();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // ── Listeleme nitelikleri (taksonomi-güdümlü grid; N11 kategori-attribute grid ikizi) ──────────────

    // Taksonomi seçiliyse property tanımlarını (on-demand) çeker — kategori değişince tazelenir. Taksonomi yoksa
    // grid boşaltılır (kategori temizlenince eski nitelikler kalmasın). Hata → tanımlar boş + dostane toast (form
    // KIRILMASIN); ağ çağrısı son-yüklenen anahtarla tekrarlanmaz.
    private async Task EnsurePropertiesAsync()
    {
        if (Model.TaxonomyId is not { } taxonomyId)
        {
            if (_propertyDefs.Count > 0 || _propertyRows.Count > 0)
            {
                _propertyDefs = new List<EtsyTaxonomyPropertyDto>();
                _propertyRows = new List<EtsyPropertyCellRow>();
            }

            _loadedPropertiesTaxonomyId = null;
            return;
        }

        if (taxonomyId == _loadedPropertiesTaxonomyId)
        {
            return;
        }

        _loadedPropertiesTaxonomyId = taxonomyId;
        try
        {
            // Form sırası: ZORUNLULAR önce → ad artan (N11'deki priority/Marka önceliği Etsy'de yok).
            _propertyDefs = (await TaxonomyAppService.GetPropertiesAsync(taxonomyId))
                .OrderByDescending(p => p.IsRequired)
                .ThenBy(p => p.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _propertyDefs = new List<EtsyTaxonomyPropertyDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }

        BuildPropertyRows();
    }

    // Def + Model.ListingAttributes'taki mevcut değer(ler)den grid satırlarını kur (def sırası korunur). Çoklu-değerli
    // property → aynı Name'e (DisplayName) ait TÜM değerler SelectedValues'a (her biri ayrı ListingAttributes satırı);
    // tek-değerli → o Name'e ait ilk (tek) değer CustomValue'ya (combo seçimi ya da serbest metin).
    private void BuildPropertyRows()
    {
        _propertyRows = _propertyDefs.Select(def =>
        {
            var row = new EtsyPropertyCellRow
            {
                PropertyId = def.PropertyId,
                Name = def.DisplayName,
                IsRequired = def.IsRequired,
                SupportsVariations = def.SupportsVariations,
                IsMultivalued = def.IsMultivalued,
                MaxValuesAllowed = def.MaxValuesAllowed,
                HasValueList = def.PossibleValues.Count > 0,
                DefinitionValues = def.PossibleValues,
            };

            if (def.IsMultivalued)
            {
                row.SelectedValues = Model.ListingAttributes
                    .Where(a => a.Name == def.DisplayName)
                    .Select(a => a.Value)
                    .ToList();
            }
            else
            {
                row.CustomValue = Model.ListingAttributes.FirstOrDefault(a => a.Name == def.DisplayName)?.Value ?? string.Empty;
            }

            return row;
        }).ToList();
    }

    // DxTagBox seçimi değişti (çoklu satır) — MaxValuesAllowed doluysa fazlasını KIRP + kısa uyarı (DxTagBox'ta doğrudan
    // max yok); yoksa serbest. Edit-model klonuna yeni liste atanır (orijinal Save'de geri kopyalanır).
    private void OnMultiValuesChanged(EtsyPropertyCellRow row, IEnumerable<string> values)
    {
        var selected = values.ToList();
        if (row.MaxValuesAllowed is { } max && selected.Count > max)
        {
            selected = selected.Take(max).ToList();
            UiService.ShowWarningToast(string.Format(L["EtsyProduct:MaxValuesExceeded"].Value, max));
        }

        row.SelectedValues = selected;
    }

    // Hücre düzenlemesi kapanınca (EditCell — ayrı kaydet/düzenle tuşu yok) edit-model klonunun tek değerini orijinal
    // satıra + gerçek Model.ListingAttributes'a ANINDA uygular: o Name'e ait eski girdi silinip (varsa) tek (Name,Value)
    // çifti yeniden kurulur; boş değer o niteliği kaldırır.
    private void OnPropertyRowSaving(GridEditModelSavingEventArgs e)
    {
        var edited = (EtsyPropertyCellRow)e.EditModel;
        if (e.DataItem is EtsyPropertyCellRow original)
        {
            original.CustomValue = edited.CustomValue;
            original.SelectedValues = edited.SelectedValues;
        }

        Model.ListingAttributes.RemoveAll(a => a.Name == edited.Name);
        if (edited.IsMultivalued)
        {
            // Çoklu: her seçili değer AYRI (Name,Value) satırı — ayraçla birleştirme YOK (push-dostu; aynı Name tekrarlanır).
            foreach (var value in edited.SelectedValues.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                Model.ListingAttributes.Add(new SalesChannelEtsyProductListingAttributeDto { Name = edited.Name, Value = value });
            }
        }
        else if (!string.IsNullOrWhiteSpace(edited.CustomValue))
        {
            Model.ListingAttributes.Add(new SalesChannelEtsyProductListingAttributeDto { Name = edited.Name, Value = edited.CustomValue });
        }

        MarkDirty(nameof(Model.ListingAttributes));
    }

    // Yaprak taksonomi seçildi — dış id'yi long'a çevir + dirty + nitelikleri tazele. Taksonomi taslakta OPSİYONEL
    // (2026-07-11 Trendyol deseni): temizleme boş seçim gelir → id null'lanır (grid de boşalır). TaxonomyName
    // persistlenmez (picker seçili adı kendi state'inde tutar; reload'da id yeterli).
    private async Task OnTaxonomySelectedAsync(EtsyTaxonomySelection selection)
    {
        Model.TaxonomyId = string.IsNullOrEmpty(selection.ExternalId) ? null : long.Parse(selection.ExternalId);
        MarkDirty(nameof(Model.TaxonomyId));
        await EnsurePropertiesAsync();
    }

    // ── Etiket / malzeme (serbest string listesi; DxTagBox AllowCustomTags) ────────────────────────────

    private void OnTagsChanged(IEnumerable<string> tags)
    {
        Model.Tags = tags.ToList();
        MarkDirty(nameof(Model.Tags));
    }

    private void OnMaterialsChanged(IEnumerable<string> materials)
    {
        Model.Materials = materials.ToList();
        MarkDirty(nameof(Model.Materials));
    }

    // ── Özel bilgi (serbest key/value) ─────────────────────────────────────────────────────────────────
    private string? SpecialInfoSaveGuard(SalesChannelEtsyProductSpecialInfoDto item)
    {
        return string.IsNullOrWhiteSpace(item.Key) ? L["EtsyProduct:SpecialInfoKeyRequired"].Value : null;
    }

    // ── Özellikler (kombinasyon üretimi) — sıra no + boş-alan guard'ları (ERP ProductAttributeGraphDto deseniyle AYNI). ──
    private static int NextAttributeOrder(IEnumerable<SalesChannelEtsyProductAttributeDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private static int NextAttributeValueOrder(IEnumerable<SalesChannelEtsyProductAttributeValueDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private string? AttributeSaveGuard(SalesChannelEtsyProductAttributeDto item)
    {
        return string.IsNullOrWhiteSpace(item.Name) ? L["EtsyProduct:AttributeNameRequired"].Value : null;
    }

    private string? AttributeValueSaveGuard(SalesChannelEtsyProductAttributeValueDto item)
    {
        return string.IsNullOrWhiteSpace(item.Value) ? L["EtsyProduct:AttributeValueRequired"].Value : null;
    }

    // Varyant grid/edit'te kod hücresi — ERP-backed satırda ERP kodu, Etsy-only satırda (ERP karşılığı yok)
    // özellik-değer özeti (CombinationLabel); ikisi de boşsa "-" (henüz reconcile edilmemiş/legacy taslak).
    private static string StockItemCodeOrLabel(SalesChannelEtsyProductStockItemGraphDto stockItem)
    {
        if (!string.IsNullOrEmpty(stockItem.VariantCode))
        {
            return stockItem.VariantCode;
        }

        return string.IsNullOrEmpty(stockItem.CombinationLabel) ? "-" : stockItem.CombinationLabel;
    }

    // ── Kanal-özel varyant override'ları (fiyat/stok/marj + reçete) ──────────────────────────────────

    // Reçete satırı eklendi/değişti/silindi → CANLI net maliyet (ERP ile ORTAK persistsiz hesap motoru) + türetilmiş
    // fiyat yeniden hesaplanır (satır maliyet alanları grid'e döner). Tam kayıt gerekmez.
    private async Task HandleStockItemRecipeChangedAsync(SalesChannelEtsyProductStockItemGraphDto stockItem)
    {
        var result = await RecipeCostAppService.CalculateRecipeCostAsync(
            new ProductRecipeCostRequestDto { Lines = stockItem.RecipeLines });

        stockItem.NetCost = result.NetCost;
        stockItem.NetCostCurrency = result.NetCostCurrency;
        stockItem.NetCostMissingRate = result.NetCostMissingRate;
        ApplyLineCosts(stockItem.RecipeLines, result.Lines);
        RecomputeDerivedPrice(stockItem);
        MarkDirty(nameof(Model.StockItems));
        StateHasChanged();
    }

    // Marj değişti → türetilmiş fiyatı ANINDA güncelle (NetCost sunucu çağrısı gerekmez; markup salt aritmetik) + dirty.
    private void OnStockItemMarginChanged(SalesChannelEtsyProductStockItemGraphDto stockItem, decimal? margin)
    {
        stockItem.Margin = margin;
        RecomputeDerivedPrice(stockItem);
        MarkDirty(nameof(Model.StockItems));
    }

    // Override fiyat para birimi (ValueExpression'sız) → değeri yaz + dirty.
    private void OnOverrideCurrencyChanged(SalesChannelEtsyProductStockItemGraphDto stockItem, Guid? currencyUnitId)
    {
        stockItem.OverridePriceCurrencyUnitId = currencyUnitId;
        MarkDirty(nameof(Model.StockItems));
    }

    // Türetilmiş fiyat — backend ile AYNI merkezi formül (DerivedPriceCalculator); kur eksik/NetCost yoksa null.
    private static void RecomputeDerivedPrice(SalesChannelEtsyProductStockItemGraphDto stockItem)
    {
        stockItem.DerivedPrice = stockItem.NetCost is { } netCost && !stockItem.NetCostMissingRate
            ? DerivedPriceCalculator.Calculate(netCost, stockItem.Margin)
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

    // ── Dükkân bölümü ekle/düzenle (Etsy'ye YAZMA — createShopSection/updateShopSection) ────────────────

    // Ekle butonu → boş başlıkla popup aç (yeni bölüm).
    private void OpenAddShopSection()
    {
        _shopSectionEditId = null;
        _shopSectionTitle = string.Empty;
        _shopSectionPopupVisible = true;
    }

    // Düzenle butonu → seçili bölümün başlığını ön-doldurup popup aç. Seçili yoksa no-op (buton zaten gizli).
    private void OpenEditShopSection()
    {
        if (Model.ShopSectionId is not { } id)
        {
            return;
        }

        var current = _shopSections.FirstOrDefault(s => s.Id == id);
        _shopSectionEditId = id;
        _shopSectionTitle = current?.Title ?? string.Empty;
        _shopSectionPopupVisible = true;
    }

    // Popup Kaydet → Etsy'ye create/update yaz → dönen kaydı listeye uygula + seç. Hata → dostane toast (form kırılmaz).
    private async Task SaveShopSectionAsync()
    {
        var title = _shopSectionTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            UiService.ShowWarningToast(L["EtsyProduct:ShopSectionTitleRequired"].Value);
            return;
        }

        _shopSectionSaving = true;
        try
        {
            var input = new EtsyShopSectionInputDto { Title = title };
            var saved = _shopSectionEditId is { } editId
                ? await EtsyProductAppService.UpdateShopSectionAsync(Model.SalesChannelId, editId, input)
                : await EtsyProductAppService.CreateShopSectionAsync(Model.SalesChannelId, input);

            ApplySavedShopSection(saved);
            _shopSectionPopupVisible = false;
            UiService.ShowSuccessToast(L["EtsyProduct:ShopSectionSaved"].Value);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyProduct:ShopSectionSaveFailed"].Value);
        }
        finally
        {
            _shopSectionSaving = false;
        }
    }

    // Etsy'nin döndürdüğü (yetkili) kaydı picker listesine uygular (yeni → ekle, mevcut → güncelle) + combo'da seçer.
    // Yeni liste referansı atanır ki DxComboBox Data değişimini görsün. Reload YERİNE write-yanıtı = SSOT (ekstra çağrı yok).
    private void ApplySavedShopSection(EtsyShopSectionDto saved)
    {
        var existing = _shopSections.FirstOrDefault(s => s.Id == saved.Id);
        if (existing is null)
        {
            _shopSections.Add(saved);
        }
        else
        {
            existing.Title = saved.Title;
        }

        _shopSections = _shopSections.ToList();
        _shopSectionsLoaded = true;
        _shopSectionsLoadFailed = false;
        Model.ShopSectionId = saved.Id;
        MarkDirty(nameof(Model.ShopSectionId));
    }

    // ── İade politikası ekle/düzenle (Etsy'ye YAZMA — createShopReturnPolicy/updateShopReturnPolicy) ─────

    private void OpenAddReturnPolicy()
    {
        _returnPolicyEditId = null;
        _returnPolicyAcceptsReturns = false;
        _returnPolicyAcceptsExchanges = false;
        _returnPolicyDeadlineDays = null;
        _returnPolicyPopupVisible = true;
    }

    private void OpenEditReturnPolicy()
    {
        if (Model.ReturnPolicyId is not { } id)
        {
            return;
        }

        var current = _returnPolicies.FirstOrDefault(p => p.Id == id);
        _returnPolicyEditId = id;
        _returnPolicyAcceptsReturns = current?.AcceptsReturns ?? false;
        _returnPolicyAcceptsExchanges = current?.AcceptsExchanges ?? false;
        _returnPolicyDeadlineDays = current?.ReturnDeadlineDays;
        _returnPolicyPopupVisible = true;
    }

    // Popup Kaydet → Etsy'ye create/update yaz. Kabul (iade/değişim) varsa iade süresi ZORUNLU (Etsy reddeder) → ön-guard.
    private async Task SaveReturnPolicyAsync()
    {
        if ((_returnPolicyAcceptsReturns || _returnPolicyAcceptsExchanges) && _returnPolicyDeadlineDays is null or < 1)
        {
            UiService.ShowWarningToast(L["EtsyProduct:ReturnDeadlineRequired"].Value);
            return;
        }

        _returnPolicySaving = true;
        try
        {
            var input = new EtsyReturnPolicyInputDto
            {
                AcceptsReturns = _returnPolicyAcceptsReturns,
                AcceptsExchanges = _returnPolicyAcceptsExchanges,
                ReturnDeadlineDays = _returnPolicyDeadlineDays,
            };
            var saved = _returnPolicyEditId is { } editId
                ? await EtsyProductAppService.UpdateReturnPolicyAsync(Model.SalesChannelId, editId, input)
                : await EtsyProductAppService.CreateReturnPolicyAsync(Model.SalesChannelId, input);

            ApplySavedReturnPolicy(saved);
            _returnPolicyPopupVisible = false;
            UiService.ShowSuccessToast(L["EtsyProduct:ReturnPolicySaved"].Value);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyProduct:ReturnPolicySaveFailed"].Value);
        }
        finally
        {
            _returnPolicySaving = false;
        }
    }

    private void ApplySavedReturnPolicy(EtsyReturnPolicyDto saved)
    {
        var existing = _returnPolicies.FirstOrDefault(p => p.Id == saved.Id);
        if (existing is null)
        {
            _returnPolicies.Add(saved);
        }
        else
        {
            existing.Label = saved.Label;
            existing.AcceptsReturns = saved.AcceptsReturns;
            existing.AcceptsExchanges = saved.AcceptsExchanges;
            existing.ReturnDeadlineDays = saved.ReturnDeadlineDays;
        }

        _returnPolicies = _returnPolicies.ToList();
        _returnPoliciesLoaded = true;
        _returnPoliciesLoadFailed = false;
        Model.ReturnPolicyId = saved.Id;
        MarkDirty(nameof(Model.ReturnPolicyId));
    }

    // ValueExpression'sız editörler EditContext'e bildirmez → dirty ELLE tetiklenir (DrillList Save aktifliği).
    private void MarkDirty(string fieldName)
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, fieldName));
    }
}
