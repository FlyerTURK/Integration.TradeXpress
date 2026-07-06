using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
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
    [Inject] protected IStoneAppService StoneAppService { get; set; } = default!;
    [Inject] protected IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] protected IEffectivePriceAppService EffectivePriceAppService { get; set; } = default!;

    private ICommitCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto>? _coordinator;
    private bool _ready;

    // Reçete katalog lookup verisi — açılışta bir kez yüklenir (varyant reçete drill'lerinin ortak beslemesi).
    protected IReadOnlyList<MetalListDto> Metals { get; private set; } = Array.Empty<MetalListDto>();
    protected IReadOnlyList<ScrapListDto> Scraps { get; private set; } = Array.Empty<ScrapListDto>();
    protected IReadOnlyList<FutureListDto> Futures { get; private set; } = Array.Empty<FutureListDto>();
    protected IReadOnlyList<JewelryListDto> Jewelries { get; private set; } = Array.Empty<JewelryListDto>();
    protected IReadOnlyList<StoneListDto> Stones { get; private set; } = Array.Empty<StoneListDto>();
    protected IReadOnlyList<ServiceListDto> Services { get; private set; } = Array.Empty<ServiceListDto>();
    protected IReadOnlyList<CurrentPriceDto> Units { get; private set; } = Array.Empty<CurrentPriceDto>();

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
        Scraps = await ScrapAppService.GetPickerListAsync();
        Futures = await FutureAppService.GetPickerListAsync();
        Jewelries = await JewelryAppService.GetPickerListAsync();
        Stones = await StoneAppService.GetPickerListAsync();
        Services = await ServiceAppService.GetPickerListAsync();
        Units = await EffectivePriceAppService.GetCurrentPricesAsync();
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
        try
        {
            var generated = await ProductAppService.GenerateVariantsAsync(new ProductVariantGenerateRequestDto
            {
                ProductName = model.Name,
                Attributes = model.Attributes,
            });

            model.Variants.Clear();
            model.Variants.AddRange(generated);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir
            // (ör. TradeXpress:ProductAttribute:ValueRequired); anahtar yoksa kodun kendisi görünür.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }
    }
}
