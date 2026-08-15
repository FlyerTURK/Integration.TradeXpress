using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.VariantTemplates;
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
    [Inject] protected ISubstitutionGroupAppService SubstitutionGroupAppService { get; set; } = default!;
    [Inject] protected IVariantTemplateAppService VariantTemplateAppService { get; set; } = default!;
    [Inject] protected IProductCategoryAppService ProductCategoryAppService { get; set; } = default!;
    [Inject] protected IRecipeTemplateAppService RecipeTemplateAppService { get; set; } = default!;
    [Inject] protected ISubstitutionCalculationAppService SubstitutionCalculationAppService { get; set; } = default!;
    [Inject] protected ICompanyAppService CompanyAppService { get; set; } = default!;
    [Inject] protected ICountryAppService CountryAppService { get; set; } = default!;
    [Inject] protected IViewOpener ViewOpener { get; set; } = default!;
    [Inject] protected IWorkingContextService Working { get; set; } = default!;
    [Inject] protected IServiceProvider ServiceProvider { get; set; } = default!;

    private ICommitCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto>? _coordinator;
    private bool _ready;
    private bool _verifyBusy;

    /// <summary>"Satışa Doğrula" — push kapısını açan İNSAN yolu.
    ///
    /// <para>Kapı fail-closed çalışıyordu ama açacak yol yoktu: canlıda 165/165 varyant <c>Draft</c>'tı ve
    /// hiçbir ürün pazaryerine çıkamıyordu. Kullanıcı hatayı göremiyordu bile — push "aday bulunamadı" diyordu,
    /// sebebini hiçbir ekran söylemiyordu.</para>
    ///
    /// <para>YENİ kayıtta gizlidir: doğrulanacak varyant ancak kaydedildikten sonra vardır. SortIndex 150 —
    /// Delete(100) ile Previous(700) arası, sipariş aksiyonlarıyla aynı slot felsefesi.</para></summary>
    private IReadOnlyList<CrudToolbarAction> BuildProductActions(ProductGetDto model)
    {
        if (model.Id == Guid.Empty)
        {
            return Array.Empty<CrudToolbarAction>();
        }

        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 150,
                Text = L["Product:VerifyForSale"],
                Tooltip = L["Product:VerifyForSale"],
                IconCssClass = TradeXpressIcons.CheckCircle + " xaf-toolbar-item-icon",
                Visible = true,
                Enabled = !_verifyBusy,
                OnClick = () => VerifyForSaleClickedAsync(model),
            },
        };
    }

    private async Task VerifyForSaleClickedAsync(ProductGetDto model)
    {
        if (_verifyBusy)
        {
            return;
        }

        // Onay diyaloğu METNİ önemli: kullanıcı "bir kereye mahsus mühür" sanmasın — onay o ANDAKİ reçeteye
        // verilir ve reçete değişince kendiliğinden düşer.
        var confirmed = await UiService.ConfirmAsync(
            L["Product:VerifyForSaleConfirm"].Value,
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value,
            showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        _verifyBusy = true;
        try
        {
            var result = await ProductAppService.VerifySaleReadinessAsync(
                new ProductSaleVerifyInputDto { ProductId = model.Id });

            // Atlananlar SESSİZ geçilmez — kullanıcı "hepsi açıldı" sanıp push'un neden hâlâ boş döndüğünü
            // arayamamalı.
            foreach (var issue in result.Issues)
            {
                UiService.ShowErrorToast(issue);
            }

            UiService.ShowSuccessToast(
                string.Format(L["Product:VerifyForSaleDone"].Value, result.VerifiedVariants));

            // Rozetleri tazele — ama FORMU YENİDEN YÜKLEMEDEN. Tam reload, kullanıcının kaydetmediği
            // düzenlemelerini sessizce çöpe atardı; burada yalnız SALT-OKUNUR statü alanları kopyalanır.
            await RefreshVariantSaleStatusAsync(model);
        }
        finally
        {
            _verifyBusy = false;
        }
    }

    /// <summary>Varyant statü rozetlerini sunucudan tazeler — YALNIZ salt-okunur alanlar.
    /// <para>Kullanıcının açık formdaki kaydedilmemiş değişiklikleri (fiyat, reçete, medya) KORUNUR: statü
    /// zaten form tarafından yazılamayan bir projeksiyondur, dolayısıyla üzerine yazmak veri kaybı üretmez.</para></summary>
    private async Task RefreshVariantSaleStatusAsync(ProductGetDto model)
    {
        var fresh = await ProductAppService.GetAsync(model.Id);

        foreach (var variant in model.Variants)
        {
            var updated = fresh.Variants.FirstOrDefault(x => x.Id == variant.Id);
            if (updated is { })
            {
                variant.SaleStatus = updated.SaleStatus;
                variant.VerifiedAt = updated.VerifiedAt;
            }
        }

        StateHasChanged();
    }

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


    // ── Muadil (Substitution) modu durumu (Dilim-3) — grup lookup'u + seçili grubun kalemleri (override
    //    ağacının devralınan-küme referansı) + son hesap sonucu. Layout DUMB; iş burada. ──
    /// <summary>Ülke katalogu — menşei seçimi (yeni üründe şirketin ülkesi varsayılan).</summary>
    protected IReadOnlyList<CountryListDto> Countries { get; private set; } = Array.Empty<CountryListDto>();

    protected IReadOnlyList<SubstitutionGroupListDto> SubstitutionGroups { get; private set; } = Array.Empty<SubstitutionGroupListDto>();

    /// <summary>Varyant şablonu katalogu — "Katalogdan Uygula" modunun combo verisi.</summary>
    protected IReadOnlyList<VariantTemplateListDto> VariantTemplates { get; private set; } = Array.Empty<VariantTemplateListDto>();

    /// <summary>Çekirdek kategori katalogu (yol sıralı) — ürünün kanal kategorisi ve komisyonu bu bağdan çözülür.</summary>
    protected IReadOnlyList<ProductCategoryListDto> ProductCategories { get; private set; } = Array.Empty<ProductCategoryListDto>();

    /// <summary>Seçili kategorinin SPESİFİKASYON nitelikleri (kalıtım çözülmüş) — ürün formundaki "Özellikler"
    /// sekmesinin sürücüsü. Kategori DEĞİŞTİĞİNDE tazelenir; kategori yoksa boştur.</summary>
    protected IReadOnlyList<ProductCategoryEffectiveAttributeDto> CategorySpecificationAttributes { get; private set; }
        = Array.Empty<ProductCategoryEffectiveAttributeDto>();

    /// <summary>Reçete şablonu ("orta reçete") katalogu — ürüne uygulanacak hizmet/yarı mamul demetleri.</summary>
    protected IReadOnlyList<RecipeTemplateListDto> RecipeTemplates { get; private set; } = Array.Empty<RecipeTemplateListDto>();
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

    /// <summary>
    /// "ÜRÜNDEN EMTİA YARAT" — reçete panelindeki anahtarın gövdesi (2026-08-11 Hakan tasarımı).
    ///
    /// <para><b>Neden host'ta:</b> katalogların sahibi burasıdır. Yeni kayıt açıldıktan sonra lookup'lar
    /// tazelenmezse panel yeni emtiayı GÖREMEZ ve satır boş seçimle açılırdı. Panel bilinçli olarak
    /// dilsizdir; I/O buraya delege edilir.</para>
    ///
    /// <para><b>Sessiz yaratım YOK — form açılır.</b> Maden/Hurda/Vadeli ailelerinde takip birimi
    /// (<c>FollowingUnitId</c>) ZORUNLUDUR ve bir iş kararıdır; uydurulamaz. Mamül tam projeksiyonla,
    /// diğerleri kod/ad ile tohumlanır, kalanını kullanıcı doldurur ("sınıflandırma manueldir, yazılım
    /// tahmin etmez"). Aile → form eşlemesi <see cref="ProductCommoditySeed"/>'de, sihirbazın sınıflandırma
    /// paneliyle ORTAK.</para>
    ///
    /// <para><b>Yeni kayıt FARKLA bulunur</b> (önce/sonra kimlik kümesi): app service "az önce ne yarattın"
    /// diye sorulabilecek bir uç sunmuyor ve kullanıcı formu kaydetmeden kapatmış da olabilir — fark, her
    /// iki durumu da doğru cevaplar (vazgeçince boş küme → <c>null</c>).</para>
    /// </summary>
    private async Task<Guid?> CreateCommodityFromProductAsync(ProcessType family)
    {
        if (Id is not { } productId || productId == Guid.Empty)
        {
            return null;
        }

        if (ProductCommoditySeed.EditComponentOf(family) is not { } editComponent)
        {
            return null;
        }

        var before = CommodityIdsOf(family);

        // Kod/ad SUNUCUDAN okunur: host, açık formun modeline erişmiyor (model razor context'inde yaşıyor)
        // ve kullanıcının kaydedilmemiş kod değişikliğini tohuma taşımak zaten YANLIŞ olurdu — emtia,
        // kayıtlı ürünün kimliğinden doğar.
        var product = await ProductAppService.GetAsync(productId);

        var extra = await ProductCommoditySeed.BuildExtraParamsAsync(
            family, productId, product.Code, product.Name, ProductAppService);

        await ViewOpener.OpenAsync(
            editComponent, null, L[$"Enum:ProcessType:{family}"].Value, iconCssClass: null, extraParams: extra);

        await LoadRecipeCatalogsAsync();

        var created = CommodityIdsOf(family).Except(before).ToList();
        return created.Count == 1 ? created[0] : null;
    }

    /// <summary>Ailenin şu anki katalog kimlikleri — yaratım öncesi/sonrası farkı bununla alınır.</summary>
    private HashSet<Guid> CommodityIdsOf(ProcessType family)
    {
        return family switch
        {
            ProcessType.Metal   => Metals.Select(x => x.Id).ToHashSet(),
            ProcessType.Scrap   => Scraps.Select(x => x.Id).ToHashSet(),
            ProcessType.Future  => Futures.Select(x => x.Id).ToHashSet(),
            ProcessType.Jewelry => Jewelries.Select(x => x.Id).ToHashSet(),
            ProcessType.Stone   => Stones.Select(x => x.Id).ToHashSet(),
            ProcessType.Good    => Goods.Select(x => x.Id).ToHashSet(),
            _                   => new HashSet<Guid>(),
        };
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
        SubstitutionGroups = await LoadActiveSubstitutionGroupsAsync();
        Countries = (await CountryAppService.GetListAsync(
            new CountryListRequestDto { MaxResultCount = 500 })).Items;
        VariantTemplates = await VariantTemplateAppService.GetPickerListAsync();
        ProductCategories = await ProductCategoryAppService.GetPickerListAsync();
        RecipeTemplates = await RecipeTemplateAppService.GetPickerListAsync();
    }

    // Yalnız AKTİF gruplar seçilebilir (pasif grup sunucuda da fail-fast — hesaplama sayfası deseni).
    private async Task<IReadOnlyList<SubstitutionGroupListDto>> LoadActiveSubstitutionGroupsAsync()
    {
        var result = await SubstitutionGroupAppService.GetListAsync(
            new SubstitutionGroupListRequestDto { IsActive = true, MaxResultCount = 200 });
        return result.Items.ToList();
    }

    // Inline şablon ekle/düzelt sonrası katalog listesini tazeler (yeni şablon anında combo'ya düşsün).
    private async Task ReloadVariantTemplatesAsync()
    {
        VariantTemplates = await VariantTemplateAppService.GetPickerListAsync();
        StateHasChanged();
    }

    // Inline kategori ekle/düzelt sonrası katalog listesini tazeler (yeni kategori anında combo'ya düşsün).
    /// <summary>
    /// Kategori seçicisinden YENİ kategori — katalog formu popup'ta açılır, kapanınca liste tazelenir ve
    /// ÖNCE/SONRA farkından yeni kaydın id'si bulunup seçiciye döndürülür (lookup ekle/düzelt standardı).
    /// </summary>
    private async Task<Guid?> AddProductCategoryAsync()
    {
        var before = ProductCategories.Select(c => c.Id).ToHashSet();

        await ViewOpener.OpenAsync(typeof(ProductCategoryEditHost), null, L["ProductCategory"].Value,
            TradeXpressIcons.ProductCategory);

        await ReloadProductCategoriesAsync();
        return ProductCategories.FirstOrDefault(c => !before.Contains(c.Id))?.Id;
    }

    /// <summary>Seçili kategoriyi düzenle — popup kapanınca liste tazelenir (ad/yol değişmiş olabilir).</summary>
    private async Task EditProductCategoryAsync(Guid categoryId)
    {
        await ViewOpener.OpenAsync(typeof(ProductCategoryEditHost), categoryId, L["ProductCategory"].Value,
            TradeXpressIcons.ProductCategory);

        await ReloadProductCategoriesAsync();
    }

    private async Task ReloadProductCategoriesAsync()
    {
        ProductCategories = await ProductCategoryAppService.GetPickerListAsync();
        StateHasChanged();
    }

    /// <summary>
    /// Kategorinin SPESİFİKASYON niteliklerini yükler — "Özellikler" sekmesi hangi alanları soracağını buradan
    /// bilir. Kategori değiştiğinde yeniden çağrılır.
    ///
    /// <para>VARYANT ekseni nitelikleri DIŞLANIR: onlar kartezyene girip ayrı varyantlar doğurur, değerleri
    /// varyantın kendisinde yaşar. Aynı listede gösterilseydi kullanıcı "Renk" alanına ürün düzeyinde tek bir
    /// değer yazmaya çalışır, o değer de hiçbir yere gitmezdi.</para>
    /// </summary>
    private async Task LoadCategorySpecificationAttributesAsync(Guid? categoryId)
    {
        if (categoryId is not { } id || id == Guid.Empty)
        {
            CategorySpecificationAttributes = Array.Empty<ProductCategoryEffectiveAttributeDto>();
            return;
        }

        var effective = await ProductCategoryAppService.GetEffectiveAttributesAsync(id);
        CategorySpecificationAttributes = effective
            .Where(a => a.Kind == ProductCategoryAttributeKind.Specification)
            .ToList();
    }

    /// <summary>Ürün formunda kategori değişti — özellik alanları ANINDA tazelenir (kaydetmeyi beklemeden).</summary>
    private async Task OnProductCategoryChangedAsync(Guid? categoryId)
    {
        await LoadCategorySpecificationAttributesAsync(categoryId);
        StateHasChanged();
    }

    // Inline reçete şablonu ekle/düzelt sonrası katalog listesini tazeler.
    private async Task ReloadRecipeTemplatesAsync()
    {
        RecipeTemplates = await RecipeTemplateAppService.GetPickerListAsync();
        StateHasChanged();
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

        var losesVariants = model.VariantMode is ProductVariantMode.MultiVariant or ProductVariantMode.FromCatalog
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

        // ŞABLONDAN gelen satırlar (paketleme/kargo/sigorta) KORUNUR — yalnız kombinasyonun kendi ürettiği
        // satırlar tazelenir. Hepsini silmek, muadil hedefi her değiştiğinde ara masrafları sessizce düşürüyordu:
        // kombinasyon yeniden kuruluyor, şablon satırları geri gelmiyor ve fiyat eksik çıkıyordu — kullanıcının
        // "şablon etki etmiyor" dediği durum tam olarak buydu (2026-07-28 Hakan).
        var replaceable = variant.RecipeLines
            .Where(l => !l.IsDeleted && l.Origin != RecipeLineOrigin.Template)
            .ToList();
        foreach (var line in replaceable)
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

        // Korunan şablon satırları kombinasyon satırlarının ARDINDA sıralanır: ara masraflar (yüzde/brütleştirme
        // dahil) kendinden ÖNCEKİ satırların toplamına uygulanır — önde kalsalardı taban eksik hesaplanırdı.
        foreach (var templateLine in variant.RecipeLines
                     .Where(l => !l.IsDeleted && l.Origin == RecipeLineOrigin.Template)
                     .OrderBy(l => l.LineOrder)
                     .ToList())
        {
            templateLine.LineOrder = order++;
        }

        // Kombinasyon serildikten SONRA ürünün kayıtlı reçete şablonu da uygulanır — "Uygula" butonunun
        // çağırdığı metodun ta kendisi (2026-07-28 Hakan: "önce varyantlar oluşturulsun, sonra uygula
        // butonunun çağırdığı metot da çalışsın").
        //
        // <para>Neden ELLE uygulamak yetmiyordu: muadil hedefi her değiştiğinde kombinasyon reçeteyi baştan
        // kuruyor; ara masraf satırlarını (paketleme/kargo/sigorta) kullanıcının her seferinde yeniden
        // uygulaması gerekiyordu ve unutulduğunda fiyat sessizce eksik çıkıyordu.</para>
        //
        // <para>Şablon bağlı değilse ya da ürün henüz kaydedilmemişse metot kendi içinde erken çıkar
        // (sunucuya yazan bir işlem; kaydedilmemiş üründe uygulanacak varyant yok).</para>
        if (model.RecipeTemplateId is { } recipeTemplateId && recipeTemplateId != Guid.Empty)
        {
            await ApplyRecipeTemplateAsync(model, recipeTemplateId);
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
            // Kaynak işareti: muadil kayıtta Id'siz otomatik-kaynaklı satırları sunucu eler (sahibi materializer).
            Origin               = RecipeLineOrigin.Substitution,
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

    // Yeni kayıt: aktif + gridde görünsün diye base ANA VARYANT satırı seed'lenir (SABİT kimlik ANAVARYANT/Ana
    // Varyant; ProductConsts SSOT). Bu satır kaydedince sunucunun ProductVariantManager ile yarattığı DB main'e
    // eşlenir (AppService ResolveTargetVariant: Id yok + IsMain + kombinasyon yok → DB main) → Yeni'de girilen
    // reçete ana varyanta yazılır. Attribute'lu üretimde bu satır synchronizer tarafından kombinasyonlarla değişir.
    /// <summary>MAMÜLÜN ÜRÜN AYNASI — <c>GoodToProductProjector</c> çıktısı (2026-08-10). Kod/ad/KDV'nin
    /// yanında NİTELİK ve VARYANT grafını, kayıt-geneli ve varyant medyasını da taşır; kullanıcı aynı bilgiyi
    /// ikinci kez girmez. <c>GoodEditHost.SeedModel</c>'in birebir simetriği.
    /// <para><b>Fiyat taşımaz</b> — mamülde fiyat varyantta yaşar, üründe reçeteden türetilir (gerekçe
    /// projektörün özetinde).</para></summary>
    [Parameter] public ProductGetDto? SeedModel { get; set; }

    private void ApplyNew(ProductGetDto m)
    {
        m.IsActive = true;

        // ZENGİN TOHUM önce: mamülün ürün aynası varsa kimlik + KDV + nitelik + varyant + medya olduğu gibi
        // gelir. Bu dalda stok ana varyant EKLENMEZ — projeksiyon ana varyantı zaten üretiyor (varyantsız
        // mamülde bile, kaydın koduyla) ve ikincisini eklemek çift ana varyant demekti.
        if (SeedModel is { } s)
        {
            m.Code        = s.Code;
            m.Name        = s.Name;
            m.Description = s.Description;
            m.VatRate     = s.VatRate;
            m.Attributes  = s.Attributes;
            m.Media       = s.Media;

            foreach (var v in s.Variants)
            {
                m.Variants.Add(v);
            }

            _ = InvokeAsync(() => ApplyCompanyDefaultsAsync(m));
            return;
        }

        m.Variants.Add(new ProductVariantGraphDto
        {
            IsMain = true,
            IsActive = true,
            Code = ProductConsts.MainVariantCode,
            Name = ProductConsts.MainVariantName,
        });

        // Şirket-türevli varsayılanlar ASENKRON (para birimi şirket kaydından okunur) — ApplyNewDefaults
        // senkron bir kanca olduğundan iş arka planda başlatılır ve bittiğinde form tazelenir. Kullanıcı
        // o ana kadar alanı zaten değiştirmişse DOKUNULMAZ (aşağıdaki ??= kontrolleri).
        _ = InvokeAsync(() => ApplyCompanyDefaultsAsync(m));
    }

    /// <summary>
    /// Yeni ürünün ŞİRKETE bağlı varsayılanları: menşei ülke + para birimi.
    ///
    /// <para><b>Para birimi YEREL birimdir, bilanço birimi DEĞİL</b> (2026-07-28 Hakan düzeltmesi): ürün
    /// fiyatı satışın yapıldığı ülkenin parasıyla girilir; bilanço birimi değerleme/raporlama tarafına aittir
    /// ve ikisi farklı olabilir (bkz. financials kuralı: "kur görüntüsü YERELE, pozisyon BİLANÇOYA"). Yerel
    /// birim şirketin ÜLKESİNDEN çözülür (<c>Country.DefaultCurrencyUnitId</c>).</para>
    ///
    /// <para>Ülke ya da ülkenin birimi tanımsızsa alan BOŞ bırakılır — bilanço birimine düşmek, yanlış birimi
    /// doğruymuş gibi göstermek olurdu; boş alan kullanıcıyı seçim yapmaya çağırır.</para>
    ///
    /// <para>Okuma başarısız olursa SESSİZ geçilir: varsayılan bir kolaylıktır, yeni ürün açmayı engellememeli.</para>
    /// </summary>
    private async Task ApplyCompanyDefaultsAsync(ProductGetDto m)
    {
        if (Working.CurrentCompanyId is not { } companyId || companyId == Guid.Empty)
        {
            return;
        }

        try
        {
            var company = await CompanyAppService.GetAsync(companyId);
            m.OriginCountryId ??= company.CountryId;

            if (m.CurrencyUnitId is null && company.CountryId is { } countryId)
            {
                m.CurrencyUnitId = Countries.FirstOrDefault(c => c.Id == countryId)?.DefaultCurrencyUnitId;
            }
            StateHasChanged();
        }
        catch
        {
            // varsayılan gelmedi — kullanıcı elle seçer
        }
    }

    // "Varyantları Oluştur" — layout DUMB kalır (servis inject etmez), çağrıyı host yapar. PERSISTSİZ önizleme:
    // sunucu nitelik grafından kartezyeni hesaplar, dönen graf Model.Variants'a yazılır (kalıcılaşma Save'de).
    private async Task GenerateVariantsAsync(ProductGetDto model)
    {
        // Mod kapısı (Dilim-3): SingleVariant/Muadil'de nitelik-tabanlı üretim BYPASS — host guard (buton zaten
        // görünmez; savunma). Sunucu kapısı AYRICA ŞART (client güven sınırı değildir — SaveVariantGraphAsync).
        if (model.VariantMode is not (ProductVariantMode.MultiVariant or ProductVariantMode.FromCatalog))
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

    /// <summary>
    /// Katalogdan şablon uygular: şablonun grupları/değerleri ürünün nitelik grafına KATILIR (mevcutlar korunur,
    /// tekrarlar ayıklanır — katma kuralı nitelik popup'ıyla ORTAK: <c>VariantTemplateMerger</c>), ardından
    /// varyantlar yeniden üretilir. Şablonla ürün arasında kalıcı bağ KURULMAZ; sonradan şablon değişirse ürüne
    /// yansımaz (2026-07-27 kararı — şablon bir başlangıç kaynağıdır).
    /// </summary>
    /// <summary>
    /// Reçete şablonunu ürüne uygular: sunucu, şablonun satırlarını ürünün TÜM varyantlarına — muadillikten
    /// gelen emtiaların ve kullanıcının kendi satırlarının ARDINA — serer. Uygulama SUNUCUDA kalıcıdır (satırlar
    /// doğrudan yazılır), bu yüzden formdaki reçete grafı yeniden yüklenir; aksi hâlde ekran bayat kalırdı.
    /// </summary>
    /// <summary>
    /// Kaydetme SONRASI sunucunun TÜRETTİĞİ grafı forma geri okur — muadil modunda varyant kümesi ve reçete
    /// satırları sunucuda yeniden üretilir (hedef miktar değişince kombinasyonlar baştan doğar, yenilerine
    /// reçete şablonu satırları serilir).
    ///
    /// <para><b>Neden gerekli:</b> bu üretim KAYDETME sırasında sunucuda oluyor; form kendi gönderdiği grafı
    /// tutmaya devam ettiğinden ekran bayat kalıyordu. Kullanıcı muadil miktarını değiştirip kaydediyor, yeni
    /// kombinasyonlar ve şablon satırları veritabanına yazılıyor ama formda hiçbir şey değişmiyordu — "şablon
    /// etki etmedi" izlenimi tam olarak buydu (2026-07-28 Hakan).</para>
    ///
    /// <para>Tazeleme yalnız MUADİL modunda: diğer modlarda sunucu varyant kümesini kendi başına değiştirmez,
    /// gereksiz bir okuma olurdu.</para>
    ///
    /// <para>Kör atama YERİNE <c>VariantGraphMerge</c>: kaydetmeden hemen sonra bile formda kalmış olabilecek
    /// düzenlemeleri korur — şablon uygulama akışıyla aynı desen.</para>
    /// </summary>
    private async Task RefreshDerivedGraphAsync(ProductGetDto model)
    {
        if (model.VariantMode != ProductVariantMode.Substitution || model.Id == Guid.Empty)
        {
            return;
        }

        var refreshed = await ProductAppService.GetAsync(model.Id);
        VariantGraphMerge.Apply(model.Variants, refreshed.Variants);
        StateHasChanged();
    }

    private async Task ApplyRecipeTemplateAsync(ProductGetDto model, Guid templateId)
    {
        if (model.Id == Guid.Empty)
        {
            return;   // kaydedilmemiş üründe varyant yok → uygulanacak yer de yok (UI da göstermiyor)
        }

        try
        {
            var result = await RecipeTemplateAppService.ApplyToProductAsync(templateId, model.Id);

            // Sunucu satırları KALICI yazdı → formdaki graf bayat kaldı, tazelenmeli. TAM EZME yerine
            // VariantGraphMerge: kullanıcının kaydetmediği düzenlemeleri (fiyat/barkod/uzantı) korur, yalnız
            // sunucudan geleni birleştirir — varyant şablonu akışıyla AYNI desen. Kör atama, formda yapılmış
            // ama kaydedilmemiş her şeyi uyarısız silerdi.
            var refreshed = await ProductAppService.GetAsync(model.Id);
            VariantGraphMerge.Apply(model.Variants, refreshed.Variants);

            UiService.ShowSuccessToast(L["RecipeTemplate:Applied",
                result.AffectedVariantCount, result.AppliedLineCount].Value);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir.
            // Yakalanmazsa circuit düşer ve kullanıcı formdaki tüm düzenlemelerini kaybederdi.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }

        StateHasChanged();
    }

    private async Task ApplyVariantTemplateAsync(ProductGetDto model, Guid templateId)
    {
        var template = await VariantTemplateAppService.GetAsync(templateId);
        VariantTemplateMerger.Merge(model.Attributes, template);

        await GenerateVariantsAsync(model);
        StateHasChanged();
    }
}
