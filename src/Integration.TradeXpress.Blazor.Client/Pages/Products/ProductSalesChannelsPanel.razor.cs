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
using Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;
using Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Birleşik satış-kanalı ürünleri paneli — N11 + Trendyol kanal ürünlerini TEK grid'de listeler; düzenleme
/// ChannelType'a göre AYRI edit formu açar. IN-MEMORY GRAF: iki kaynak liste (N11Items = Model.SalesChannelProducts,
/// TrendyolItems = Model.SalesChannelTrendyolProducts) KORUNUR; grid için <see cref="SalesChannelProductRow"/>
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

    /// <summary>Ürün formunun cascade ettiği satışa-hazırlık issue endeksi. Panel ürün formu DIŞINDA da
    /// çizilebildiği için OPSİYONELDİR: endeks yoksa durum kolonu hiç görünmez (boş kolon çizmek yerine).</summary>
    [CascadingParameter(Name = "SaleReadinessIndex")] private SaleReadinessIssueIndex? Readiness { get; set; }

    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IProductCategoryAppService ProductCategoryAppService { get; set; } = default!;
    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;

    private DrillList<SalesChannelProductRow>? _drill;

    // Açık N11 edit formunun referansı — SaveGuard zorunlu alan doğrulamasını ona delege eder (zorunlu
    // nitelik tanımları o bileşende yüklü). Popup kapanınca Blazor referansı doğal olarak tazeler.
    private SalesChannelTrN11ProductEditFields? _n11EditFields;
    private SalesChannelTrTrendyolProductEditFields? _trendyolEditFields;

    /// <summary>Kaydetmeden ÖNCE açık hücre düzenlemelerini (kategori nitelik grid'leri) modele commit eder —
    /// aksi hâlde seçilen değer edit-model'de kalır ve sessizce kaybolur (drill <c>BeforeSave</c> geri çağrısı).</summary>
    private async Task CommitPendingCellEditsAsync(SalesChannelProductRow row)
    {
        if (row.IsN11 && _n11EditFields is not null)
        {
            await _n11EditFields.CommitPendingAttributeEditAsync();
        }

        if (row.IsTrendyol && _trendyolEditFields is not null)
        {
            await _trendyolEditFields.CommitPendingAttributeEditAsync();
        }
    }

    /// <summary>Kanal ürünü satırı kaydedilirken zorunlu alan kontrolü — mesaj döner = kayıt engellenir,
    /// popup açık kalır. Bugün yalnız N11 (zorunlu nitelik kavramı orada); Trendyol/Etsy push aşamalarında
    /// kendi kuralları geldikçe buraya eklenir.</summary>
    private string? ChannelRowSaveGuard(SalesChannelProductRow row)
    {
        return row.IsN11 ? _n11EditFields?.ValidateMandatoryInputs() : null;
    }

    /// <summary>Satırın issue kapsamı. KAYDEDİLMEMİŞ satırda (Id boş) kapsam YOKTUR: sunucu henüz o kayıt
    /// hakkında issue üretemez ve boş kapsam "kök" sayılıp ürünün TÜM issue'larını bu satıra yapıştırırdı.</summary>
    private string? ReadinessScopeOf(SalesChannelProductRow row)
    {
        return ChannelReadinessScopes.ChannelOf(row.Id);
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

    // ── "Yeni" DROPDOWN buton: ana tık MENÜYÜ açar, tip (N11 / Trendyol / Etsy) menüden seçilir. Built-in Yeni
    //    kapalı (AllowAdd=false). Eskiden split'ti ve ana tık DOĞRUDAN N11 taslağı açıyordu — kanal-agnostik bir
    //    panelde tek kanalı kayırmak yanılttı (2026-08-19 Hakan: "Yeni buttonu agnostik olmalı, menü öğelerini
    //    göstermeli"). ──
    private IReadOnlyList<CrudToolbarAction> PanelActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 0,
            Text = L["New"],
            Tooltip = L["New"],
            IconCssClass = FrameworkIcons.Add,
            Items = BuildNewChannelProductItems(),
        },
    };

    /// <summary>"Yeni ▾" menüsü — her satır KANALIN KENDİ KODU (2026-08-20 Hakan: <i>"kanal tipi değil doğrudan
    /// kanalın kendi kodu yer alsın"</i>). Menü şirkette TANIMLI kanallardan kurulur; tanımlı kanalı olmayan tür
    /// menüde HİÇ görünmez — "N11 Ürünü" yazan bir satıra basıp "kanal yok" uyarısı almak, olmayan bir seçeneği
    /// önermekti.
    ///
    /// <para><b>Yan kazanç:</b> kanal ARTIK SEÇİLİYOR. Eski menü tür seçtiriyor, kod ise o türün İLK kanalını
    /// sessizce alıyordu (<c>FirstOrDefault</c>); şirkette iki N11 mağazası varsa kullanıcı hangisine ürün
    /// açtığını bilmiyordu ve ikinciye ürün açmanın yolu yoktu.</para>
    ///
    /// <para>Etiket <c>Kod — Ad</c>: kod kısa ve kullanıcının kendi verdiği kimliktir, ad ise ayırt ediciliği
    /// tamamlar (iki mağaza benzer kodlanmış olabilir). Ad boşsa yalnız kod yazılır.</para></summary>
    private IReadOnlyList<CrudToolbarAction> BuildNewChannelProductItems()
    {
        var items = new List<CrudToolbarAction>();

        foreach (var channel in _n11Channels)
        {
            var id = channel.Id;
            items.Add(NewChannelProductItem(channel, () => StartNewN11Async(id)));
        }

        foreach (var channel in _trendyolChannels)
        {
            var id = channel.Id;
            items.Add(NewChannelProductItem(channel, () => StartNewTrendyolAsync(id)));
        }

        foreach (var channel in _etsyChannels)
        {
            var id = channel.Id;
            items.Add(NewChannelProductItem(channel, () => StartNewEtsyAsync(id)));
        }

        // Hiç kanal yoksa menü BOŞ kalmaz: tek pasif satır "önce kanal tanımlayın" der. Boş açılan bir menü
        // "bozuk" görünür; devre dışı bir açıklama satırı ne yapılması gerektiğini söyler.
        if (items.Count == 0)
        {
            items.Add(new CrudToolbarAction
            {
                Text = L["SalesChannelProduct:NoChannelDefined"].Value,
                IconCssClass = TradeXpressIcons.SalesChannel,
                Enabled = false,
            });
        }

        return items;
    }

    private CrudToolbarAction NewChannelProductItem(SalesChannelListDto channel, Func<Task> onClick)
    {
        var label = string.IsNullOrWhiteSpace(channel.Name)
            ? channel.Code
            : $"{channel.Code} — {channel.Name}";

        return new CrudToolbarAction
        {
            Text = label,
            Tooltip = label,
            IconCssClass = TradeXpressIcons.SalesChannel,
            OnClick = onClick,
        };
    }

    // ── Yeni taslak akışları — şirketin TEK kanalını otomatik bul (yoksa dostane uyarı), kanal atanmış taslakla aç. ──

    private async Task StartNewN11Async(Guid channelId)
    {
        // Kanal MENÜDEN seçilir (2026-08-20): "türün İLK kanalı" varsayımı kalktı. Menü yalnız tanımlı
        // kanallardan kurulduğu için "kanal yok" dalı da konusuzdur.
        var draft = BuildNewN11Draft(channelId);
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

    private async Task StartNewTrendyolAsync(Guid channelId)
    {
        var draft = BuildNewTrendyolDraft(channelId);
        if (await ResolveChannelCategoryAsync(SalesChannelType.TrTrendyol) is { } resolution
            && !string.IsNullOrWhiteSpace(resolution.ChannelCategoryExternalId))
        {
            draft.CategoryId = resolution.ChannelCategoryExternalId;
            draft.CategoryName = resolution.ChannelCategoryName;
        }

        _drill?.StartNewItem(SalesChannelProductRow.ForTrendyol(draft));
    }

    private async Task StartNewEtsyAsync(Guid channelId)
    {
        var draft = BuildNewEtsyDraft(channelId);
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
    /// Ürünün core kategorisinin bu KANALDAKİ karşılığı — yeni kanal ürünü taslağına varsayılan kategori
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

    // ── Kanal aksiyonları — uygulama ChannelProductActions'ta (TEK yer); burada yalnız bağlam + sonuç kopyası ──

    /// <summary>Satırın aksiyon bağlamı — tipine göre ilgili <c>From(...)</c> eşleyicisi. Düğme uygunluğu
    /// (Gönder hep · Stok-Fiyat / Durumu Yenile yalnız kanala ulaşmış kayıtta) eski panelin görünür davranışıyla
    /// birebir; kural eşleyicide yaşar, burada tekrarlanmaz.</summary>
    private static ChannelProductActionContext ActionContextOf(SalesChannelProductRow row)
    {
        if (row.IsN11)
        {
            return ChannelProductActionContext.From(row.N11!);
        }

        if (row.IsEtsy)
        {
            return ChannelProductActionContext.From(row.Etsy!);
        }

        return ChannelProductActionContext.From(row.Trendyol!);
    }

    // Push/sync sonrası N11 durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf; kullanıcının
    // kaydedilmemiş düzenlemeleri korunur). Callback sahibi bu panel olduğundan render kendiliğinden tazelenir.
    private static void CopyN11StatusInto(SalesChannelTrN11ProductDto target, SalesChannelTrN11ProductDto source)
    {
        target.N11ProductId = source.N11ProductId;
        target.SaleStatus = source.SaleStatus;
        target.ApprovalStatus = source.ApprovalStatus;
        target.LastSyncedAt = source.LastSyncedAt;
        target.LastError = source.LastError;
        target.PendingPushTaskId = source.PendingPushTaskId;
        target.PendingPushTaskAt = source.PendingPushTaskAt;
        target.Skus = source.Skus;
    }

    // Push/refresh/sync sonrası Trendyol durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf).
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
