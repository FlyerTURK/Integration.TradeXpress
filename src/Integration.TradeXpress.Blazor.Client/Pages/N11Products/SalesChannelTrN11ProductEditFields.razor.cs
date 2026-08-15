using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Kategori attribute grid'inin satırı — HÜCRE-İÇİ düzenleme (Trendyol EditCell deseniyle AYNI). Her N11
/// kategori attribute'ı TEK DEĞERLİDİR (N11'de çoklu-seçim bayrağı yok — 2026-07-11 kullanıcı kararı: "MultiSelection
/// yoksa combo olmalı"): değer listeli attribute → tek-seçim DxComboBox, serbest-metin attribute → DxTextBox; ikisi de
/// değeri <see cref="CustomValue"/>'da tutar. DxGrid edit-model klonu için public parametresiz ctor + set'li
/// property'ler.</summary>
public class N11AttributeCellRow
{
    public string AttributeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    /// <summary>Değer listesi dolu → tek-seçim DxComboBox; aksi halde serbest metin DxTextBox. N11'de
    /// isCustomValue=true olsa da (ör. Marka) valueList dolu gelebilir → combo GÖSTERİLİR, custom değere
    /// <see cref="AllowCustomValues"/> ile açılır (liste-dışı marka da yazılabilir).</summary>
    public bool HasValueList { get; set; }

    /// <summary>N11 isCustomValue=true → combo listeye kapalı DEĞİL, kullanıcı liste-dışı değer de girebilir
    /// (DxComboBox.AllowUserInput).</summary>
    public bool AllowCustomValues { get; set; }

    public List<N11CategoryAttributeValueDto> DefinitionValues { get; set; } = new();

    /// <summary>Seçili/yazılı tek değer (combo seçimi, custom giriş ya da serbest metin — hepsi buraya).</summary>
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

    /// <summary>Çekirdek ürünün CANLI varyant grafı (ProductLayout.Model.Variants — kaydedilmemiş değişiklikler
    /// dahil). Varyantlar sekmesi, kayıtlı stok kalemlerinde HENÜZ karşılığı olmayan varyantları buradan
    /// salt-okunur önizler: kullanıcı çekirdekte ne değiştirdiyse kanal formunda ANINDA görür, ürünün
    /// kaydedilmesini beklemez (2026-07-28 Hakan).</summary>
    [Parameter] public List<ProductVariantGraphDto> CoreVariants { get; set; } = new();

    /// <summary>Kayıtlı stok kalemi karşılığı OLMAYAN canlı çekirdek varyantları — salt-okunur önizleme satırları.
    ///
    /// <para><b>Neden gerçek satır değil:</b> stok kalemi kimlikleri (Id/ProductVariantId çıpası) SUNUCU üretimi;
    /// kaydedilmemiş varyanta istemcide satır uydurmak, kayıtta yanlış çıpaya ya da çakışan anahtara dönerdi.
    /// Önizleme görüntüler, düzenleme (fiyat/stok ezme) kayıttan sonra açılır.</para></summary>
    private List<ProductVariantGraphDto> PreviewCoreVariants
    {
        get
        {
            return CoreVariants
                .Where(v => !v.IsDeleted && v.IsActive)
                .Where(v => !Model.StockItems.Any(si =>
                    (si.ProductVariantId is { } pid && pid != Guid.Empty && pid == v.Id)
                    || string.Equals(si.VariantCode, v.Code, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    [Inject] private IN11ShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] private IN11CategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private ISalesChannelTrN11ProductAppService ProductAppService { get; set; } = default!;
    [Inject] private ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // "Üründen emtia yarat" akışının popup kapısı (lookup → popup → tazele → odaklan deseni).
    [Inject] private IViewOpener ViewOpener { get; set; } = default!;

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

    private List<N11ShipmentTemplateDto> _templates = new();
    private List<N11CategoryAttributeDto> _attributeDefs = new();

    // N11 para birimi lookup verisi (döviz cache → TRY/USD/EUR'e filtreli; inline ekle/düzelt YOK).
    private List<CurrencyUnitListDto> _units = new();
    private bool _unitsLoaded;

    // KDV combo verisi — küme Domain.Shared'daki SSOT'tan gelir (entity guard'ı ve REST istemci doğrulaması da
    // aynı listeyi okur), böylece formda seçilebilen bir oran push'ta reddedilemez.
    private static readonly List<VatRateOption> VatRateOptions = VatRateOption.From(N11ProductConsts.AllowedVatRates);

    // Kanal-özel varyant override drill'i (satır düzenleme aç/kapa) + reçete katalog lookup verisi (bir kez yüklenir).
    private DrillList<SalesChannelTrN11ProductStockItemGraphDto>? _stockItemDrill;
    private IReadOnlyList<MetalListDto> _metals = Array.Empty<MetalListDto>();
    // Varyant-farkındalıklı MADEN lookup'ı (ProductRecipePanel.MetalVariants) — beslenmezse combo BOŞ kalır
    // (varyant geçişi sancısı: çekirdek host güncellendi, kanal formları unutulmuştu).
    private IReadOnlyList<MetalVariantLookupDto> _metalVariants = Array.Empty<MetalVariantLookupDto>();
    private IReadOnlyList<ScrapListDto> _scraps = Array.Empty<ScrapListDto>();
    private IReadOnlyList<FutureListDto> _futures = Array.Empty<FutureListDto>();
    private IReadOnlyList<JewelryListDto> _jewelries = Array.Empty<JewelryListDto>();
    private IReadOnlyList<GoodListDto> _goods = Array.Empty<GoodListDto>();
    private IReadOnlyList<StoneListDto> _stones = Array.Empty<StoneListDto>();
    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private IReadOnlyList<CurrentPriceDto> _priceUnits = Array.Empty<CurrentPriceDto>();
    private bool _catalogsLoaded;

    // Çekirdek varyant listesinin sunucu geri-dönüşü — CoreVariants yalnız ürün-formu yüzeyinde besleniyor;
    // standalone/tahta yüzeylerinde "Üründen" anahtarının görünürlük kapısı (fail-closed) için buradan çekilir.
    private IReadOnlyList<ProductVariantGraphDto> _coreVariantsFallback = Array.Empty<ProductVariantGraphDto>();
    private Guid _loadedCoreVariantsProductId;

    /// <summary>Panelin AllVariants beslemesi — canlı graf (ürün formu yüzeyi) varsa o, yoksa sunucudan
    /// çekilen kopya. Canlı graf ÖNCELİKLİ: kaydedilmemiş varyant değişikliklerini de bilir.</summary>
    private IReadOnlyList<ProductVariantGraphDto> EffectiveCoreVariants
    {
        get { return CoreVariants.Count > 0 ? CoreVariants : _coreVariantsFallback; }
    }

    // Kategori attribute grid satırları (def + o anki değerler) — hücre-içi düzenleme, paging yok.
    private List<N11AttributeCellRow> _attributeRows = new();

    // GPSR gürültüsü: N11 REST /cdn kategori-attribute yüzeyi, 3 gerçek zorunlu (Marka/Toplam Gram/Maden Ayarı)
    // yanına ~32 platform-geneli "Ürün Güvenliği/GPSR" opsiyonel alanı ekliyor. Zorunlular üstte HEP açık; opsiyoneller
    // sayaçlı KATLANMIŞ grupta (form kalabalıklaşmasın, kabiliyet kaybolmasın). İki grid aynı EditCell mantığını paylaşır.
    //
    // ALAN olarak tutulurlar, getter DEĞİL (kod-inceleme bulgusu): markup'tan çağrılan LINQ getter'ı her render'da YENİ
    // List üretiyordu → DxGrid.Data referansı her seferinde değişip veri kaynağını yeniden yüklüyordu (opsiyonel liste
    // ayrıca tek render'da ÜÇ kez hesaplanıyordu). Bölünme yalnız satır kümesi değişince (BuildAttributeRows) kurulur.
    private IReadOnlyList<N11AttributeCellRow> _mandatoryAttributeRows = Array.Empty<N11AttributeCellRow>();
    private IReadOnlyList<N11AttributeCellRow> _optionalAttributeRows = Array.Empty<N11AttributeCellRow>();

    /// <summary>Opsiyonel alanlardan kaçı dolu (grup başlığı sayacı) — satır sayısı küçük, hücre kaydında tazelenir.</summary>
    private int OptionalFilledCount
    {
        get { return _optionalAttributeRows.Count(r => !string.IsNullOrEmpty(r.CustomValue)); }
    }

    /// <summary>Zorunlu/opsiyonel bölünmesini kurar — <c>_attributeRows</c> her değiştiğinde çağrılır.</summary>
    private void SplitAttributeRows()
    {
        _mandatoryAttributeRows = _attributeRows.Where(r => r.IsMandatory).ToList();
        _optionalAttributeRows = _attributeRows.Where(r => !r.IsMandatory).ToList();
    }

    // Özellikler drill'i (kombinasyon ÜRETİMİ amaçlı — kategori-attribute-push'tan AYRI) — üst = özellik
    // (Model.ProductAttributes, ilk açılışta ERP'den klonlanmış taslak gelir), alt = özellik değerleri. İkisi de serbest
    // ekle/sil (klon-sonra-ayrış felsefesi).
    private DrillList<SalesChannelTrN11ProductAttributeDto>? _attributeDrill;
    private DrillList<SalesChannelTrN11ProductAttributeValueDto>? _attributeValueDrill;

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
        await EnsureCoreVariantsAsync();
        await EnsureTemplatesAsync();
        await EnsureAttributesAsync();
        await EnsurePreviewAsync();
    }

    // Çekirdek varyant listesini bir kez yükler — yalnız CoreVariants parametresi BOŞKEN (standalone yüzeyler);
    // ürün-formu yüzeyi canlı grafı zaten veriyor, ikinci bir sunucu okuması gürültü olurdu.
    private async Task EnsureCoreVariantsAsync()
    {
        if (CoreVariants.Count > 0
            || Model.ProductId == Guid.Empty
            || Model.ProductId == _loadedCoreVariantsProductId)
        {
            return;
        }

        _loadedCoreVariantsProductId = Model.ProductId;
        try
        {
            _coreVariantsFallback = (await RecipeCostAppService.GetAsync(Model.ProductId)).Variants;
        }
        catch (Exception ex)
        {
            _coreVariantsFallback = Array.Empty<ProductVariantGraphDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
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
            _units = FilterN11Currencies(await CurrencyLookup.GetAsync());
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // N11 yalnız TRY/USD/EUR ile listeleme kabul eder (2026-07-11 kullanıcı kararı) → para birimi seçicileri bu 3
    // koda daraltılır (tüm sistem dövizleri değil). Kod karşılaştırması normalize (UPPER-invariant) kodlarla.
    private static List<CurrencyUnitListDto> FilterN11Currencies(IEnumerable<CurrencyUnitListDto> units)
    {
        return units
            .Where(u => N11ProductConsts.SupportedCurrencyCodes.Contains(u.Code))
            .ToList();
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
    /// <summary>
    /// Satır kaydında koşan ZORUNLU alan doğrulaması — panel (drill SaveGuard) çağırır. Mesaj döner = kayıt engellenir.
    ///
    /// <para><b>Neden burada:</b> zorunlu nitelik tanımları (IsMandatory) bu bileşende yüklü; panel onları
    /// göremez. Push validator aynı kuralı zaten zorluyor ama PUSH anında — kullanıcı eksik satırı KAYDEDERKEN
    /// uyarılmazsa hatayı ancak push'ta, bağlamdan kopmuş halde görüyordu (2026-07-28 Hakan bulgusu).</para>
    ///
    /// <para>Nitelik tanımları henüz YÜKLENMEMİŞSE (kategori seçilmemiş/liste gelmedi) nitelik kontrolü SUSAR —
    /// yükleme gecikmesini "eksik" diye raporlamak yanlış alarm olurdu; kategori kontrolü zaten önce çalışır ve
    /// push validator son kapı olarak durur.</para>
    /// </summary>
    public string? ValidateMandatoryInputs()
    {
        if (string.IsNullOrWhiteSpace(Model.CategoryExternalId))
        {
            return L["N11Product:CategoryRequiredOnSave"].Value;
        }

        if (string.IsNullOrWhiteSpace(Model.ShipmentTemplateName))
        {
            return L["N11Product:ShipmentTemplateRequiredOnSave"].Value;
        }

        // Ad kıyası push validator ile AYNI (Türkçe kültür, harf duyarsız) — burada geçen push'ta da geçsin.
        var missing = _attributeDefs
            .Where(d => d.IsMandatory && !d.IsVariant)
            .Where(d => string.IsNullOrWhiteSpace(
                Model.CategoryAttributes.FirstOrDefault(a =>
                    string.Compare(a.Name?.Trim(), d.Name?.Trim(),
                        CultureInfo.GetCultureInfo("tr-TR"), CompareOptions.IgnoreCase) == 0)?.Value))
            .Select(d => d.Name)
            .ToList();

        return missing.Count > 0
            ? L["N11Product:MandatoryAttributesMissingOnSave", string.Join(", ", missing)].Value
            : null;
    }

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

            // Şablon ZORUNLU (N11 boş kabul etmez) ve combo AllowClear=false — boş açılması kullanıcıya
            // her yeni kanal ürününde tek seçenekli bir tıklama zorunluluğu bindiriyordu. Yalnız BOŞSA
            // doldurulur: kayıtlı bir ürünün seçimi asla ezilmez.
            if (string.IsNullOrWhiteSpace(Model.ShipmentTemplateName) && _templates.Count > 0)
            {
                Model.ShipmentTemplateName = _templates[0].TemplateName;
                MarkDirty(nameof(Model.ShipmentTemplateName));
            }
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

    // Def + Model.CategoryAttributes'taki mevcut değerden grid satırlarını kur (def sırası korunur) — her attribute
    // TEK DEĞERLİ: o Name'e ait ilk (tek) mevcut değer CustomValue'ya alınır (combo seçimi ya da serbest metin).
    private void BuildAttributeRows()
    {
        _attributeRows = _attributeDefs.Select(def =>
        {
            // N11'de bir attribute'ın hem valueList'i dolu hem isCustomValue=true olabilir (ör. Marka: 69 marka +
            // liste-dışı marka girişine izin). Combo'yu SADECE değer listesi doluluğuna bak → göster; custom giriş
            // AllowCustomValues ile ayrıca açılır.
            var existingValue = Model.CategoryAttributes.FirstOrDefault(a => a.Name == def.Name)?.Value ?? string.Empty;
            return new N11AttributeCellRow
            {
                AttributeId = def.AttributeId,
                Name = def.Name,
                IsMandatory = def.IsMandatory,
                HasValueList = def.Values.Count > 0,
                AllowCustomValues = def.IsCustomValue,
                DefinitionValues = def.Values,
                CustomValue = existingValue,
            };
        }).ToList();

        SplitAttributeRows();
    }

    // Hücre düzenlemesi kapanınca (EditCell — ayrı kaydet/düzenle tuşu yok) edit-model klonunun tek değerini orijinal
    // satıra + gerçek Model.CategoryAttributes'a ANINDA uygular: o Name'e ait eski girdi silinip (varsa) tek (Name,Value)
    // çifti yeniden kurulur; boş değer o attribute'ı kaldırır.
    private void OnAttributeRowSaving(GridEditModelSavingEventArgs e)
    {
        var edited = (N11AttributeCellRow)e.EditModel;
        if (e.DataItem is N11AttributeCellRow original)
        {
            original.CustomValue = edited.CustomValue;
        }

        Model.CategoryAttributes.RemoveAll(a => a.Name == edited.Name);
        if (!string.IsNullOrWhiteSpace(edited.CustomValue))
        {
            Model.CategoryAttributes.Add(new SalesChannelTrN11ProductCategoryAttributeDto { Name = edited.Name, Value = edited.CustomValue });
        }

        MarkDirty(nameof(Model.CategoryAttributes));
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

    // ── Özellikler (kombinasyon üretimi) — sıra no + boş-alan guard'ları (Product ProductAttributeGraphDto
    // deseniyle AYNI: silinmemişlerin max sırası + 1).
    private static int NextAttributeOrder(IEnumerable<SalesChannelTrN11ProductAttributeDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private static int NextAttributeValueOrder(IEnumerable<SalesChannelTrN11ProductAttributeValueDto> items)
    {
        return items.Where(x => !x.IsDeleted).Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    private string? AttributeSaveGuard(SalesChannelTrN11ProductAttributeDto item)
    {
        return string.IsNullOrWhiteSpace(item.Name) ? L["N11Product:AttributeNameRequired"].Value : null;
    }

    private string? AttributeValueSaveGuard(SalesChannelTrN11ProductAttributeValueDto item)
    {
        return string.IsNullOrWhiteSpace(item.Value) ? L["N11Product:AttributeValueRequired"].Value : null;
    }

    // Özellik/değer grafını PERSIST EDER + kartezyen reconcile'ı hemen tetikler — yalnız KAYDEDİLMİŞ (Id'li) kayıtta.
    // Tüm ürünü kaydetmeye gerek yok (RegenerateStockItemsAsync, Full Update ile AYNI reconcile mekanizmasını kullanır).
    private async Task RegenerateStockItemsAsync()
    {
        if (Model.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var result = await ProductAppService.RegenerateStockItemsAsync(Model.Id, Model.ProductAttributes);
            Model.ProductAttributes = result.ProductAttributes;
            Model.StockItems = result.StockItems;
            MarkDirty(nameof(Model.StockItems));
            UiService.ShowSuccessToast(string.Format(L["N11Product:StockItemsRegenerated"].Value, Model.StockItems.Count));
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yan-maliyet satırlarını kanal gider ayarlarından TAZELER ("yeniden uygula") — yalnız KAYDEDİLMİŞ kayıtta.
    // İdempotent: otomatik (SideCostKind işaretli) satırlar yeniden üretilir, kullanıcı satırlarına dokunulmaz;
    // silinen otomatik satır da bununla geri gelir (kendiliğinden GERİ GELMEZ — açık tetik).
    private async Task ReapplySideCostsAsync()
    {
        if (Model.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var result = await ProductAppService.ReapplySideCostsAsync(Model.Id);
            Model.StockItems = result.StockItems;
            MarkDirty(nameof(Model.StockItems));
            UiService.ShowSuccessToast(L["SideCost:Reapplied"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Varyant grid/edit'te kod hücresi — ERP-backed satırda ERP kodu, N11-only satırda (ERP karşılığı yok)
    // özellik-değer özeti (CombinationLabel); ikisi de boşsa "-" (henüz reconcile edilmemiş/legacy taslak).
    private static string StockItemCodeOrLabel(SalesChannelTrN11ProductStockItemGraphDto stockItem)
    {
        if (!string.IsNullOrEmpty(stockItem.VariantCode))
        {
            return stockItem.VariantCode;
        }

        return string.IsNullOrEmpty(stockItem.CombinationLabel) ? "-" : stockItem.CombinationLabel;
    }

    // ── "Üründen emtia yarat" — KANAL formundan (yayılım adımı 2026-08-14'te kuruldu) ────────────────

    /// <summary>
    /// Panelin "Üründen" akışı — <c>ProductEditHost.CreateCommodityFromProductAsync</c>'in kanal-form eşi,
    /// bir farkla: çekirdek formda satır panelin draft'ıyla çekirdek grafına girer; burada draft KANAL
    /// grafına gireceğinden emtia satırı ÇEKİRDEK varyanta AYRICA yazılmalıdır (otorite orada — stok zinciri
    /// ve rezervasyon yalnız çekirdek reçeteyi okur). Yayılım sihirbazın mevcut ucuyla yapılır
    /// (<c>ProvisionCommoditiesAsync</c> · UseExisting): reçete satırı + StockPolicy + otorite devri +
    /// stok senkronu tek kapıdan, ikinci bir yazım yolu açılmadan. Trendyol EditFields simetriği.
    /// </summary>
    private async Task<Guid?> CreateCommodityFromProductAsync(ProcessType family)
    {
        if (Model.ProductId == Guid.Empty)
        {
            return null;
        }

        if (ProductCommoditySeed.EditComponentOf(family) is not { } editComponent)
        {
            return null;
        }

        var before = CommodityIdsOf(family);

        // Kod/ad SUNUCUDAN okunur — emtia, kayıtlı ürünün kimliğinden doğar (ProductEditHost ile aynı gerekçe).
        var product = await RecipeCostAppService.GetAsync(Model.ProductId);
        var extra = await ProductCommoditySeed.BuildExtraParamsAsync(
            family, Model.ProductId, product.Code, product.Name, RecipeCostAppService);

        await ViewOpener.OpenAsync(
            editComponent, null, L[$"Enum:ProcessType:{family}"].Value, iconCssClass: null, extraParams: extra);

        // Katalogları ZORLA tazele — bayrak tek-seferlik; sıfırlanmazsa panel yeni emtiayı göremez ve
        // önce/sonra farkı boş çıkar.
        _catalogsLoaded = false;
        await EnsureRecipeCatalogsAsync();

        var created = CommodityIdsOf(family).Except(before).ToList();
        if (created.Count != 1)
        {
            return null;   // vazgeçildi (boş küme) ya da eşzamanlı ikinci oluşum (belirsiz) — draft açılmaz
        }

        var result = await RecipeCostAppService.ProvisionCommoditiesAsync(new ProductCommodityProvisionInputDto
        {
            Items =
            {
                new ProductCommodityProvisionItemDto
                {
                    ProductId = Model.ProductId,
                    Family = family,
                    Mode = ProductCommodityProvisionMode.UseExisting,
                    ExistingCommodityId = created[0],
                },
            },
        });
        if (result.Issues.Count > 0)
        {
            UiService.ShowWarningToast(string.Join(" · ", result.Issues));
        }

        // Otorite devri kanal aynasını (Override*) sunucuda temizledi — AÇIK formdaki kopya da hizalanır;
        // aksi hâlde sonraki Kaydet, devrin sildiği pazaryeri stok/fiyatını sessizce geri diriltirdi.
        if (result.ClearedChannelOverrides > 0)
        {
            foreach (var stockItem in Model.StockItems)
            {
                stockItem.OverridePrice = null;
                stockItem.OverrideStock = null;
            }

            MarkDirty(nameof(Model.StockItems));
        }

        return created[0];
    }

    /// <summary>Ailenin yüklü katalog kimlik kümesi (önce/sonra farkı için) — <c>ProductEditHost.CommodityIdsOf</c>
    /// ile aynı kapsam: Service katalog kaydı taşımaz (boş küme → "Üründen" Service'te provizyona gitmez).</summary>
    private HashSet<Guid> CommodityIdsOf(ProcessType family)
    {
        return family switch
        {
            ProcessType.Metal   => _metals.Select(x => x.Id).ToHashSet(),
            ProcessType.Scrap   => _scraps.Select(x => x.Id).ToHashSet(),
            ProcessType.Future  => _futures.Select(x => x.Id).ToHashSet(),
            ProcessType.Jewelry => _jewelries.Select(x => x.Id).ToHashSet(),
            ProcessType.Stone   => _stones.Select(x => x.Id).ToHashSet(),
            ProcessType.Good    => _goods.Select(x => x.Id).ToHashSet(),
            _                   => new HashSet<Guid>(),
        };
    }

    // ── Kanal-özel varyant override'ları (fiyat/stok/marj + reçete) ──────────────────────────────────

    // Reçete satırı eklendi/değişti/silindi → CANLI net maliyet (ERP ile ORTAK persistsiz hesap motoru) + türetilmiş
    // fiyat yeniden hesaplanır (satır maliyet alanları grid'e döner). Tam kayıt gerekmez.
    private async Task HandleStockItemRecipeChangedAsync(SalesChannelTrN11ProductStockItemGraphDto stockItem)
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
    private void OnStockItemMarginChanged(SalesChannelTrN11ProductStockItemGraphDto stockItem, decimal? margin)
    {
        stockItem.Margin = margin;
        RecomputeDerivedPrice(stockItem);
        MarkDirty(nameof(Model.StockItems));
    }

    // Override fiyat para birimi (ValueExpression'sız) → değeri yaz + dirty.
    private void OnOverrideCurrencyChanged(SalesChannelTrN11ProductStockItemGraphDto stockItem, Guid? currencyUnitId)
    {
        stockItem.OverridePriceCurrencyUnitId = currencyUnitId;
        MarkDirty(nameof(Model.StockItems));
    }

    // Türetilmiş fiyat — backend ile AYNI merkezi formül (DerivedPriceCalculator); kur eksik/NetCost yoksa null.
    private static void RecomputeDerivedPrice(SalesChannelTrN11ProductStockItemGraphDto stockItem)
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

    // ValueExpression'sız editörler EditContext'e bildirmez → dirty ELLE tetiklenir (DrillList Save aktifliği).
    private void MarkDirty(string fieldName)
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, fieldName));
    }
}
