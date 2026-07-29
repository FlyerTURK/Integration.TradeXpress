using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;

/// <summary>
/// ProductCategory edit host code-behind — coordinator kurulumu + üst kategori seçeneklerinin yüklenmesi.
/// Kategori grafı (kendi nitelikleri) dumb layout'ta iç içe DrillList ile düzenlenir.
/// </summary>
public partial class ProductCategoryEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }

    /// <summary>Yeni kayıtta ön-seçili üst kategori (bir kategorinin altına "yeni alt kategori" açarken).</summary>
    [Parameter] [SupplyParameterFromQuery] public Guid? ParentId { get; set; }

    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IProductCategoryAppService ProductCategoryAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;
    [Inject] protected IN11CategoryAppService N11CategoryAppService { get; set; } = default!;
    [Inject] protected ITrendyolCategoryAppService TrendyolCategoryAppService { get; set; } = default!;
    [Inject] protected IEtsyTaxonomyAppService EtsyTaxonomyAppService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IServiceProvider ServiceProvider { get; set; } = default!;

    private ICommitCoordinator<ProductCategoryGetDto, ProductCategoryListDto, Guid, ProductCategoryListRequestDto>? _coordinator;
    private List<ProductCategoryListDto> _categories = new();
    private List<ProductCategoryChannelMappingDto> _channelMappings = new();
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<ProductCategoryGetDto, ProductCategoryListDto, Guid, ProductCategoryListRequestDto, ProductCategoryCreateDto, ProductCategoryUpdateDto>(
            ProductCategoryAppService, Mapper);

        // Üst kategori seçenekleri — kategorinin KENDİSİ ve TÜM ALT AĞACI sunucuda düşülür (kendi torununu
        // üst seçmek döngü kurardı). Dışlamayı sunucu yapar: alt ağacı görmek pasif düğümler dahil TAM ağacı
        // gerektirir, picker ise yalnız aktifleri döndürür (gerekçe: GetParentOptionsAsync).
        _categories = await ProductCategoryAppService.GetParentOptionsAsync(Id);

        await LoadChannelMappingsAsync();

        _ready = true;
    }

    /// <summary>Kategorinin KENDİ kanal eşleştirmeleri — devralınanlar burada gösterilmez (onlar sahibinde
    /// düzenlenir; kalıtımın sonucu ürün tarafında zaten çözülür).</summary>
    private async Task LoadChannelMappingsAsync()
    {
        if (Id is not { } id)
        {
            _channelMappings = new List<ProductCategoryChannelMappingDto>();
            return;
        }

        _channelMappings = await ProductCategoryAppService.GetChannelMappingsAsync(id);
    }

    /// <summary>
    /// Üst kategori seçilir seçilmez (KAYDETMEDEN) kalıtımı tazeler: sunucu, seçilen üstün ve onun tüm
    /// atalarının niteliklerini formdaki kendi nitelikleriyle birleştirip döner. Birleştirme sunucuda yapılır
    /// ki önizleme ile kayıt sonucu ayrışmasın.
    /// </summary>
    private async Task RefreshInheritedAttributesAsync(ProductCategoryGetDto model, Guid? parentId)
    {
        // Yalnız KENDİ satırları gönderilir: devralınanlar zaten sunucunun ağaçtan çözeceği şeydir; geri
        // göndermek eski üstün niteliklerini yeni üste taşımak olurdu.
        model.Attributes = await ProductCategoryAppService.PreviewInheritanceAsync(
            new ProductCategoryInheritancePreviewDto
            {
                ParentId = parentId,
                OwnAttributes = model.Attributes.Where(a => !a.IsInherited).ToList(),
            });

        StateHasChanged();
    }

    /// <summary>Bir kanal eşleştirmesini kaydeder (kanal başına TEK satır — aynı kanal yeniden kaydedilirse
    /// üzerine yazılır). Sunucu çözülmüş komisyon oranını geri döndürür; liste onunla tazelenir ki kullanıcı
    /// eşleştirmenin fiyata etkisini anında görsün.</summary>
    /// <summary>Seçili kanal kategorisinin nitelikleri — eşleştirme combo'sunun kaynağı. Kanal kategorisi
    /// değişince tazelenir.</summary>
    private IReadOnlyList<ProductCategoryLayout.ChannelAttributeOption> _channelAttributeOptions
        = Array.Empty<ProductCategoryLayout.ChannelAttributeOption>();

    /// <summary>
    /// Kanal kategorisinin niteliklerini yükler ve ÜÇ pazaryerinin farklı DTO'sunu tek şekle (kimlik + ad)
    /// indirger — eşleştirme tablosu kanal-agnostik kalsın diye.
    ///
    /// <para><b>VARYANT ekseni nitelikleri DIŞLANIR</b> (N11 IsVariant / Trendyol Varianter): onlar ürün
    /// seviyesinde değil varyant (stok kalemi) başına gider; listede gösterilseydi kullanıcı ürün özelliğini
    /// oraya eşleştirir ve değer push'ta hiç yazılmazdı.</para>
    ///
    /// <para>Nitelik listesi çekilemezse (kanal API'si kapalı/yavaş) BOŞ döner ve kullanıcı uyarılır — sessiz
    /// boş liste "bu kategoride nitelik yok" gibi okunurdu.</para>
    /// </summary>
    private async Task LoadChannelAttributeOptionsAsync(SalesChannelType channel, string? channelCategoryExternalId)
    {
        if (string.IsNullOrWhiteSpace(channelCategoryExternalId))
        {
            _channelAttributeOptions = Array.Empty<ProductCategoryLayout.ChannelAttributeOption>();
            return;
        }

        try
        {
            _channelAttributeOptions = channel switch
            {
                SalesChannelType.TrN11 => (await N11CategoryAppService.GetLeafAttributesAsync(channelCategoryExternalId))
                    .Where(a => !a.IsVariant)
                    .Select(a => new ProductCategoryLayout.ChannelAttributeOption(
                        a.AttributeId,
                        a.Name,
                        a.IsMandatory,
                        // ValueId'si olmayan değer eşleştirilemez (kanal kimlik bekler) → listeye alınmaz.
                        a.Values
                            .Where(v => !string.IsNullOrWhiteSpace(v.ValueId))
                            .Select(v => new ProductCategoryLayout.ChannelValueOption(v.ValueId!, v.Value))
                            .ToList()))
                    .ToList(),
                SalesChannelType.TrTrendyol => (await TrendyolCategoryAppService.GetLeafAttributesAsync(channelCategoryExternalId))
                    .Where(a => !a.Varianter)
                    .Select(a => new ProductCategoryLayout.ChannelAttributeOption(
                        a.AttributeId.ToString(CultureInfo.InvariantCulture),
                        a.Name,
                        a.Required,
                        a.Values
                            .Select(v => new ProductCategoryLayout.ChannelValueOption(
                                v.ValueId.ToString(CultureInfo.InvariantCulture), v.Value))
                            .ToList()))
                    .ToList(),
                SalesChannelType.Etsy => long.TryParse(channelCategoryExternalId, out var taxonomyId)
                    ? (await EtsyTaxonomyAppService.GetPropertiesAsync(taxonomyId))
                        .Select(a => new ProductCategoryLayout.ChannelAttributeOption(
                            a.PropertyId.ToString(CultureInfo.InvariantCulture),
                            string.IsNullOrWhiteSpace(a.DisplayName) ? a.Name : a.DisplayName,
                            a.IsRequired,
                            a.PossibleValues
                                .Select(v => new ProductCategoryLayout.ChannelValueOption(
                                    v.ValueId.ToString(CultureInfo.InvariantCulture), v.Name))
                                .ToList()))
                        .ToList()
                    : new List<ProductCategoryLayout.ChannelAttributeOption>(),
                _ => new List<ProductCategoryLayout.ChannelAttributeOption>(),
            };

            // ESAS nitelikler EN ÜSTTE (2026-07-28 Hakan): N11 her kategoriye zorunlu-olmayan GPSR/ürün
            // güvenliği nitelikleri ekliyor ve alfabetik sırada bunlar "Marka"/"Ayar" gibi esas nitelikleri
            // listenin dibine itiyordu. Zorunlular önce, kendi içlerinde ada göre.
            _channelAttributeOptions = _channelAttributeOptions
                .OrderByDescending(o => o.IsMandatory)
                .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _channelAttributeOptions = Array.Empty<ProductCategoryLayout.ChannelAttributeOption>();
            UiService.ShowErrorToast(
                CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }

        StateHasChanged();
    }

    private async Task SaveChannelMappingAsync(ProductCategoryChannelMappingDto row)
    {
        if (Id is not { } id)
        {
            return;
        }

        var saved = await ProductCategoryAppService.SaveChannelMappingAsync(id, new ProductCategoryChannelMappingSaveDto
        {
            Channel = row.Channel,
            ChannelCategoryExternalId = row.ChannelCategoryExternalId,
            ChannelCategoryName = row.ChannelCategoryName,
            AttributeMappings = row.AttributeMappings,
        });

        await LoadChannelMappingsAsync();
        StateHasChanged();
    }

    private async Task RemoveChannelMappingAsync(SalesChannelType channel)
    {
        if (Id is not { } id)
        {
            return;
        }

        await ProductCategoryAppService.DeleteChannelMappingAsync(id, channel);
        await LoadChannelMappingsAsync();
        StateHasChanged();
    }
}
