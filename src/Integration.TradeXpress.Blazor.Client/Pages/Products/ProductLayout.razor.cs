using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.VariantTemplates;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product dumb layout code-behind — Model bağlama + varyant drill referansı + dirty cascade.</summary>
public partial class ProductLayout
{
    // KDV combo verisi — mevzuat SSOT'u (ProductConsts.AllowedVatRates). Ürün seviyesinde seçilen oran
    // kanal kayıtlarına devralınır; kanalda özel bir oran gerekiyorsa orada ezilir.
    private static readonly List<VatRateOption> VatRateOptions = VatRateOption.From(ProductConsts.AllowedVatRates);

    [Parameter, EditorRequired] public ProductGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    // ── KADEMELİ KİLİT (yalnız YENİ üründe): kategori → kod → ad → geri kalan ────────────────────────
    //
    // 2026-08-20 Hakan talimatı. Gerekçe kategoride: ürün nitelikleri, kanal kategori eşleşmesi ve zorunlu
    // alanlar kategoriden TÜREDİĞİ için sonradan değiştirmek kullanıcının doldurduğu alanların bir kısmını
    // geçersizleştiriyor. Kod/ad ise kaydın kimliğidir (form standardında da ilk iki alan).
    //
    // KAYITLI üründe kilit YOKTUR: mevcut ürün bu adımlardan zaten geçmiştir; alanlarını kilitlemek düzeltme
    // yapmayı imkânsızlaştırırdı.
    //
    // Ölçüt "dolu mu", "geçerli mi" DEĞİL: uzunluk/benzersizlik kuralları kaydetme yolunun işidir (entity +
    // AppService). Kilit yalnız SIRAYI dayatır; yazarken kullanıcıyı ikinci bir doğrulamayla boğmaz.

    /// <summary>Kod alanı düzenlenebilir mi — kategori seçilince açılır.</summary>
    private bool CanEditCode
    {
        get { return !IsNew || Model.ProductCategoryId is not null; }
    }

    /// <summary>Ad alanı düzenlenebilir mi — kod girilince açılır.</summary>
    private bool CanEditName
    {
        get { return CanEditCode && (!IsNew || !string.IsNullOrWhiteSpace(Model.Code)); }
    }

    /// <summary>Formun geri kalanı (diğer alanlar, gruplar ve sekmeler) düzenlenebilir mi — ad girilince açılır.</summary>
    private bool CanEditRest
    {
        get { return CanEditName && (!IsNew || !string.IsNullOrWhiteSpace(Model.Name)); }
    }

    /// <summary>Sıradaki adımın metni; kilit yoksa <c>null</c> (ipucu çizilmez). Kilitli bir alanın NEDEN kilitli
    /// olduğu yazılmazsa kullanıcı formu bozuk sanar — bu yüzden kilit sessiz değildir.</summary>
    private string? NewProductStepHint
    {
        get
        {
            if (!IsNew || CanEditRest)
            {
                return null;
            }

            if (!CanEditCode)
            {
                return L["Product:NewStepSelectCategory"].Value;
            }

            if (!CanEditName)
            {
                return L["Product:NewStepEnterCode"].Value;
            }

            return L["Product:NewStepEnterName"].Value;
        }
    }

    // Reçete drill'inin katalog lookup verisi — host yükler (DUMB layout servis çağırmaz).
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();
    [Parameter] public IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; set; } = Array.Empty<MetalVariantLookupDto>();
    [Parameter] public IReadOnlyList<ScrapListDto> Scraps { get; set; } = Array.Empty<ScrapListDto>();
    [Parameter] public IReadOnlyList<FutureListDto> Futures { get; set; } = Array.Empty<FutureListDto>();
    [Parameter] public IReadOnlyList<JewelryListDto> Jewelries { get; set; } = Array.Empty<JewelryListDto>();
    [Parameter] public IReadOnlyList<GoodListDto> Goods { get; set; } = Array.Empty<GoodListDto>();
    [Parameter] public IReadOnlyList<CommodityVariantLookupDto> GoodVariants { get; set; } = Array.Empty<CommodityVariantLookupDto>();
    [Parameter] public IReadOnlyList<CommodityVariantLookupDto> JewelryVariants { get; set; } = Array.Empty<CommodityVariantLookupDto>();
    [Parameter] public IReadOnlyList<StoneListDto> Stones { get; set; } = Array.Empty<StoneListDto>();
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    /// <summary>Kategori popup'ından YENİ kategori isteniyor — host katalog formunu açar, dönen id seçilir.
    /// Layout DUMB kalır (ViewOpener'ı host kullanır).</summary>
    [Parameter] public Func<Task<Guid?>>? OnAddProductCategory { get; set; }

    /// <summary>Seçili kategoriyi düzenleme isteği — host katalog formunu açar, kapanınca liste tazelenir.</summary>
    [Parameter] public Func<Guid, Task>? OnEditProductCategory { get; set; }

    /// <summary>Ülke katalogu (menşei seçimi) — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<CountryListDto> Countries { get; set; } = Array.Empty<CountryListDto>();

    /// <summary>Varsayılan para birimi lookup verisi — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>Inline döviz ekle/düzelt sonrası lookup listesini host tazeler (EntityChange tetikler).</summary>
    [Parameter] public EventCallback OnReloadCurrencyUnits { get; set; }

    /// <summary>Eklenti katalogu lookup verisi — host yükler (DUMB layout servis çağırmaz). "Seçenekler" sekmesinde
    /// katalogdan seçim için.</summary>
    [Parameter] public IReadOnlyList<AddOnListDto> AddOnCatalog { get; set; } = Array.Empty<AddOnListDto>();

    /// <summary>Inline eklenti ekle/düzelt sonrası katalog listesini host tazeler (EntityChange tetikler).</summary>
    [Parameter] public EventCallback OnReloadAddOns { get; set; }

    /// <summary>Kargo şablonu lookup verisi — host yükler (DUMB layout servis çağırmaz). Ürün formunda
    /// varsayılan kargo şablonu ataması için (GetPickerListAsync).</summary>

    /// <summary>Inline kargo şablonu ekle/düzelt sonrası lookup listesini host tazeler (EntityChange tetikler).</summary>

    // Nitelik + varyant drill'leri JENERİK paylaşılan panellerde (EntityAttributesPanel / EntityVariantsPanel).
    // Legacy görsel drill'i 2026-07-31'de emekli — görseller merkezi DAM panelinde (EntityMediaPanel + varyant sekmesi).

    /// <summary>Özel bilgi satırı kaydetme engeli — key boşsa satır kabul edilmez (SetSpecialInfo sunucuda da boş key eler).</summary>
    private string? SpecialInfoSaveGuard(ProductSpecialInfoDto item)
    {
        return string.IsNullOrWhiteSpace(item.Key) ? L["Product:SpecialInfoKeyRequired"].Value : null;
    }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>Reçete değişince CANLI maliyet — host yapar (persistsiz hesap, varyant bazında); tam kayıt gerekmez.</summary>
    [Parameter] public Func<ProductVariantGraphDto, Task>? OnRecipeChanged { get; set; }

    /// <summary>"Üründen emtia yarat" — layout DİLSİZDİR, işi host yapar (katalogların sahibi o). Bu delege
    /// yalnız çağrıyı geçirir; host bağlı değilse akış SESSİZCE durur ve panel draft açmaz.</summary>
    [Parameter] public Func<ProcessType, Task<Guid?>>? OnCreateCommodityFromProduct { get; set; }

    /// <summary>Reçete satırı eklenince/değişince/silinince: önce CANLI maliyet (host), sonra form dirty.</summary>
    private async Task HandleRecipeChangedAsync(ProductVariantGraphDto variant)
    {
        if (OnRecipeChanged is not null)
        {
            await OnRecipeChanged(variant);
        }

        EditChanged?.Invoke();
    }

    /// <summary>Nitelik/değer değişince (EntityAttributesPanel.OnAttributesChanged) host varyantları OTOMATİK yeniden
    /// üretir (VariantGraphMerge — kullanıcı düzenlemeleri korunur). Layout DUMB kalır (servis çağırmaz); işi host yapar.</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    /// <summary>Çekirdek ürün kategorisi katalogu (yol sıralı) — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<ProductCategoryListDto> ProductCategories { get; set; } = Array.Empty<ProductCategoryListDto>();

    /// <summary>Inline kategori ekle/düzelt sonrası katalog listesini host tazeler (yeni kategori anında combo'ya düşsün).</summary>
    [Parameter] public EventCallback OnReloadProductCategories { get; set; }

    /// <summary>Seçili kategorinin SPESİFİKASYON nitelikleri (kalıtım çözülmüş) — host yükler, kategori
    /// değişince tazeler. Layout DUMB kalır (servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<ProductCategoryEffectiveAttributeDto> CategorySpecificationAttributes { get; set; }
        = Array.Empty<ProductCategoryEffectiveAttributeDto>();

    /// <summary>
    /// Formda gösterilen özellik satırları — KATEGORİ tanımı sürücüdür, ürünün kayıtlı değerleri değil.
    ///
    /// <para><b>Neden kategoriden türetiliyor:</b> ürün daha önce hiç değer girmemişse yine de sorulmalı;
    /// kayıtlı satırlardan üretilseydi yeni ürün boş bir ekran görür ve hangi özellikleri girmesi gerektiğini
    /// bilemezdi. Kayıtlı değer varsa satıra yerleşir, yoksa boş açılır.</para>
    ///
    /// <para><b>Neden ALANDA tutuluyor (getter değil):</b> grid satır NESNESİNİ kimlik olarak kullanır — her
    /// render'da yeni liste üretilseydi hücre düzenlemesi açıldığı anda kapanırdı.</para>
    ///
    /// <para>Kategoride ARTIK OLMAYAN bir niteliğin eski değeri gösterilmez — sunucu da kaydetmede onu siler;
    /// göstermek, kullanıcının düzenleyebildiğini sandığı ama hiçbir yere gitmeyen bir alan üretirdi.</para>
    /// </summary>
    // Varyant kapsamı ağacı — "Tümünü Seç" başlıktaki butondan çağrılır (panel eylemi dışarı açar).
    private SubstitutionVariantTreePanel? _variantScopePanel;

    private List<SpecificationRow> _specificationRows = new();

    // Satırların kurulduğu kaynak — referans değişince (host yeni kategorinin niteliklerini yükledi) yeniden kurulur.
    private IReadOnlyList<ProductCategoryEffectiveAttributeDto>? _specificationRowsSource;

    private List<SpecificationRow> SpecificationRows => _specificationRows;

    private void RebuildSpecificationRowsIfChanged()
    {
        if (ReferenceEquals(_specificationRowsSource, CategorySpecificationAttributes))
        {
            return;
        }

        _specificationRowsSource = CategorySpecificationAttributes;
        _specificationRows = CategorySpecificationAttributes
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new SpecificationRow
            {
                AttributeId = a.AttributeId,
                Name = a.Name,
                Options = a.Values.OrderBy(v => v.DisplayOrder).Select(v => v.Value).ToList(),
                Value = Model.Specifications
                    .FirstOrDefault(sp => sp.ProductCategoryAttributeId == a.AttributeId)?.Value ?? string.Empty,
            })
            .ToList();
    }

    /// <summary>Hücre düzenlemesi kaydedildi — düzenlenen satırı KİMLİĞİYLE bulup değeri hem satıra hem
    /// <c>Model.Specifications</c>'a uygular (grid düzenleme için satırın KOPYASINI verir; kopyayı listeye
    /// koymak canlı satırı bayat bırakırdı). Boş değer satırı KALDIRIR — sunucu da boş satır saklamaz.</summary>
    private void OnSpecificationRowSaving(GridEditModelSavingEventArgs e)
    {
        if (e.EditModel is not SpecificationRow edited)
        {
            return;
        }

        var row = _specificationRows.FirstOrDefault(r => r.AttributeId == edited.AttributeId);
        if (row is null)
        {
            return;
        }

        row.Value = edited.Value?.Trim() ?? string.Empty;

        Model.Specifications.RemoveAll(sp => sp.ProductCategoryAttributeId == row.AttributeId);
        if (!string.IsNullOrWhiteSpace(row.Value))
        {
            Model.Specifications.Add(new ProductSpecificationDto
            {
                ProductCategoryAttributeId = row.AttributeId,
                Name = row.Name,
                Value = row.Value,
            });
        }

        EditChanged?.Invoke();
    }

    /// <summary>Bir özellik satırı: nitelik kimliği + adı + kategoriden gelen ÖNERİLEN değerler + girilen değer.
    /// Grid hücre-içi düzenlemede satırın KOPYASINI ürettiğinden MUTABLE sınıf (record değil).</summary>
    private sealed class SpecificationRow
    {
        public Guid AttributeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public string Value { get; set; } = string.Empty;

        /// <summary>Kategoride tanımlı değer listesi var mı — varsa combo, yoksa serbest metin.</summary>
        public bool HasOptions => Options.Count > 0;
    }

    /// <summary>Reçete şablonu ("orta reçete") katalogu — host yükler.</summary>
    [Parameter] public IReadOnlyList<RecipeTemplateListDto> RecipeTemplates { get; set; } = Array.Empty<RecipeTemplateListDto>();

    /// <summary>Inline reçete şablonu ekle/düzelt sonrası katalog listesini host tazeler.</summary>
    [Parameter] public EventCallback OnReloadRecipeTemplates { get; set; }

    /// <summary>Seçilen reçete şablonunu ürüne uygulama İSTEĞİ — host şablonu ürünün varyantlarına serer.</summary>
    [Parameter] public Func<Guid, Task>? OnApplyRecipeTemplate { get; set; }

    /// <summary>Varyant şablonu katalogu — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<VariantTemplateListDto> VariantTemplates { get; set; } = Array.Empty<VariantTemplateListDto>();

    /// <summary>Inline şablon ekle/düzelt sonrası katalog listesini host tazeler (yeni şablon anında combo'ya düşsün).</summary>
    [Parameter] public EventCallback OnReloadVariantTemplates { get; set; }

    /// <summary>Seçilen şablonu ürüne uygulama İSTEĞİ — host şablonu yükleyip nitelik grafına katar.</summary>
    [Parameter] public Func<Guid, Task>? OnApplyVariantTemplate { get; set; }

    /// <summary>Combo DAİMA boş durur: şablon bir KAYNAK, ürünle kalıcı bağ kurmaz — üzerinde seçili kalması
    /// "bu ürün o şablona bağlı" izlenimi verirdi. Boş kalması aynı şablonun tekrar uygulanmasına da izin verir.</summary>
    private Guid? _selectedVariantTemplateId;

    /// <summary>Seçili kategori kanal eşleştirmesiz mi — uyarı bunun için gösterilir. Kategori seçili DEĞİLSE
    /// uyarı çıkmaz: o durumda zaten "kategori zorunlu" hatası konuşur, iki mesaj birden gürültü olurdu.
    ///
    /// <para>Katalog listesi henüz yüklenmediyse (satır bulunamıyorsa) da SUSAR — yükleme gecikmesini
    /// "eşleştirme yok" diye raporlamak yanlış alarm üretirdi.</para></summary>
    private bool ShowMissingChannelMappingWarning
    {
        get
        {
            if (Model.ProductCategoryId is not { } categoryId)
            {
                return false;
            }

            var category = ProductCategories.FirstOrDefault(c => c.Id == categoryId);
            return category is not null && !category.HasChannelMapping;
        }
    }

    /// <summary>Talep miktarı caption'ının birim eki — SEÇİLİ MUADİL GRUBUNUN birimi (" (lt)"). Etiket
    /// eskiden "(gr)" olarak SABİTTİ; litre/kilo ile çalışan bir grupta yanlış birim gösteriyordu
    /// (2026-07-28 Hakan). Grup seçilmemişse ya da birimi boşsa ek çıkmaz.</summary>
    private string SubstitutionUnitSuffix
    {
        get
        {
            if (Model.SubstitutionGroupId is not { } groupId)
            {
                return string.Empty;
            }

            var unit = SubstitutionGroups.FirstOrDefault(g => g.Id == groupId)?.QuantityUnit;
            return string.IsNullOrWhiteSpace(unit) ? string.Empty : $" ({unit})";
        }
    }

    /// <summary>İndirim tipi değişti — tip seçilince değer 0'a KURULUR (boş kalmasın: alan zorunlu ve boş
    /// bir sayı kutusu "girdim mi girmedim mi" belirsizliği yaratıyordu). "İndirim yok"a dönülünce değer ve
    /// tarihler temizlenir — sunucu da aynısını yapıyor (SetDiscount), iki taraf hizalı kalsın.</summary>
    private void OnDiscountTypeChanged(ProductDiscountType type)
    {
        Model.DiscountType = type;
        if (type == ProductDiscountType.None)
        {
            Model.DiscountValue = null;
            Model.DiscountStartDate = null;
            Model.DiscountEndDate = null;
        }
        else
        {
            Model.DiscountValue ??= 0m;
        }

        EditChanged?.Invoke();
    }

    /// <summary>İndirim değeri caption'ının birim eki — TUTAR tipinde ürünün para birimi kodu (" (TRY)"),
    /// yüzde tipinde ya da birim seçilmemişken boş. Para birimi katalogda bulunamazsa da boş döner (bayat
    /// id yüzünden caption'a ham Guid yazılmasın).</summary>
    private string DiscountValueUnitSuffix
    {
        get
        {
            if (Model.DiscountType != ProductDiscountType.Amount || Model.CurrencyUnitId is not { } unitId)
            {
                return string.Empty;
            }

            var code = CurrencyUnits.FirstOrDefault(u => u.Id == unitId)?.Code;
            return string.IsNullOrWhiteSpace(code) ? string.Empty : $" ({code})";
        }
    }

    private async Task<Guid?> OnAddProductCategoryRequested()
    {
        return OnAddProductCategory is null ? null : await OnAddProductCategory();
    }

    private async Task OnEditProductCategoryRequested(Guid? categoryId)
    {
        if (OnEditProductCategory is not null && categoryId is { } id && id != Guid.Empty)
        {
            await OnEditProductCategory(id);
        }
    }

    /// <summary>Kategori seçimi — şablondan FARKLI olarak KALICI bir bağdır (combo seçili kalır); ürünün
    /// kanal kategorisi, kanal nitelikleri ve komisyonu bu bağdan çözülecek.</summary>
    private async Task OnProductCategoryChanged(Guid? categoryId)
    {
        Model.ProductCategoryId = categoryId;

        // Özellik alanları kategoriye bağlı — host yeni kategorinin niteliklerini çeker (layout DUMB kalır).
        // İşaret burada da güncellenir ki OnParametersSetAsync aynı kategoriyi İKİNCİ kez istemesin.
        if (OnProductCategorySelected is not null)
        {
            _requestedSpecificationCategoryId = categoryId;
            await OnProductCategorySelected(categoryId);
        }

        EditChanged?.Invoke();
    }

    /// <summary>Kategori seçimi değişti — host "Özellikler" sekmesinin nitelik listesini tazeler.</summary>
    [Parameter] public Func<Guid?, Task>? OnProductCategorySelected { get; set; }

    /// <summary>Reçete şablonu seçimi artık ÜRÜNE KAYITLIDIR (2026-07-28 Hakan): muadil motoru stok değişince
    /// kombinasyonları yeniden üretiyor ve ara masraf satırlarını (paketleme/kargo/sigorta) bu şablondan
    /// tazeleyecek — form ömürlü bir seçim, yeniden üretimde o satırların kaynağını bilinemez kılardı.</summary>
    private Guid? _selectedRecipeTemplateId
    {
        get { return Model.RecipeTemplateId; }
        set { Model.RecipeTemplateId = value; }
    }

    /// <summary>Şablon bloğunun altındaki ipucu — YENİ üründe METİN DEĞİŞİR.
    ///
    /// <para><b>Neden iki metin:</b> kaydedilmemiş üründe "Uygula" düğmesi gizlidir (uygulanacak varyant
    /// henüz yok) ve düğmeyi göremeyen kullanıcı seçimin işe yaramadığını sanardı. İpucu, uygulamanın
    /// ertelendiğini — ilk kayıtta kendiliğinden bir kez yapılacağını — SÖYLER; kilitli/gizli bir kontrolün
    /// nedenini yazmamak formu bozuk gösterir (kademeli kilidin ipucuyla aynı gerekçe).</para></summary>
    private string RecipeTemplateHint
    {
        get
        {
            if (IsNew)
            {
                return L["RecipeTemplate:ApplyOnSaveHint"].Value;
            }

            return L["RecipeTemplate:ApplyHint"].Value;
        }
    }

    private void OnRecipeTemplateSelected(Guid? templateId)
    {
        _selectedRecipeTemplateId = templateId;
        EditChanged?.Invoke();
    }

    private async Task ApplySelectedRecipeTemplateAsync()
    {
        if (_selectedRecipeTemplateId is not { } id || OnApplyRecipeTemplate is null)
        {
            return;
        }

        await OnApplyRecipeTemplate(id);
    }

    private async Task ApplyVariantTemplateAsync(Guid? templateId)
    {
        if (templateId is not { } id || OnApplyVariantTemplate is null)
        {
            return;
        }

        await OnApplyVariantTemplate(id);
        _selectedVariantTemplateId = null;
    }

    // ── Varyant modu + Muadil (Dilim-3) — layout DUMB: onay/servis işleri host'ta, burada yalnız bağlama ──

    /// <summary>Varyant modu değişim İSTEĞİ — host onaylar (MultiVariant'tan çıkışta veri-kaybı uyarısı) ve
    /// modeli günceller; reddederse model değişmez (combo eski değere geri çizilir).</summary>
    [Parameter] public Func<ProductVariantMode, Task>? OnVariantModeChangeRequested { get; set; }

    /// <summary>Muadil grubu lookup verisi — host yükler (aktif gruplar).</summary>
    [Parameter] public IReadOnlyList<SubstitutionGroupListDto> SubstitutionGroups { get; set; } = Array.Empty<SubstitutionGroupListDto>();

    /// <summary>Inline muadil grubu ekle/düzelt sonrası lookup listesini host tazeler.</summary>
    [Parameter] public EventCallback OnReloadSubstitutionGroups { get; set; }

    /// <summary>Reçete panelindeki katalog lookup'larından biri (maden/hurda/vadeli/mücevher/taş/mamul/hizmet)
    /// "Ekle/Düzelt" ile kayıt yapınca host katalog listelerini yeniden yükler — bağlı değilse yeni kayıt combo'da
    /// görünmez (2026-08-15 Hakan bulgusu).</summary>
    [Parameter] public EventCallback OnReloadRecipeCatalogs { get; set; }

    /// <summary>Seçili grubun kalemleri (override ağacının devralınan-küme referansı) — host yükler.</summary>
    [Parameter] public List<SubstitutionGroupItemGraphDto> SubstitutionGroupItems { get; set; } = new();

    /// <summary>Grup seçimi değişince host kalemleri yeniden yükler (ilk açılışta da tetiklenir — guard'lı).</summary>
    [Parameter] public EventCallback<Guid?> OnSubstitutionGroupChanged { get; set; }

    /// <summary>Son kombinasyon hesabı sonucu (host durumu; salt görüntü).</summary>
    [Parameter] public SubstitutionCalculationResultDto? SubstitutionResult { get; set; }

    /// <summary>Hesap koşuyor mu (buton kilidi).</summary>
    [Parameter] public bool SubstitutionBusy { get; set; }

    /// <summary>"Kombinasyon Hesapla" — host CalculateAsync'i override'lı çağırır.</summary>
    [Parameter] public EventCallback OnCalculateSubstitution { get; set; }

    /// <summary>Otomatik yeniden hesabın bekleme süresi — kullanıcı arka arkaya kutu işaretlerken her tık
    /// için sunucuya gidilmesin.</summary>
    private const int SubstitutionRecalcDelayMs = 400;

    /// <summary>Bekleyen otomatik hesap — yeni değişiklik öncekini iptal eder (son yazan kazanır).</summary>
    private CancellationTokenSource? _substitutionRecalcCts;

    /// <summary>
    /// Hesap sonucundaki kombinasyonları VARYANT listesine yansıtır — kaydetmeden. Sunucu bunu kayıt anında
    /// zaten yapıyor (SubstitutionVariantMaterializer); burada aynı sonucun ÖNİZLEMESİ kurulur ki kullanıcı
    /// kapsamı/miktarı değiştirir değiştirmez varyantları görsün (2026-07-27 Hakan kararı).
    ///
    /// <para><b>Kod eşleşmesi kimliği korur:</b> mevcut varyantın kodu hesapta da varsa satır YERİNDE kalır —
    /// stok/barkod/GTIN ve kanal SKU bağları kopmaz. Kod üreticisi sunucuyla TEK kaynak
    /// (<c>SubstitutionCombinationCodeBuilder</c>), yani önizlemedeki kod kayıttakiyle birebir aynıdır.</para>
    ///
    /// <para><b>Ana varyant:</b> en iyi sıradaki kombinasyon ana olur (sunucudaki Rank 1 kuralı).
    /// Muadil dışı modlarda hiç çalışmaz.</para>
    /// </summary>
    /// <summary>Şablon satırının BAŞKA bir varyanta serilecek kopyası — kimlik alanları sıfırlanır
    /// (<c>Id</c> boş = yeni satır, <c>ClientKey</c> taze): aynı Id iki varyantta görünseydi kaydetmede
    /// satırlar birbirini ezerdi.</summary>
    private static ProductRecipeLineGraphDto KlonlaSablonSatiri(ProductRecipeLineGraphDto kaynak)
    {
        var json = JsonSerializer.Serialize(kaynak);
        var kopya = JsonSerializer.Deserialize<ProductRecipeLineGraphDto>(json)!;
        kopya.Id = Guid.Empty;
        kopya.ClientKey = Guid.NewGuid();
        kopya.IsDeleted = false;
        // Bayat maliyet gösterme: LineCost/AppliedBase ESKİ varyantın hesabından — sıfırlanır, doğrusu
        // hemen ardından koşan sunucu hesabıyla dolar.
        kopya.LineCost = null;
        kopya.AppliedBase = null;
        return kopya;
    }

    private void SyncSubstitutionVariantPreview()
    {
        if (Model.VariantMode != ProductVariantMode.Substitution || SubstitutionResult is null)
        {
            return;
        }

        // Seçim kuralı SUNUCUYLA ORTAK (SubstitutionVariantSelection) — önizlemede başka, kayıtta başka
        // varyant çıkmasın diye kural tek yerde yaşar.
        var secilenler = SubstitutionVariantSelection.Select(
            SubstitutionResult.Trials, Model.SubstitutionVariantMode);

        // Hesap hiç başarılı kombinasyon vermediyse (stok yok/tolerans dar) mevcut varyantlara DOKUNULMAZ:
        // ekranı boşaltmak, kullanıcının kayıtlı varyantlarını yok olmuş gibi gösterirdi.
        if (secilenler.Count == 0)
        {
            return;
        }

        var mevcutlar = Model.Variants.Where(v => !v.IsDeleted).ToList();
        var yeniListe = new List<ProductVariantGraphDto>();

        // ŞABLON PROTOTİPİ — mevcut varyantlardan birindeki şablon (ara masraf) satırları. Yeni doğan
        // kombinasyonlara bunun KOPYASI serilir: hedef miktar değişince varyant kümesi baştan kuruluyor ve
        // kombinasyon satırları şablon satırlarını eziyordu; kullanıcı paketleme/kargo/sigortayı her seferinde
        // elle yeniden uygulamak zorunda kalıyor, unuttuğunda fiyat sessizce eksik çıkıyordu (2026-07-28 Hakan).
        // Prototip YALNIZ dokunulmamış şablon satırlarından seçilir (TemplateEdited HARİÇ) — sunucunun kardeşten
        // çoğaltma sorgusuyla (SubstitutionVariantMaterializer) BİREBİR aynı küme olsun diye: önizlemenin kaydın
        // ne yapacağını göstermesi gerekir, kullanıcının bir varyanta özgü düzeltmesini yeni kombinasyonlarda
        // göstermek kaydedilmeyecek bir sayı vaat ederdi.
        var sablonPrototipi = mevcutlar
            .Select(v => v.RecipeLines.Where(l => !l.IsDeleted && l.Origin == RecipeLineOrigin.Template).ToList())
            .FirstOrDefault(l => l.Count > 0) ?? new List<ProductRecipeLineGraphDto>();

        for (var i = 0; i < secilenler.Count; i++)
        {
            var trial = secilenler[i];
            var eslesen = mevcutlar.FirstOrDefault(
                v => string.Equals(v.Code, trial.CombinationCode, StringComparison.OrdinalIgnoreCase));

            var variant = eslesen ?? new ProductVariantGraphDto { Code = trial.CombinationCode };
            variant.Code = trial.CombinationCode;
            variant.Name = trial.CombinationCode;
            // "Kombinasyon" kolonu nitelik özetinden beslenir; materyalize varyantın niteliği olmadığı için
            // orası boş kalıyordu → bileşim + toplam özeti yazılır:
            //   "1×5gr + 4×1gr = Toplam 5 parça, 10,02 gr"
            // Cümle BURADA kurulur (sunucuda değil): metin lokalize, sayılar hesaptan hazır gelir.
            variant.AttributeSummary = $"{trial.CombinationSummary} = " +
                L["Substitution:TotalSummary", trial.PieceCount, trial.TotalWeight.ToString("0.#####")].Value;
            variant.StockQuantity = trial.PackageCount;
            // Maliyet hesaptan HAZIR gelir (kombinasyonun toplam maliyeti) — reçeteden yeniden hesaplamaya
            // gerek yok, üstelik önizlemede reçete henüz kurulmuş değil.
            variant.NetCost = trial.TotalCost;
            variant.NetCostCurrency = SubstitutionResult.CostCurrencyCode;

            // Reçete satırları da hesapla birlikte geldi (sunucu üretti, kayıt anıyla aynı matematik) →
            // varyantın içi kaydetmeden dolu görünür. Boş gelirse (bağlam yüklenemedi) mevcut reçeteye
            // DOKUNULMAZ: kullanıcının kayıtlı satırlarını önizleme yüzünden silmek veri kaybı olurdu.
            if (trial.RecipeLines.Count > 0)
            {
                // Varyantın KENDİ şablon satırları varsa onlar korunur (kullanıcı düzenlemiş olabilir);
                // yoksa prototipin kopyası serilir. Şablon satırları kombinasyon satırlarının ARDINA gelir:
                // yüzde/brütleştirme kalemleri kendinden ÖNCEKİ satırların toplamına uygulanır, önde
                // kalsalardı taban eksik hesaplanırdı.
                // ŞABLON SOYUNUN TAMAMI korunur (dokunulmamış + kullanıcı düzenlemesi): yalnız Template'e
                // baksaydı düzenlenmiş satır yeni listeye HİÇ girmez, ekrandan kaybolur ama DB'de yaşamaya
                // devam ederdi (kaydetmede silme bayrağı da taşımaz) → kullanıcının göremediği ama fiyatlanan
                // hayalet satır + çakışan LineOrder (2026-08-20 inceleme bulgusu).
                var kendiSablonu = variant.RecipeLines
                    .Where(l => !l.IsDeleted
                        && (l.Origin == RecipeLineOrigin.Template || l.Origin == RecipeLineOrigin.TemplateEdited))
                    .ToList();
                var sablonSatirlari = kendiSablonu.Count > 0
                    ? kendiSablonu
                    : sablonPrototipi.Select(KlonlaSablonSatiri).ToList();

                var sira = 0;
                foreach (var satir in trial.RecipeLines)
                {
                    satir.LineOrder = sira++;
                    // Kaynak işareti ŞART: sunucu, muadil kayıtta Id'siz otomatik-kaynaklı satırları eler
                    // (sahibi materializer) — işaretsiz kalsa Manual sayılır ve reçetede İKİNCİ kez yazılırdı.
                    satir.Origin = RecipeLineOrigin.Substitution;
                }

                foreach (var satir in sablonSatirlari)
                {
                    satir.LineOrder = sira++;
                }

                variant.RecipeLines = trial.RecipeLines.Concat(sablonSatirlari).ToList();
            }
            variant.IsMain = i == 0;
            variant.IsActive = true;
            yeniListe.Add(variant);
        }

        Model.Variants = yeniListe;
    }

    /// <summary>Varyant kapsamı değişti — formu kirlet + kombinasyonları tazele.</summary>
    private async Task OnSubstitutionScopeChangedAsync()
    {
        EditChanged?.Invoke();
        await ScheduleSubstitutionRecalculationAsync();
    }

    /// <summary>Hedef miktar değişti — modele yaz, formu kirlet, kombinasyonları tazele.</summary>
    private async Task OnSubstitutionTargetQuantityChangedAsync(ProductGetDto model, decimal? value)
    {
        var previous = model.SubstitutionTargetQuantity;
        model.SubstitutionTargetQuantity = value;
        EditChanged?.Invoke();

        // Hedef miktar DEĞİŞTİ → görünür emtia kümesi de değişir (miktarı aşan madenler elenir/geri gelir).
        // Kapsam bu yüzden TAMAMEN SEÇİLİ kurulur: yeni giren bir maden seçilmemiş kalırsa kullanıcı onu
        // fark etmeden kombinasyon dışında bırakır. Daraltmayı kullanıcı sonra yapar (2026-07-28 Hakan).
        if (previous != value && value > 0m)
        {
            await SelectAllVariantScopeAsync();
        }

        await ScheduleSubstitutionRecalculationAsync();
    }

    /// <summary>Kapsam ağacındaki TÜM emtiaları seçer — ağaç henüz kurulmadıysa (grup ilk kez görünür oluyor)
    /// bir sonraki render'a ertelenir; referans o zaman dolar.</summary>
    private async Task SelectAllVariantScopeAsync()
    {
        if (_variantScopePanel is not null)
        {
            await _variantScopePanel.SelectAllAsync();
            return;
        }

        _selectAllScopePending = true;
    }

    // Ağaç henüz render edilmemişken gelen "hepsini seç" isteği — panel kurulunca uygulanır.
    private bool _selectAllScopePending;

    /// <summary>Bekleyen "hepsini seç" isteğini panel kurulur kurulmaz uygular. Hedef miktar 0'dan büyüğe
    /// çıktığında kapsam grubu O RENDER'DA görünür oluyor; panel referansı ancak ondan sonra dolduğu için
    /// istek erteleniyor.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (_selectAllScopePending && _variantScopePanel is not null)
        {
            _selectAllScopePending = false;
            await _variantScopePanel.SelectAllAsync();
            StateHasChanged();
        }

        // Satışa hazırlık panelinden gelen "bu varyantı düzelt" isteği: Varyantlar sekmesi az önce açıldı, panel bu render'da kuruldu —
        // şimdi satırın formu açılabilir. Varyant listede yoksa (bayat issue) sekmede kalınır; ikinci deneme YOK.
        if (_pendingVariantEditId is { } variantId && _variantsPanel is not null)
        {
            _pendingVariantEditId = null;
            _variantsPanel.EditVariant(variantId);
        }
    }

    // ── Ürünün satışa hazırlık paneli (ProductSaleReadinessPanel) bağlantıları ─────────────────────────────────────────

    /// <summary>Sekme indeksleri — satışa hazırlık paneli "Düzelt →" hedefini sekmeye çevirir. Sıra ProductLayout.razor'daki
    /// DxTabPage sırasıdır; Visible ile gizlenen sekme de sayılır (toplayıcıya kayıtlı kalır).</summary>
    private const int CockpitTabIndex = 0;
    private const int VariantsTabIndex = 2;
    private const int MediaTabIndex = 3;
    private const int SalesChannelsTabIndex = 7;

    // DrillTabs'e bağlı aktif sekme; satışa hazırlık paneli ilk sekme olduğu için form satışa hazırlık paneliyle açılır.
    private int _activeTabIndex = CockpitTabIndex;

    /// <summary>
    /// ISSUE ENDEKSİ — sekme başlıkları, varyant satırları ve reçete bölümü bunun üzerinden renklenir
    /// (<c>CascadingValue Name="SaleReadinessIndex"</c>).
    ///
    /// <para><b>Neden ALANDA tutulur:</b> endeksi üreten satışa hazırlık paneli bir SEKME içeriğidir; endeks panelin
    /// içinde yaşasaydı yalnız o sekme çizilirken erişilebilir olurdu. Burada tutulunca tüm sekmeler aynı
    /// referanstan okur ve panel her yüklendiğinde (form açılışı · kayıt sonrası taze model · "Yenile" · satışa hazırlık paneli
    /// aksiyonu) üzerine taze endeksi yazar.</para>
    ///
    /// <para><b>Tazeliğin ŞARTI — DrillTabs OnDemand:</b> panel ancak MOUNT kaldığı sürece parametre değişimini
    /// (taze <c>Model</c> = <c>ReloadToken</c>) görüp endeksi yeniler. DxTabs'in varsayılan kipinde sekme
    /// değişince içerik yıkıldığı için kullanıcı başka sekmede kaydettiğinde bu alan son (bayat) değerinde
    /// donardı. ProductLayout.razor'daki <c>RenderMode="TabsRenderMode.OnDemand"</c> bunun için vardır —
    /// kaldırılırsa işaretler sessizce bayatlar.</para>
    ///
    /// <para>Layout endeksi KENDİ ÇEKMEZ: aynı sunucu ucuna ikinci bir tur atmak hem gereksiz maliyet olurdu
    /// hem de iki kopya farklı anlarda tazelenip ayrışırdı.</para>
    /// </summary>
    private SaleReadinessIssueIndex _readinessIndex = SaleReadinessIssueIndex.Empty;

    private void HandleReadinessIndexChanged(SaleReadinessIssueIndex index)
    {
        _readinessIndex = index;
        StateHasChanged();
    }

    // ── Satışa-hazırlık: satışa hazırlık paneline geçiş ──────────────────────────────────────────────────────────────

    // NOT (2026-08-20): bandın metin/ton/görünürlük yardımcıları KALDIRILDI — bant artık kapsam-duyarlı ortak
    // bileşende (ReadinessNotice) yaşıyor ve her bileşen kendi kapsamını veriyor. Burada yalnız "satışa hazırlık paneline git"
    // metodu kaldı.
    private void OpenReadinessCockpit()
    {
        _activeTabIndex = CockpitTabIndex;
        StateHasChanged();
    }

    private EntityVariantsPanel<ProductVariantGraphDto>? _variantsPanel;

    // Sekme geçişi ile varyant formunun açılması AYNI render'da olamaz (sekme İLK kez açılıyorsa içeriği o
    // render'da çizilir; panel referansı ancak geçişten SONRA dolar) → istek ertelenir, OnAfterRenderAsync uygular.
    private Guid? _pendingVariantEditId;

    /// <summary>Formun efektif kirliliği — EntityEditForm cascade'i (Name="EditIsDirty"). Satışa hazırlık paneli "Satışa Doğrula"yı
    /// kirli formda kapatır. Cascade yoksa (layout tek başına çizildi — test) temiz sayılır.</summary>
    [CascadingParameter(Name = "EditIsDirty")] private Func<bool>? EditIsDirty { get; set; }

    /// <summary>"Satışa Doğrula" — host'un mevcut yolu (onay + VerifySaleReadinessAsync + rozet tazeleme). Layout dilsiz:
    /// servis çağırmaz, yalnız iletir. Bağlı değilse satışa hazırlık paneli düğmesi işlevsiz kalır ama gizlenmez (test edilebilirlik).</summary>
    [Parameter] public Func<Task>? OnVerifyForSale { get; set; }

    private bool IsFormDirty
    {
        get { return EditIsDirty?.Invoke() ?? false; }
    }

    private async Task HandleVerifyForSaleAsync()
    {
        if (OnVerifyForSale is not null)
        {
            await OnVerifyForSale();
        }
    }

    /// <summary>Satışa hazırlık paneli "Düzelt →" → sekme. <c>GeneralTab</c> için sekme YOK: genel alanlar sekmelerin ÜSTÜNDEKİ
    /// formdadır ve her zaman görünür. <c>VariantForm</c>: Varyantlar sekmesi + (id varsa) satır formu — panel
    /// referansı render sonrası dolduğundan form açma ertelenir; varyant bulunamazsa sekmeye düşülür.
    /// <c>ChannelProductForm</c>: kanal sekmesine düşülür — kanal satırının düzenleme formunu id ile açan bir API
    /// bugün yok (DrillList satırı ClientKey ile tutuyor); sekme açılır, satır kullanıcı tarafından tıklanır.</summary>
    private void HandleSaleReadinessNavigate(SaleReadinessNavigation navigation)
    {
        switch (navigation.Target)
        {
            case SaleReadinessFixTarget.VariantsTab:
                _activeTabIndex = VariantsTabIndex;
                break;
            case SaleReadinessFixTarget.VariantForm:
                _activeTabIndex = VariantsTabIndex;
                _pendingVariantEditId = navigation.TargetId;
                break;
            case SaleReadinessFixTarget.MediaTab:
                _activeTabIndex = MediaTabIndex;
                break;
            case SaleReadinessFixTarget.ChannelsTab:
            case SaleReadinessFixTarget.ChannelProductForm:
                _activeTabIndex = SalesChannelsTabIndex;
                break;
            case SaleReadinessFixTarget.GeneralTab:
            case SaleReadinessFixTarget.Verify:
            case SaleReadinessFixTarget.None:
            default:
                // Genel alanlar sekme dışı (her zaman görünür); Verify satışa hazırlık panelinin kendi düğmesidir; None hedefsizdir.
                break;
        }

        StateHasChanged();
    }

    /// <summary>
    /// Muadil girdisi değişince (kapsam ya da hedef miktar) kombinasyonlar KENDİLİĞİNDEN yeniden hesaplanır —
    /// kullanıcının "Hesapla"ya basması gerekmez; kombinasyonları etkileyen başka girdi yok
    /// (2026-07-27 Hakan kararı).
    /// <para><b>Debounce şart:</b> her tık bir sunucu turu demek; hızlı daraltmada ardışık isteklerin
    /// sonuncusu dışındakiler boşa gider ve sırasız dönerlerse ekranda bayat kombinasyon kalırdı.</para>
    /// </summary>
    private async Task ScheduleSubstitutionRecalculationAsync()
    {
        _substitutionRecalcCts?.Cancel();
        var cts = new CancellationTokenSource();
        _substitutionRecalcCts = cts;

        try
        {
            await Task.Delay(SubstitutionRecalcDelayMs, cts.Token);
            await OnCalculateSubstitution.InvokeAsync();
        }
        // Yeni değişiklik geldi — bu tur bilinçli düşürüldü.
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
    }

    /// <summary>"Reçeteye Uygula" — seçilen BAŞARILI kombinasyon host'ta ana varyant reçetesine çevrilir.</summary>
    [Parameter] public EventCallback<SubstitutionTrialDto> OnApplySubstitutionTrial { get; set; }

    // Kombinasyon grid'inin seçili satırı — salt UI durumu (uygula butonu başarılı satırla açılır).
    private SubstitutionTrialRow? _selectedTrialRow;

    // TxGrid seçim API'si ÇOĞUL (SelectedDataItems) — tekil seçim tek elemanlı liste olarak taşınır.
    private IReadOnlyList<object> _selectedTrialItems = Array.Empty<object>();

    // Satır önbelleği — satırlar YALNIZ sonuç değişince yeniden kurulur (her render'da yeni instance üretmek
    // grid seçim kimliğini kırardı; referans kıyası yeterli — host sonucu atomik değiştirir).
    private SubstitutionCalculationResultDto? _trialRowsSource;
    private List<SubstitutionTrialRow> _trialRows = new();

    // Önizleme yeni kuruldu → satır maliyetleri (ara toplam/taban) sunucudan tazelenmeli.
    private bool _previewCostsStale;

    // Grup kalemlerinin son istenen grup id'si — OnParametersSetAsync tetiklemesi yalnız DEĞİŞİMDE bir kez koşar
    // (mevcut kayıt Muadil modunda açıldığında devralınan-küme referansı host'tan yüklensin diye).
    private Guid? _requestedSubstitutionGroupId;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        // Deneme satırları RENDER'DAN ÖNCE burada kurulur (kod-inceleme bulgusu): eskiden bunu SubstitutionTrialRows
        // getter'ı yapıyordu ve markup'ta toolbar grid'den ÖNCE çizildiği için, yeni hesaptan sonraki render'da
        // "Reçeteye Uygula" bir ÖNCEKİ seçimden aktif boyanıp hemen ardından seçim sıfırlanıyordu → aktif ama işlevsiz
        // buton. Yan etkiyi yaşam döngüsüne almak markup sırasına bağımlılığı da ortadan kaldırır.
        RebuildTrialRowsIfResultChanged();
        RebuildSpecificationRowsIfChanged();

        // Önizleme varyantları yeni kurulduysa maliyetler SUNUCUDA yeniden hesaplanır (ara toplam + türetilmiş
        // satır tabanları). Kombinasyon satırları maliyetle hazır gelir ama ŞABLON satırları prototipten
        // klonlanıyor — klonun LineCost/AppliedBase'i ESKİ varyantın hesabından kalmadır; yeniden hesap
        // olmadan devralınan taban yanlış görünür ve yüzde/brütleştirme kalemleri o yanlış tabandan hesaplanmış
        // gibi okunurdu (2026-07-28 Hakan bulgusu).
        if (_previewCostsStale && OnRecipeChanged is not null)
        {
            _previewCostsStale = false;
            foreach (var variant in Model.Variants.Where(v => !v.IsDeleted && v.RecipeLines.Count > 0))
            {
                await OnRecipeChanged(variant);
            }
        }

        if (Model.VariantMode == ProductVariantMode.Substitution
            && Model.SubstitutionGroupId != _requestedSubstitutionGroupId
            && OnSubstitutionGroupChanged.HasDelegate)
        {
            _requestedSubstitutionGroupId = Model.SubstitutionGroupId;
            await OnSubstitutionGroupChanged.InvokeAsync(Model.SubstitutionGroupId);
        }

        // KAYITLI ürün açıldığında da özellik alanları gelsin: combo'dan geçmediği için OnProductCategoryChanged
        // tetiklenmez ve "Özellikler" sekmesi boş açılırdı (muadil grup kalemleriyle AYNI desen).
        if (Model.ProductCategoryId != _requestedSpecificationCategoryId && OnProductCategorySelected is not null)
        {
            _requestedSpecificationCategoryId = Model.ProductCategoryId;
            await OnProductCategorySelected(Model.ProductCategoryId);
        }
    }

    // Özellik niteliklerinin son istendiği kategori — aynı kategori için host'a tekrar tekrar sorulmasın.
    private Guid? _requestedSpecificationCategoryId;

    /// <summary>Sonuç referansı değiştiyse deneme satırlarını yeniden kurar + seçimi sıfırlar (bayat seçim yeni
    /// sonucun satırlarına işaret edemez). Sonuç aynıysa satır instance'ları korunur → grid seçim kimliği bozulmaz.</summary>
    private void RebuildTrialRowsIfResultChanged()
    {
        if (ReferenceEquals(_trialRowsSource, SubstitutionResult))
        {
            return;
        }

        _trialRowsSource = SubstitutionResult;
        _selectedTrialRow = null;
        _selectedTrialItems = Array.Empty<object>();

        if (SubstitutionResult is not { } result)
        {
            _trialRows = new List<SubstitutionTrialRow>();
            return;
        }

        var rows = new List<SubstitutionTrialRow>(result.Trials.Count);
        // Varyant adı yalnız ÇOK varyantlı madende ayırt edicidir (kombinasyon koduyla aynı ölçüt).
        var cokVaryantliMadenler = SubstitutionTrialFormat.MultiVariantMetalIds(result.Trials);

        for (var i = 0; i < result.Trials.Count; i++)
        {
            var trial = result.Trials[i];
            rows.Add(new SubstitutionTrialRow
            {
                Trial        = trial,
                TrialNo      = i + 1,
                Combination  = trial.CombinationSummary,
                Variants     = SubstitutionTrialFormat.VariantsText(trial, cokVaryantliMadenler),
                StatusText   = BuildTrialStatusText(trial),
            });
        }

        _trialRows = rows
            .OrderByDescending(r => r.Trial.Success)
            .ThenBy(r => r.Trial.Rank ?? int.MaxValue)
            .ThenBy(r => r.TrialNo)
            .ToList();

        // Yeni hesap geldi → varyant listesi de kaydetmeden tazelensin (kullanıcı Varyantlar sekmesinde
        // güncel kombinasyonları görsün; kayıtta sunucu aynı sonucu kalıcılaştırır).
        SyncSubstitutionVariantPreview();
        _previewCostsStale = true;
    }

    /// <summary>Muadil sekmesinin grid satırları — hesaplama sayfası BuildRows dizilimiyle birebir
    /// (başarılılar üstte Rank sırasıyla; TrialNo orijinal deneme numarasını korur). SAF getter: satırlar
    /// <see cref="RebuildTrialRowsIfResultChanged"/> tarafından render'dan ÖNCE kurulur (yan etki markup'ta değil).</summary>
    private List<SubstitutionTrialRow> SubstitutionTrialRows
    {
        get { return _trialRows; }
    }

    // Maliyet kolonu başlığı — para birimi çözüldüyse yanına eklenir (hesaplama sayfası deseni).
    private string SubstitutionCostCaption
    {
        get
        {
            return SubstitutionResult is { CostCurrencyCode.Length: > 0 } r
                ? $"{L["Substitution:Cost"]} ({r.CostCurrencyCode})"
                : L["Substitution:Cost"].Value;
        }
    }

    // Teknik başarısızlık nedeni → okunur metin (hesaplama sayfası BuildStatusText paritesi).
    private string BuildTrialStatusText(SubstitutionTrialDto trial)
    {
        if (trial.Success)
        {
            return L["Substitution:Success"];
        }

        var reason = trial.FailureReason ?? string.Empty;
        if (reason.StartsWith(SubstitutionReasonCodes.RemainderPrefix, StringComparison.Ordinal))
        {
            var raw = reason[SubstitutionReasonCodes.RemainderPrefix.Length..];
            var text = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var remainder)
                ? remainder.ToString("0.#####", CultureInfo.CurrentCulture)
                : raw;
            return L["Substitution:FailRemainder", text];
        }

        if (reason == SubstitutionReasonCodes.StockExhausted)
        {
            return L["Substitution:FailStockExhausted"];
        }

        return reason; // bilinmeyen yeni neden — ham teknik kod göster (sessiz yutma yok)
    }

    // Satır boyama: başarılı = hafif yeşil zemin; Rank 1 = ANA kombinasyon (hesaplama sayfası deseni; CSS dosyası yok).
    private void OnCustomizeTrialRow(GridCustomizeElementEventArgs e)
    {
        if (e.ElementType != GridElementType.DataRow)
        {
            return;
        }

        if (e.Grid.GetDataItem(e.VisibleIndex) is not SubstitutionTrialRow row || !row.Trial.Success)
        {
            return;
        }

        e.Style = row.Trial.Rank == 1
            ? "background-color: rgba(22,163,74,0.20); font-weight: 600;"
            : "background-color: rgba(22,163,74,0.08);";
    }

    private void OnSelectedTrialItemsChanged(IReadOnlyList<object> items)
    {
        _selectedTrialItems = items;
        _selectedTrialRow = items.FirstOrDefault() as SubstitutionTrialRow;
    }

    /// <summary>Varyant modu combo değişimi — isteği host'a iletir (onay + atama orada), sonra dirty + yeniden çizim
    /// (host reddettiyse combo Model'deki eski değere döner).</summary>
    private async Task HandleVariantModeChangedAsync(ProductVariantMode newMode)
    {
        if (OnVariantModeChangeRequested is not null)
        {
            await OnVariantModeChangeRequested(newMode);
        }

        // Muadil moduna GERÇEKTEN geçildiyse (host onayladıysa) sağlıklı varsayılanlar: hedef 0 (null değil),
        // tolerans Miktar (Amount; devral yok), ilk grup otomatik seçili + kalemleri yüklenir. Kullanıcı boş/null
        // alanla ya da seçilmemiş grupla karşılaşmasın.
        // DİKKAT (kod-inceleme düzeltmesi): tolerans TÜRÜ ve DEĞERİ birlikte dolar — Product.SetSubstitutionConfig
        // "ikisi de dolu ya da ikisi de boş" değişmezini fail-fast zorlar. Yalnız türü set etmek kaydı KIRIYORDU
        // (değer editörü sadece Binde'de göründüğü ve tür combosu temizlenemediği için UI'dan düzeltilemiyordu).
        // Amount + 0 = TAM EŞLEŞME (ToleranceType.Amount dokümanının tanımı) → geçerli ve anlamlı varsayılan.
        if (Model.VariantMode == ProductVariantMode.Substitution)
        {
            Model.SubstitutionTargetQuantity ??= 0m;
            Model.SubstitutionToleranceType  ??= ToleranceType.Amount;
            Model.SubstitutionToleranceValue ??= 0m;
            if (Model.SubstitutionGroupId is null && SubstitutionGroups.FirstOrDefault()?.Id is { } firstGroupId)
            {
                await HandleSubstitutionGroupChangedAsync(firstGroupId);
            }
        }

        EditChanged?.Invoke();
        StateHasChanged();
    }

    /// <summary>Tolerans türü değişti — Miktar (tam eşleşme) türünde tolerans değeri UI'da gizlendiğinden SIFIRA
    /// çekilir (Binde'den geçişte bayat değer ±miktar sapması gibi yorumlanmasın). Binde seçilince değer alanı
    /// UI'da tekrar görünür ve kullanıcı girer.
    /// <para>null DEĞİL 0: tür ve değer birlikte dolmalı (Product.SetSubstitutionConfig fail-fast'i); null'a çekmek
    /// kaydı kırıyordu. Amount+0 = tam eşleşme.</para></summary>
    private void OnToleranceTypeChanged(ToleranceType? toleranceType)
    {
        Model.SubstitutionToleranceType = toleranceType;
        if (toleranceType != ToleranceType.PerMille)
        {
            Model.SubstitutionToleranceValue = toleranceType is null ? null : 0m;
        }

        EditChanged?.Invoke();
    }

    /// <summary>Muadil grubu seçimi değişti — model güncellenir, bayat override temizlenir (grup değişince eski
    /// grubun varyant seçimi anlamsız), host kalemleri yeniden yükler.</summary>
    private async Task HandleSubstitutionGroupChangedAsync(Guid? groupId)
    {
        Model.SubstitutionGroupId = groupId;
        Model.SubstitutionOverrideVariantIds.Clear();
        _selectedTrialRow = null;
        _requestedSubstitutionGroupId = groupId;   // OnParametersSetAsync tetiklemesi aynı grubu İKİNCİ kez istemesin
        if (OnSubstitutionGroupChanged.HasDelegate)
        {
            await OnSubstitutionGroupChanged.InvokeAsync(groupId);
        }

        EditChanged?.Invoke();
    }

    /// <summary>Seçilen BAŞARILI kombinasyonu host'a iletir (ana varyant reçetesine uygulanır) + dirty.</summary>
    private async Task ApplySelectedTrialAsync()
    {
        if (_selectedTrialRow is not { Trial.Success: true } row)
        {
            return;
        }

        if (OnApplySubstitutionTrial.HasDelegate)
        {
            await OnApplySubstitutionTrial.InvokeAsync(row.Trial);
        }

        EditChanged?.Invoke();
    }

    /// <summary>Muadil sekmesi grid satırı — deneme DTO'sunun görüntü düzleştirmesi (hesaplama sayfası TrialRow
    /// deseni; DTO referansı uygula akışı için taşınır).</summary>
    private sealed class SubstitutionTrialRow
    {
        public required SubstitutionTrialDto Trial { get; init; }
        public int TrialNo { get; init; }
        public string Combination { get; init; } = string.Empty;
        public string Variants { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;

        // Grid FieldName bağlamaları — DTO'ya delege (blok gövde konvansiyonu).
        public decimal TotalWeight { get { return Trial.TotalWeight; } }
        public decimal Deviation { get { return Trial.Deviation; } }
        public decimal TotalCost { get { return Trial.TotalCost; } }
        public int PieceCount { get { return Trial.PieceCount; } }
        public int PackageCount { get { return Trial.PackageCount; } }
        public int? Rank { get { return Trial.Rank; } }
        public bool Success { get { return Trial.Success; } }
    }


    // Yeni eklenti satırı eklenince Sıra No OTOMATİK artar (max + 1; boşsa 1).
    private int NextAddOnOrder()
    {
        return Model.AddOns.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Eklenti satırının katalog adını çözer (grid gösterimi) — bulunamazsa boş.
    private string AddOnName(Guid addOnId)
    {
        return AddOnCatalog.FirstOrDefault(a => a.Id == addOnId)?.Name ?? string.Empty;
    }

    // Aynı eklentinin ürüne İKİ KEZ atanmasını engelle (aynı AddOnId'li başka satır varsa).
    private string? AddOnSaveGuard(ProductAddOnDto item)
    {
        if (item.AddOnId == Guid.Empty)
        {
            return L["Product:AddOnRequired"].Value;
        }

        var duplicate = Model.AddOns.Any(x => x.ClientKey != item.ClientKey && x.AddOnId == item.AddOnId);
        return duplicate ? L["Product:AddOnDuplicate"].Value : null;
    }

    /// <summary>Satış statüsü rozetinin rengi. <c>Ready</c> DIŞINDAKİ her durum push guard'ından geçemez —
    /// bu yüzden yalnız Ready yeşil, kalanlar dikkat çeken tonlarda: kullanıcı "satışa hazır değil"i bir
    /// bakışta görmeli, yoksa ürünün neden pazaryerinde olmadığını aramak zorunda kalır.</summary>
    private static string SaleStatusCssClass(ProductSaleStatus status)
    {
        return status switch
        {
            ProductSaleStatus.Ready => "text-success fw-bold",
            ProductSaleStatus.Suspended => "text-danger fw-bold",
            ProductSaleStatus.Closed => "text-muted fw-bold",
            _ => "text-warning fw-bold",
        };
    }
}
