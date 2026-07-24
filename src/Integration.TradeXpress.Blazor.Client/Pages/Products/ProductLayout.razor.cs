using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Shipments;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product dumb layout code-behind — Model bağlama + varyant drill referansı + dirty cascade.</summary>
public partial class ProductLayout
{
    [Parameter, EditorRequired] public ProductGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    // Reçete drill'inin katalog lookup verisi — host yükler (DUMB layout servis çağırmaz).
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();
    [Parameter] public IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; set; } = Array.Empty<MetalVariantLookupDto>();
    [Parameter] public IReadOnlyList<ScrapListDto> Scraps { get; set; } = Array.Empty<ScrapListDto>();
    [Parameter] public IReadOnlyList<FutureListDto> Futures { get; set; } = Array.Empty<FutureListDto>();
    [Parameter] public IReadOnlyList<JewelryListDto> Jewelries { get; set; } = Array.Empty<JewelryListDto>();
    [Parameter] public IReadOnlyList<GoodListDto> Goods { get; set; } = Array.Empty<GoodListDto>();
    [Parameter] public IReadOnlyList<StoneListDto> Stones { get; set; } = Array.Empty<StoneListDto>();
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

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
    [Parameter] public IReadOnlyList<ShipmentTemplateListDto> ShipmentTemplates { get; set; } = Array.Empty<ShipmentTemplateListDto>();

    /// <summary>Inline kargo şablonu ekle/düzelt sonrası lookup listesini host tazeler (EntityChange tetikler).</summary>
    [Parameter] public EventCallback OnReloadShipmentTemplates { get; set; }

    // Nitelik + varyant drill'leri artık JENERİK paylaşılan panellerde (EntityAttributesPanel / EntityVariantsPanel);
    // yalnız görsel drill'i bu layout'ta kalır.
    private DrillList<ProductImageGraphDto>? _imageDrill;

    /// <summary>Görsel önizleme kaynağı — URL tipli doğrudan URL, yüklenmişte sunucunun doldurduğu data-URL.</summary>
    private static string? PreviewSrcOf(ProductImageGraphDto image)
    {
        return image.SourceType == ProductImageSourceType.Url ? image.Url : image.PreviewDataUrl;
    }

    // Cancel geri alabilsin diye kopya üzerinde düzenleme (upload'ın blob yazımı geri alınmaz — süpürücü işi;
    // ama Model.Images'taki CANLI satır iptalde mutate edilmemiş kalır).
    private static ProductImageGraphDto CloneImage(ProductImageGraphDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<ProductImageGraphDto>(json)!;
    }

    /// <summary>Tekil-bayrak transferi (HQ-devri deseni): kaydedilen görsel VARSAYILAN işaretliyse diğerlerinin
    /// bayrağı düşer — aksi halde sunucu EnsureSingleDefault "ilki kalır" kuralıyla kullanıcının YENİ seçimini
    /// sessizce geri alırdı (review bulgusu).</summary>
    private void TransferDefaultImage(ProductImageGraphDto saved)
    {
        if (!saved.IsDefault)
        {
            return;
        }

        foreach (var other in Model.Images.Where(x => x.ClientKey != saved.ClientKey && x.IsDefault))
        {
            other.IsDefault = false;
        }
    }

    /// <summary>Görsel kaydetme engeli: aynı ürüne aynı URL (case-duyarsız) ya da aynı BLOB adı İKİ KEZ girilemez.
    /// Dosya adı ARTIK dedupe anahtarı DEĞİL (blob adı path-önekli + sunucu ilk-boş-sıra probe'uyla tekil; aynı
    /// dosya adı farklı varyant klasöründe meşru). Sunucu SetImages'ta da aynı kural (savunma).</summary>
    private string? ImageSaveGuard(ProductImageGraphDto candidate)
    {
        var others = Model.Images.Where(x => x.ClientKey != candidate.ClientKey);
        var url = candidate.Url?.Trim();
        var duplicateUrl = url is { Length: > 0 }
            && others.Any(x => string.Equals(x.Url?.Trim(), url, StringComparison.OrdinalIgnoreCase));
        var duplicateBlob = candidate.BlobName is { Length: > 0 }
            && others.Any(x => string.Equals(x.BlobName, candidate.BlobName, StringComparison.Ordinal));
        return duplicateUrl || duplicateBlob ? L["TradeXpress:Product:ImageDuplicate"].Value : null;
    }

    /// <summary>Özel bilgi satırı kaydetme engeli — key boşsa satır kabul edilmez (SetSpecialInfo sunucuda da boş key eler).</summary>
    private string? SpecialInfoSaveGuard(ProductSpecialInfoDto item)
    {
        return string.IsNullOrWhiteSpace(item.Key) ? L["Product:SpecialInfoKeyRequired"].Value : null;
    }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>Reçete değişince CANLI maliyet — host yapar (persistsiz hesap, varyant bazında); tam kayıt gerekmez.</summary>
    [Parameter] public Func<ProductVariantGraphDto, Task>? OnRecipeChanged { get; set; }

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

    // ── Varyant modu + Muadil (Dilim-3) — layout DUMB: onay/servis işleri host'ta, burada yalnız bağlama ──

    /// <summary>Varyant modu değişim İSTEĞİ — host onaylar (MultiVariant'tan çıkışta veri-kaybı uyarısı) ve
    /// modeli günceller; reddederse model değişmez (combo eski değere geri çizilir).</summary>
    [Parameter] public Func<ProductVariantMode, Task>? OnVariantModeChangeRequested { get; set; }

    /// <summary>Muadil grubu lookup verisi — host yükler (aktif gruplar).</summary>
    [Parameter] public IReadOnlyList<SubstitutionGroupListDto> SubstitutionGroups { get; set; } = Array.Empty<SubstitutionGroupListDto>();

    /// <summary>Inline muadil grubu ekle/düzelt sonrası lookup listesini host tazeler.</summary>
    [Parameter] public EventCallback OnReloadSubstitutionGroups { get; set; }

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

    // Grup kalemlerinin son istenen grup id'si — OnParametersSetAsync tetiklemesi yalnız DEĞİŞİMDE bir kez koşar
    // (mevcut kayıt Muadil modunda açıldığında devralınan-küme referansı host'tan yüklensin diye).
    private Guid? _requestedSubstitutionGroupId;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (Model.VariantMode == ProductVariantMode.Substitution
            && Model.SubstitutionGroupId != _requestedSubstitutionGroupId
            && OnSubstitutionGroupChanged.HasDelegate)
        {
            _requestedSubstitutionGroupId = Model.SubstitutionGroupId;
            await OnSubstitutionGroupChanged.InvokeAsync(Model.SubstitutionGroupId);
        }
    }

    /// <summary>Muadil sekmesinin grid satırları — hesaplama sayfası BuildRows dizilimiyle birebir
    /// (başarılılar üstte Rank sırasıyla; TrialNo orijinal deneme numarasını korur). ÖNBELLEKLİ:
    /// sonuç referansı değişmedikçe aynı satır instance'ları döner (grid seçim kimliği korunur).</summary>
    private List<SubstitutionTrialRow> SubstitutionTrialRows
    {
        get
        {
            if (ReferenceEquals(_trialRowsSource, SubstitutionResult))
            {
                return _trialRows;
            }

            _trialRowsSource = SubstitutionResult;
            _selectedTrialRow = null;
            _selectedTrialItems = Array.Empty<object>();

            if (SubstitutionResult is not { } result)
            {
                _trialRows = new List<SubstitutionTrialRow>();
                return _trialRows;
            }

            var rows = new List<SubstitutionTrialRow>(result.Trials.Count);
            for (var i = 0; i < result.Trials.Count; i++)
            {
                var trial = result.Trials[i];
                rows.Add(new SubstitutionTrialRow
                {
                    Trial        = trial,
                    TrialNo      = i + 1,
                    Combination  = SubstitutionTrialFormat.CombinationText(trial),
                    Variants     = SubstitutionTrialFormat.VariantsText(trial),
                    StatusText   = BuildTrialStatusText(trial),
                });
            }

            _trialRows = rows
                .OrderByDescending(r => r.Trial.Success)
                .ThenBy(r => r.Trial.Rank ?? int.MaxValue)
                .ThenBy(r => r.TrialNo)
                .ToList();
            return _trialRows;
        }
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
        if (Model.VariantMode == ProductVariantMode.Substitution)
        {
            Model.SubstitutionTargetQuantity ??= 0m;
            Model.SubstitutionToleranceType  ??= ToleranceType.Amount;
            if (Model.SubstitutionGroupId is null && SubstitutionGroups.FirstOrDefault()?.Id is { } firstGroupId)
            {
                await HandleSubstitutionGroupChangedAsync(firstGroupId);
            }
        }

        EditChanged?.Invoke();
        StateHasChanged();
    }

    /// <summary>Tolerans türü değişti — Miktar (tam eşleşme) türünde tolerans değeri anlamsız olduğundan sıfırlanır
    /// (aksi halde Binde'den geçişte bayat değer ±miktar sapması gibi yorumlanırdı). Binde seçilince değer alanı
    /// UI'da tekrar görünür ve kullanıcı girer.</summary>
    private void OnToleranceTypeChanged(ToleranceType? toleranceType)
    {
        Model.SubstitutionToleranceType = toleranceType;
        if (toleranceType != ToleranceType.PerMille)
        {
            Model.SubstitutionToleranceValue = null;
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

    // Yeni görsel eklenince Sıra No OTOMATİK artar (max + 1; boşsa 1). Nitelik/değer sırası JENERİK panelde.
    private static int NextOrder(IEnumerable<ProductImageGraphDto> items)
    {
        return items.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
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
}
