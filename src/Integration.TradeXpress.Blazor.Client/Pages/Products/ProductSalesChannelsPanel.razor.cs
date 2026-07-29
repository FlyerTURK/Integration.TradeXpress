using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.Blazor.Client.Pages.N11Products;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Birleşik satış-kanalı ürünleri paneli — N11 + Trendyol kanal ürünlerini TEK grid'de listeler; düzenleme
/// ChannelType'a göre AYRI edit formu açar. IN-MEMORY GRAF: iki kaynak liste (N11Items = Model.SalesChannelProducts,
/// TrendyolItems = Model.SalesChannelTrendyolProducts) KORUNUR; grid için <see cref="SalesChannelProductRow"/> sarmalayıcı
/// satırları KAYNAK DTO'yu referansla tutar → edit doğrudan kaynağı değiştirir (graf senkron). Yeni eklemede
/// <see cref="OnRowSaved"/> kaynak listeye yansıtır; silmede <see cref="OnRowDeleted"/> + soft-delete işareti. Push/create
/// mantığı iki eski panelden (SalesChannelProductsPanel / SalesChannelTrendyolProductsPanel) AYNEN taşındı.</summary>
public partial class ProductSalesChannelsPanel : CrudComponentBase
{
    /// <summary>Ürün grafındaki N11 kanal ürünleri (Model.SalesChannelProducts) — in-memory düzenlenir.</summary>
    [Parameter, EditorRequired] public List<SalesChannelTrN11ProductDto> N11Items { get; set; } = default!;

    /// <summary>Ürün grafındaki Trendyol kanal ürünleri (Model.SalesChannelTrendyolProducts) — in-memory düzenlenir.</summary>
    [Parameter, EditorRequired] public List<SalesChannelTrTrendyolProductDto> TrendyolItems { get; set; } = default!;

    /// <summary>Ürün grafındaki Etsy kanal ürünleri (Model.SalesChannelEtsyProducts) — in-memory düzenlenir.</summary>
    [Parameter, EditorRequired] public List<SalesChannelEtsyProductDto> EtsyItems { get; set; } = default!;

    /// <summary>Bağlı ürünün Id'si (kaydedilmişse dolu; yeni üründe Guid.Empty). Push/sync + create için.</summary>
    [Parameter] public Guid ProductId { get; set; }

    /// <summary>Bağlı ürünün canlı grafı (ProductLayout.Model) — yeni kanal taslağının ürün-genel varsayılanlarını
    /// create-copy ile devralması için. Panel yalnız OKUR (dumb); mutate ETMEZ.</summary>
    [Parameter] public ProductGetDto? ProductDefaults { get; set; }

    /// <summary>Graf değişti — parent (ProductLayout) EditChanged'i tetikler (Save aktifliği).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private ISalesChannelTrN11ProductAppService N11AppService { get; set; } = default!;
    [Inject] private ISalesChannelTrTrendyolProductAppService TrendyolAppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IProductCategoryAppService ProductCategoryAppService { get; set; } = default!;
    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelProductRow>? _drill;

    // Açık N11 edit formunun referansı — SaveGuard zorunlu alan doğrulamasını ona delege eder (zorunlu
    // nitelik tanımları o bileşende yüklü). Popup kapanınca Blazor referansı doğal olarak tazeler.
    private SalesChannelTrN11ProductEditFields? _n11EditFields;

    /// <summary>Kanal ürünü satırı kaydedilirken zorunlu alan kontrolü — mesaj döner = kayıt engellenir,
    /// popup açık kalır. Bugün yalnız N11 (zorunlu nitelik kavramı orada); Trendyol/Etsy push aşamalarında
    /// kendi kuralları geldikçe buraya eklenir.</summary>
    private string? ChannelRowSaveGuard(SalesChannelProductRow row)
    {
        return row.IsN11 ? _n11EditFields?.ValidateMandatoryInputs() : null;
    }

    // Grid'in görüntülediği birleşik satır listesi — iki kaynak graf listesinden türetilir (kaydron REFERANS tutar).
    private List<SalesChannelProductRow> _rows = new();

    // Reload (yeni graf listeleri) tespiti — kaynak liste referansı değişince _rows yeniden kurulur.
    private List<SalesChannelTrN11ProductDto>? _seededN11;
    private List<SalesChannelTrTrendyolProductDto>? _seededTrendyol;
    private List<SalesChannelEtsyProductDto>? _seededEtsy;

    // Kanal AD çözümü + create'te otomatik kanal ataması (şirkette tip başına TEK kanal kuralı).
    private List<SalesChannelListDto> _n11Channels = new();
    private List<SalesChannelListDto> _trendyolChannels = new();
    private List<SalesChannelListDto> _etsyChannels = new();

    protected override async Task OnInitializedAsync()
    {
        // Tüm kanalları bir kez çek, tipe göre ayır (kanal adı çözümü + yeni kayıtta otomatik atama).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _n11Channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();
        _trendyolChannels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrTrendyol).ToList();
        _etsyChannels = paged.Items.Where(c => c.ChannelType == SalesChannelType.Etsy).ToList();
    }

    protected override void OnParametersSet()
    {
        // Kaynak liste referansı değiştiyse (ilk bağlama ya da ürün reload'u) grid satırlarını yeniden kur.
        if (!ReferenceEquals(_seededN11, N11Items)
            || !ReferenceEquals(_seededTrendyol, TrendyolItems)
            || !ReferenceEquals(_seededEtsy, EtsyItems))
        {
            _seededN11 = N11Items;
            _seededTrendyol = TrendyolItems;
            _seededEtsy = EtsyItems;
            RebuildRows();
        }
    }

    // İki kaynak graf listesinden birleşik satırları kur (kaynak DTO REFERANSI ile; IsDeleted olanlar da alınır —
    // FilterPredicate grid'de gizler ama graf diff'i için arka planda kalırlar).
    private void RebuildRows()
    {
        _rows = new List<SalesChannelProductRow>(N11Items.Count + TrendyolItems.Count + EtsyItems.Count);
        foreach (var n11 in N11Items)
        {
            _rows.Add(SalesChannelProductRow.ForN11(n11));
        }

        foreach (var trendyol in TrendyolItems)
        {
            _rows.Add(SalesChannelProductRow.ForTrendyol(trendyol));
        }

        foreach (var etsy in EtsyItems)
        {
            _rows.Add(SalesChannelProductRow.ForEtsy(etsy));
        }
    }

    // ── "Yeni" SPLIT buton: ana tık = N11; ▾ alt menüden tip (N11 / Trendyol). Built-in Yeni kapalı (AllowAdd=false). ──
    private IReadOnlyList<CrudToolbarAction> PanelActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 0,
            Text = L["New"],
            Tooltip = L["New"],
            IconCssClass = FrameworkIcons.Add,
            SplitDropDownButton = true,
            OnClick = StartNewN11Async,
            Items = new[]
            {
                new CrudToolbarAction
                {
                    Text = L["SalesChannelTrN11Product"],
                    IconCssClass = TradeXpressIcons.SalesChannel,
                    OnClick = StartNewN11Async,
                },
                new CrudToolbarAction
                {
                    Text = L["SalesChannelTrTrendyolProduct"],
                    IconCssClass = TradeXpressIcons.SalesChannel,
                    OnClick = StartNewTrendyolAsync,
                },
                new CrudToolbarAction
                {
                    Text = L["SalesChannelEtsyProduct"],
                    IconCssClass = TradeXpressIcons.SalesChannel,
                    OnClick = StartNewEtsyAsync,
                },
            },
        },
    };

    // ── Yeni taslak akışları — şirketin TEK kanalını otomatik bul (yoksa dostane uyarı), kanal atanmış taslakla aç. ──

    private async Task StartNewN11Async()
    {
        var channel = _n11Channels.FirstOrDefault();
        if (channel is null)
        {
            UiService.ShowWarningToast(L["N11Product:ChannelMissing"].Value);
            return;
        }

        var draft = BuildNewN11Draft(channel.Id);
        if (await ResolveChannelCategoryAsync(SalesChannelType.TrN11) is { } resolution
            && !string.IsNullOrWhiteSpace(resolution.ChannelCategoryExternalId))
        {
            draft.CategoryExternalId = resolution.ChannelCategoryExternalId;
            draft.CategoryName = resolution.ChannelCategoryName;
        }

        draft.CategoryAttributes = (await ResolveChannelAttributesAsync(SalesChannelType.TrN11))
            .Select(a => new SalesChannelTrN11ProductCategoryAttributeDto { Name = a.Name, Value = a.Value })
            .ToList();

        _drill?.StartNewItem(SalesChannelProductRow.ForN11(draft));
    }

    private async Task StartNewTrendyolAsync()
    {
        var channel = _trendyolChannels.FirstOrDefault();
        if (channel is null)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:ChannelMissing"].Value);
            return;
        }

        var draft = BuildNewTrendyolDraft(channel.Id);
        if (await ResolveChannelCategoryAsync(SalesChannelType.TrTrendyol) is { } resolution
            && !string.IsNullOrWhiteSpace(resolution.ChannelCategoryExternalId))
        {
            draft.CategoryId = resolution.ChannelCategoryExternalId;
            draft.CategoryName = resolution.ChannelCategoryName;
        }

        _drill?.StartNewItem(SalesChannelProductRow.ForTrendyol(draft));
    }

    private async Task StartNewEtsyAsync()
    {
        var channel = _etsyChannels.FirstOrDefault();
        if (channel is null)
        {
            UiService.ShowWarningToast(L["EtsyProduct:ChannelMissing"].Value);
            return;
        }

        var draft = BuildNewEtsyDraft(channel.Id);
        // Etsy taksonomi kimliği SAYISAL; eşleştirme metin tuttuğundan ayrıştırılamayan değer sessizce ATLANIR
        // (bozuk bir id yazmak, kullanıcının fark edemeyeceği bir push hatasına dönüşürdü).
        if (await ResolveChannelCategoryAsync(SalesChannelType.Etsy) is { } resolution
            && long.TryParse(resolution.ChannelCategoryExternalId, out var taxonomyId))
        {
            draft.TaxonomyId = taxonomyId;
            draft.TaxonomyName = resolution.ChannelCategoryName;
        }

        _drill?.StartNewItem(SalesChannelProductRow.ForEtsy(draft));
    }

    /// <summary>
    /// Ürünün çekirdek kategorisinin bu KANALDAKİ karşılığı — yeni kanal ürünü taslağına varsayılan kategori
    /// olarak yerleşir. Eşleştirme ATA zincirinden devralınabilir (sunucu çözer).
    ///
    /// <para><b>Neden taslakta:</b> kullanıcı kategori eşleştirmesini kategori formunda bir kez yapıyor; kanal
    /// ürününde aynı seçimi tekrar aramak zorunda kalması eşleştirmenin varlık sebebini boşa çıkarırdı. Seçim
    /// KİLİTLİ değil — kanal ürününde serbestçe değiştirilebilir (kanala özel istisnalar olabilir).</para>
    ///
    /// <para>MEVCUT kanal ürünlerine DOKUNULMAZ: yalnız yeni taslak doldurulur. Sonradan kategori değiştiğinde
    /// kayıtlı kanal ürünlerinin kategorisini sessizce değiştirmek, kullanıcının bilerek yaptığı istisnaları
    /// silerdi.</para>
    ///
    /// <para>Ürün henüz kaydedilmemişse de çalışır: kategori kimliği formdaki canlı graftan okunur.</para>
    /// </summary>
    /// <summary>
    /// Ürünün genel özelliklerini bu kanalın nitelik alanlarına çevirir — yeni kanal ürünü bunlarla ön-dolar.
    ///
    /// <para>Çeviriyi SUNUCU yapar: nitelik ve değer eşleştirmeleri kategori zincirinde (kalıtımlı) yaşıyor ve
    /// istemcinin o zinciri yeniden çözmesi aynı kuralın ikinci bir kopyası olurdu.</para>
    ///
    /// <para>Ürün henüz kaydedilmemişse de çalışır: özellik değerleri formdaki canlı graftan gönderilir.</para>
    /// </summary>
    private async Task<List<ProductChannelAttributeDto>> ResolveChannelAttributesAsync(SalesChannelType channel)
    {
        if (ProductDefaults is not { } product
            || product.ProductCategoryId is not { } categoryId
            || categoryId == Guid.Empty
            || product.Specifications.Count == 0)
        {
            return new List<ProductChannelAttributeDto>();
        }

        return await ProductAppService.ResolveChannelAttributesAsync(new ProductChannelAttributeResolveDto
        {
            ProductCategoryId = categoryId,
            Channel = channel,
            Specifications = product.Specifications,
        });
    }

    private async Task<ProductChannelResolutionDto?> ResolveChannelCategoryAsync(SalesChannelType channel)
    {
        if (ProductDefaults?.ProductCategoryId is not { } categoryId || categoryId == Guid.Empty)
        {
            return null;
        }

        return await ProductCategoryAppService.ResolveChannelAsync(categoryId, channel);
    }

    /// <summary>N11 kanal ürünü taslağı — ürün-genel varsayılanlardan create-copy (SalesChannelProductsPanel'den AYNEN).
    /// Kanal-özel/N11-özel alanlara (Category/Attributes/SellerCode/Group...) DOKUNULMAZ.</summary>
    private SalesChannelTrN11ProductDto BuildNewN11Draft(Guid salesChannelId)
    {
        var draft = new SalesChannelTrN11ProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            Condition = N11ProductCondition.New,
            Domestic = true,
            PreparingDay = 1,
            IsActive = true,
        };

        if (ProductDefaults is { } p)
        {
            // ProductCondition (New/Used) → N11ProductCondition (değerler farklı: 0/1 vs 1/2).
            draft.Condition = p.Condition == ProductCondition.Used ? N11ProductCondition.Used : N11ProductCondition.New;
            // "Yerli ürün" bayrağı ürünün MENŞEİ ülkesinden türetilir (sunucu hesaplar); menşei
            // belirtilmemişse taslak varsayılanı korunur — bilinmiyorken ithal beyan etmeyelim.
            draft.Domestic = p.IsDomestic ?? draft.Domestic;
            draft.PreparingDay = p.PreparingDay;
            draft.MaxPurchaseQuantity = p.MaxPurchaseQuantity;
            draft.SellerNote = p.SellerNote;
            draft.CurrencyUnitId = p.CurrencyUnitId;
            draft.ProductionDate = p.ProductionDate;
            draft.ExpirationDate = p.ExpirationDate;
            draft.Description = p.Description;
            // Özel bilgi listesi kopyalanır (yeni DTO satırları; ClientKey ctor'da üretilir) — referans paylaşılmaz.
            draft.SpecialInfo = p.SpecialInfo
                .Select(s => new SalesChannelTrN11ProductSpecialInfoDto { Key = s.Key, Value = s.Value })
                .ToList();
        }

        return draft;
    }

    /// <summary>Trendyol kanal ürünü taslağı — ürün-genel varsayılanlardan create-copy (SalesChannelTrendyolProductsPanel'den
    /// AYNEN). Kanal-özel alanlara (Category/Brand/Attributes) dokunulmaz.</summary>
    private SalesChannelTrTrendyolProductDto BuildNewTrendyolDraft(Guid salesChannelId)
    {
        var draft = new SalesChannelTrTrendyolProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            VatRate = 20,
            IsActive = true,
        };

        if (ProductDefaults is { } p)
        {
            draft.Description = p.Description;
            draft.DeliveryDuration = p.PreparingDay;
        }

        return draft;
    }

    /// <summary>Etsy kanal ürünü taslağı — ürün-genel varsayılanlardan create-copy. Etsy-özel alanlar
    /// (taksonomi/etiket/malzeme/kargo profili) BOŞ bırakılır (kullanıcı edit formunda doldurur); listeleme türü
    /// varsayılan Physical.</summary>
    private SalesChannelEtsyProductDto BuildNewEtsyDraft(Guid salesChannelId)
    {
        var draft = new SalesChannelEtsyProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            ListingType = EtsyListingType.Physical,
            PreparingDay = 1,
            IsActive = true,
        };

        if (ProductDefaults is { } p)
        {
            draft.DescriptionOverride = p.Description;
            draft.SellerNote = p.SellerNote;
            draft.CurrencyUnitId = p.CurrencyUnitId;
            draft.PreparingDay = p.PreparingDay;
            // Özel bilgi listesi kopyalanır (yeni DTO satırları; ClientKey ctor'da üretilir) — referans paylaşılmaz.
            draft.SpecialInfo = p.SpecialInfo
                .Select(s => new SalesChannelEtsyProductSpecialInfoDto { Key = s.Key, Value = s.Value })
                .ToList();
        }

        return draft;
    }

    // Built-in Yeni (AllowAdd=false → gizli) + Kaydet&Yeni için gereken factory; pratikte kullanılmaz (split akışı esas).
    private SalesChannelProductRow NewRowFactory()
    {
        return SalesChannelProductRow.ForN11(BuildNewN11Draft(_n11Channels.FirstOrDefault()?.Id ?? Guid.Empty));
    }

    // Cancel geri alabilsin diye JSON deep-copy (satır + saran DTO'nun listeleri dahil).
    private static SalesChannelProductRow CloneRow(SalesChannelProductRow source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<SalesChannelProductRow>(json)!;
    }

    // ── Graf senkronu — DrillList _rows'u yönetir (Add/Edit/Remove); burada kaynak graf listelerine yansıtırız. ──

    // Kaydedilen satırın kaynak DTO'sunu ilgili graf listesine yaz (yeni → ekle; mevcut → ClientKey ile değiştir).
    // Edit'te CloneFactory kaynak DTO'yu klonlar; kaydolunca klon HEM _rows'a (DrillList) HEM buradan grafa geçer → senkron.
    private void OnRowSaved(SalesChannelProductRow row)
    {
        if (row.N11 is { } n11)
        {
            var idx = N11Items.FindIndex(x => x.ClientKey == n11.ClientKey);
            if (idx >= 0)
            {
                N11Items[idx] = n11;
            }
            else
            {
                N11Items.Add(n11);
            }
        }
        else if (row.Trendyol is { } trendyol)
        {
            var idx = TrendyolItems.FindIndex(x => x.ClientKey == trendyol.ClientKey);
            if (idx >= 0)
            {
                TrendyolItems[idx] = trendyol;
            }
            else
            {
                TrendyolItems.Add(trendyol);
            }
        }
        else if (row.Etsy is { } etsy)
        {
            var idx = EtsyItems.FindIndex(x => x.ClientKey == etsy.ClientKey);
            if (idx >= 0)
            {
                EtsyItems[idx] = etsy;
            }
            else
            {
                EtsyItems.Add(etsy);
            }
        }
    }

    // Silinen satırı grafa yansıt: YENİ (Id boş, hiç kaydedilmemiş) → kaynak listeden TAMAMEN çıkar. MEVCUT → MarkDeleted
    // zaten kaynak DTO.IsDeleted işaretledi (referans paylaşıldığı için grafa yansıdı); burada ek iş yok.
    private void OnRowDeleted(SalesChannelProductRow row)
    {
        if (row.Id != Guid.Empty)
        {
            return;
        }

        if (row.N11 is { } n11)
        {
            N11Items.RemoveAll(x => x.ClientKey == n11.ClientKey);
        }
        else if (row.Trendyol is { } trendyol)
        {
            TrendyolItems.RemoveAll(x => x.ClientKey == trendyol.ClientKey);
        }
        else if (row.Etsy is { } etsy)
        {
            EtsyItems.RemoveAll(x => x.ClientKey == etsy.ClientKey);
        }
    }

    // ── Durum metni (grid Status kolonu) — tipe göre (iki eski panelin StatusTextOf'undan). ──
    private string StatusTextOf(SalesChannelProductRow row)
    {
        if (row.Id == Guid.Empty)
        {
            if (row.IsEtsy)
            {
                return L["EtsyProduct:NotSaved"].Value;
            }

            return (row.IsN11 ? L["N11Product:NotSaved"] : L["TrendyolProduct:NotSaved"]).Value;
        }

        if (row.IsN11)
        {
            var n11 = row.N11!;
            if (!n11.N11ProductId.HasValue)
            {
                return L["N11Product:NotSent"].Value;
            }

            return $"{n11.SaleStatus} / {n11.ApprovalStatus}";
        }

        if (row.IsEtsy)
        {
            var etsy = row.Etsy!;
            if (!etsy.EtsyListingId.HasValue)
            {
                return L["EtsyProduct:NotSent"].Value;
            }

            return etsy.ListingState ?? string.Empty;
        }

        var trendyol = row.Trendyol!;
        if (string.IsNullOrEmpty(trendyol.BatchRequestId))
        {
            return L["TrendyolProduct:NotSent"].Value;
        }

        return trendyol.Status ?? string.Empty;
    }

    // ── N11 push / stok-fiyat senkronu (SalesChannelProductsPanel'den AYNEN) ──

    private async Task PushN11Async(SalesChannelTrN11ProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var pushed = await N11AppService.PushToN11Async(channelProduct.Id);
            CopyN11StatusInto(channelProduct, pushed);
            UiService.ShowSuccessToast(L["N11Product:PushSuccess"].Value);

            foreach (var warning in pushed.SyncWarnings)
            {
                UiService.ShowWarningToast(warning);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private async Task SyncN11StockPriceAsync(SalesChannelTrN11ProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var synced = await N11AppService.SyncStockAndPriceAsync(channelProduct.Id);
            CopyN11StatusInto(channelProduct, synced);
            UiService.ShowSuccessToast(L["N11Product:SyncStockPriceSuccess"].Value);

            foreach (var warning in synced.SyncWarnings)
            {
                UiService.ShowWarningToast(warning);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Push/sync sonrası N11 durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf).
    private static void CopyN11StatusInto(SalesChannelTrN11ProductDto target, SalesChannelTrN11ProductDto source)
    {
        target.N11ProductId = source.N11ProductId;
        target.SaleStatus = source.SaleStatus;
        target.ApprovalStatus = source.ApprovalStatus;
        target.LastSyncedAt = source.LastSyncedAt;
        target.LastError = source.LastError;
        target.Skus = source.Skus;
    }

    // ── Trendyol push / durum yenileme (SalesChannelTrendyolProductsPanel'den AYNEN) ──

    private async Task PushTrendyolAsync(SalesChannelTrTrendyolProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var pushed = await TrendyolAppService.PushToTrendyolAsync(channelProduct.Id);
            CopyTrendyolStatusInto(channelProduct, pushed);
            UiService.ShowSuccessToast(L["TrendyolProduct:PushSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private async Task RefreshTrendyolStatusAsync(SalesChannelTrTrendyolProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var refreshed = await TrendyolAppService.RefreshStatusAsync(channelProduct.Id);
            CopyTrendyolStatusInto(channelProduct, refreshed);
            UiService.ShowSuccessToast(L["TrendyolProduct:RefreshSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Push/refresh sonrası Trendyol durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf).
    private static void CopyTrendyolStatusInto(SalesChannelTrTrendyolProductDto target, SalesChannelTrTrendyolProductDto source)
    {
        target.BatchRequestId = source.BatchRequestId;
        target.LastBatchRequestType = source.LastBatchRequestType;
        target.Status = source.Status;
        target.FailedItemCount = source.FailedItemCount;
        target.LastSyncedAt = source.LastSyncedAt;
        target.LastError = source.LastError;
        target.Skus = source.Skus;
    }
}
