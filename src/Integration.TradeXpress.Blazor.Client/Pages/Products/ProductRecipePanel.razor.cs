using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client;
using Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// Bir varyantın REÇETE paneli — <c>EntryPanelBase</c>'in (Framework, buffered giriş paneli tabanı) İLK türevi.
/// Süreç paneli davranışı (MetalProcessPanel paritesi): toolbar tip seçtirir → kırmızı-gradyan başlıklı POPUP
/// (<c>EntryPanelShell Popup</c>) açılır; DRAFT üzerinde çalışılır (satıra ANLIK yazılmaz). Kaydet draft'ı uygular
/// &amp; POPUP'ı KAPATIR (footer "Tüm varyantlara uygula" checkbox'ı açıksa tüm varyantlara upsert); Geri draft'ı atar. Ters-hesaplar
/// MetalProcessPanel.Recalc paritesi: Total→Factor, PayTotal→PayFactor geri-hesap. Bu sınıf yalnız TradeXpress'e
/// özgü kısmı taşır (emtia aileleri + Recalc + grid); yaşam döngüsü + chrome Framework tabanında (§4: generic → Framework).
/// </summary>
public partial class ProductRecipePanel
{
    [Parameter, EditorRequired] public List<ProductRecipeLineGraphDto> Lines { get; set; } = default!;

    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();
    [Parameter] public IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; set; } = Array.Empty<MetalVariantLookupDto>();
    [Parameter] public IReadOnlyList<ScrapListDto> Scraps { get; set; } = Array.Empty<ScrapListDto>();
    [Parameter] public IReadOnlyList<FutureListDto> Futures { get; set; } = Array.Empty<FutureListDto>();
    [Parameter] public IReadOnlyList<JewelryListDto> Jewelries { get; set; } = Array.Empty<JewelryListDto>();
    [Parameter] public IReadOnlyList<StoneListDto> Stones { get; set; } = Array.Empty<StoneListDto>();
    [Parameter] public IReadOnlyList<GoodListDto> Goods { get; set; } = Array.Empty<GoodListDto>();
    /// <summary>Hizmet katalogu (etiket/kimlik için — Service entity'sine dokunulmaz).</summary>
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();
    /// <summary>Birim lookup (işçilik/bedel birimi) — CurrentPriceDto (Id + kod).</summary>
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    /// <summary>Ürünün TÜM varyantları — "Tüm Varyantlar" (ekle/sil) aksiyonları bunların reçetesine upsert/silme yapar.
    /// Verilirse draft popup'ta "Bu Varyant / Tüm Varyantlar" + silme kapsam popup'ı görünür.</summary>
    [Parameter] public IReadOnlyList<ProductVariantGraphDto>? AllVariants { get; set; }

    /// <summary>Bir varyantın reçetesi değişince (tüm-varyant işleminde her varyant için) host'un maliyet recalc'ı + dirty.</summary>
    [Parameter] public Func<ProductVariantGraphDto, Task>? OnVariantRecipeChanged { get; set; }

    /// <summary>Varyantın canlı net maliyeti (host projeksiyonu; salt görüntü).</summary>
    [Parameter] public decimal? NetCost { get; set; }
    [Parameter] public string NetCostCurrency { get; set; } = string.Empty;
    [Parameter] public bool NetCostMissingRate { get; set; }

    private bool _isMobile;

    // Grid'de seçili satır (edit/sil toolbar butonları + grid-altı açıklama bunu kullanır). Tıklama = SEÇ (edit DEĞİL).
    private ProductRecipeLineGraphDto? _selectedLine;

    // Ödeme tipi seçenekleri — reçetede YALNIZ Normal (metal + işçilik) ve Bedelli (sabit bedel). Enum'a değer eklenmez.
    private record PaymentItem(ProcessPaymentType Value, string Label);
    private List<PaymentItem> _paymentItems = new();

    // Türev/devralan satır (3b) seçenekleri.
    private record DerivedBaseItem(RecipeDerivedBaseMode Value, string Label);
    private record DerivedOperationItem(RecipeDerivedOperation Value, string Label);
    private record DerivedSourceItem(Guid ClientKey, int Nr, string Code, string Cost);
    private List<DerivedBaseItem> _baseModeItems = new();
    private List<DerivedOperationItem> _operationItems = new();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _paymentItems = new()
        {
            new(ProcessPaymentType.Normal, L["Enum:ProcessPaymentType:Normal"].Value.ToUpper()),
            new(ProcessPaymentType.WithCurrency, L["Enum:ProcessPaymentType:WithCurrency"].Value.ToUpper()),
        };
        _baseModeItems = new()
        {
            new(RecipeDerivedBaseMode.AllAbove, L["Enum:RecipeDerivedBaseMode:AllAbove"].Value),
            new(RecipeDerivedBaseMode.SelectedLines, L["Enum:RecipeDerivedBaseMode:SelectedLines"].Value),
        };
        _operationItems = new()
        {
            new(RecipeDerivedOperation.Add, L["Enum:RecipeDerivedOperation:Add"].Value),
            new(RecipeDerivedOperation.Multiply, L["Enum:RecipeDerivedOperation:Multiply"].Value),
            new(RecipeDerivedOperation.Percent, L["Enum:RecipeDerivedOperation:Percent"].Value),
            new(RecipeDerivedOperation.GrossUp, L["Enum:RecipeDerivedOperation:GrossUp"].Value),
        };
    }

    /// <summary>Grid'de gösterilen (silinmemiş) satırlar, sıra ile.</summary>
    private IEnumerable<ProductRecipeLineGraphDto> VisibleLines
    {
        get { return Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder); }
    }

    // ── EntryPanelBase sözleşmesi (buffered yaşam döngüsü Framework tabanında) ──────────────────────
    protected override IList<ProductRecipeLineGraphDto> ItemsSource
    {
        get { return Lines; }
    }

    protected override ProductRecipeLineGraphDto CloneItem(ProductRecipeLineGraphDto s)
    {
        return new ProductRecipeLineGraphDto
        {
            Id = s.Id,
            ClientKey = s.ClientKey,
            IsDeleted = s.IsDeleted,
            LineOrder = s.LineOrder,
            ComponentType = s.ComponentType,
            CommodityProcessType = s.CommodityProcessType,
            CommodityId = s.CommodityId,
            CommodityVariantId = s.CommodityVariantId,
            SideCostKind = s.SideCostKind,
            Quantity = s.Quantity,
            Amount = s.Amount,
            Factor = s.Factor,
            ValuationUnitId = s.ValuationUnitId,
            MainUnitCode = s.MainUnitCode,
            PaymentType = s.PaymentType,
            Total = s.Total,
            PayFactor = s.PayFactor,
            PayTotal = s.PayTotal,
            PayUnitId = s.PayUnitId,
            PayUnitCode = s.PayUnitCode,
            ManualAmount = s.ManualAmount,
            ManualUnitId = s.ManualUnitId,
            Description = s.Description,
            LineCost = s.LineCost,
            LineCostMissingRate = s.LineCostMissingRate,
            DerivedBaseMode = s.DerivedBaseMode,
            DerivedOperation = s.DerivedOperation,
            DerivedOperand = s.DerivedOperand,
            DerivedSourceKeys = new List<Guid>(s.DerivedSourceKeys),
        };
    }

    protected override void ApplyDraft(ProductRecipeLineGraphDto d, ProductRecipeLineGraphDto target)
    {
        // Kimlik (Id/ClientKey) hedefte kalır — grid satır kimliği korunur.
        target.LineOrder = d.LineOrder;
        target.CommodityProcessType = d.CommodityProcessType;
        target.CommodityId = d.CommodityId;
        target.CommodityVariantId = d.CommodityVariantId;
        target.Quantity = d.Quantity;
        target.Amount = d.Amount;
        target.Factor = d.Factor;
        target.ValuationUnitId = d.ValuationUnitId;
        target.MainUnitCode = d.MainUnitCode;
        target.PaymentType = d.PaymentType;
        target.Total = d.Total;
        target.PayFactor = d.PayFactor;
        target.PayTotal = d.PayTotal;
        target.PayUnitId = d.PayUnitId;
        target.PayUnitCode = d.PayUnitCode;
        target.ManualAmount = d.ManualAmount;
        target.ManualUnitId = d.ManualUnitId;
        target.Description = d.Description;
        target.DerivedBaseMode = d.DerivedBaseMode;
        target.DerivedOperation = d.DerivedOperation;
        target.DerivedOperand = d.DerivedOperand;
        target.DerivedSourceKeys = new List<Guid>(d.DerivedSourceKeys);
    }

    /// <summary>Seri giriş draft'ı — uçucu alanlar sıfır (ResetVolatileFields paritesi:
    /// Amount/Quantity/Total/PayTotal/ManualAmount/Description); tip + emtia/birim seçimleri korunur.</summary>
    protected override ProductRecipeLineGraphDto CreateNextDraft(ProductRecipeLineGraphDto saved)
    {
        var next = CloneItem(saved);
        next.Id = Guid.Empty;
        next.ClientKey = Guid.NewGuid();
        next.LineOrder = NextOrder();
        next.Quantity = 0m;
        next.Amount = 0m;
        next.Total = 0m;
        next.PayTotal = 0m;
        next.ManualAmount = 0m;
        next.Description = null;
        next.DerivedOperand = 0m;              // türev: seri girişte operand + seçim sıfırlanır (tip/mod korunur)
        next.DerivedSourceKeys = new List<Guid>();
        if (SelectedMetal(next) is { IsQuantity: true, StableQuantity: > 0m })
        {
            next.Quantity = 1m;   // adetli metalde panel default'u (Miktar kilitli, adet 1'den başlar)
        }

        RecalcDraft(next);
        return next;
    }

    // ── Panel açma (toolbar) ────────────────────────────────────────────────────────────────────────
    private void OpenCatalogDraft(ProcessType family)
    {
        OpenDraft(new ProductRecipeLineGraphDto
        {
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = family,
            LineOrder = NextOrder(),
            Factor = 1m,
        });

        // Maden + birim boş açılmasın: ailenin İLK katalog kaydını otomatik seç → seçim mantığı
        // Factor/doğal-birim/işçilik-birimini (PayUnit) da doldurur (voucher paneli ilk-değer davranışı).
        SelectFirstCommodity(family);
    }

    /// <summary>Hizmet satırı draft'ı — ilk hizmet seçili (etiket) + varsayılan taban Tüm Üst Satırlar + işlem Yüzde.
    /// Türevsel bedel kuralı satırda; hizmet yalnız kimlik/etiket (Service katalog entity'sine dokunulmaz).</summary>
    private void OpenServiceDraft()
    {
        OpenDraft(new ProductRecipeLineGraphDto
        {
            ComponentType = RecipeComponentType.Service,
            LineOrder = NextOrder(),
            CommodityId = Services.FirstOrDefault()?.Id,
            DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = 0m,
        });
    }

    private void OnServiceSelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityId = id;
    }

    /// <summary>Draft açılışında ailenin ilk katalog kaydını seçer (boş combo bırakmaz); seçim handler'ı
    /// cascade default'ları (Factor/doğal birim/işçilik birimi) kurar. Liste boşsa no-op.</summary>
    private void SelectFirstCommodity(ProcessType family)
    {
        switch (family)
        {
            case ProcessType.Metal:
                OnMetalSelected(MetalVariants.FirstOrDefault()?.VariantId);
                break;
            case ProcessType.Scrap:
                OnScrapSelected(Scraps.FirstOrDefault()?.Id);
                break;
            case ProcessType.Future:
                OnFutureSelected(Futures.FirstOrDefault()?.Id);
                break;
            case ProcessType.Jewelry:
                OnJewelrySelected(Jewelries.FirstOrDefault()?.Id);
                break;
            case ProcessType.Stone:
                OnStoneSelected(Stones.FirstOrDefault()?.Id);
                break;
            case ProcessType.Good:
                OnGoodSelected(Goods.FirstOrDefault()?.Id);
                break;
        }
    }

    /// <summary>Grid seçimi değişti — satırı SEÇ (edit DEĞİL; düzenleme toolbar'daki Düzenle ile).</summary>
    private void OnSelectedLineChanged(object? item)
    {
        _selectedLine = item as ProductRecipeLineGraphDto;
    }

    /// <summary>Toolbar Düzenle — seçili satırın KOPYASI draft olur (buffered; orijinal Kaydet'e dek değişmez).</summary>
    private void EditSelectedLine()
    {
        if (_selectedLine is { } line)
        {
            BeginEdit(line);
        }
    }

    // Tüm-varyant desteği aktif mi (host TÜM varyantları + recalc callback'ini verdi).
    private bool HasAllVariants
    {
        get { return AllVariants is { Count: > 0 } && OnVariantRecipeChanged is not null; }
    }

    // "Tüm varyantlara uygula" checkbox'ı — YALNIZ yeni satır EKLERKEN görünür (düzenlemede gizli). AÇIK: Kaydet
    // satırı TÜM varyantlara ekler; KAPALI: yalnız bu varyant. Düzenleme + silme HER ZAMAN yalnız bu varyant
    // (kazara toplu ezme/silme riski — bilinçli olarak tüm-varyanta yalnız yeni ekleme için izin verilir).
    private bool _applyToAllVariants;

    /// <summary>Kaydet (dispatch) — YENİ satırda checkbox açıksa TÜM varyantlara ekler; DÜZENLEMEDE daima yalnız bu
    /// varyant. Her durumda paneli KAPATIR (seri-giriş yok — Kaydet = uygula &amp; kapat).</summary>
    private async Task SaveDraftDispatchAsync()
    {
        if (_applyToAllVariants && HasAllVariants && EditingItem is null)
        {
            await SaveDraftToAllVariantsAsync();
        }
        else
        {
            await SaveDraftAndCloseAsync();
        }
    }

    /// <summary>Toolbar Sil — seçili satırı YALNIZ bu varyanttan siler (tüm-varyant silme YOK — kazara toplu silme riski).</summary>
    private async Task DeleteSelectedLineAsync()
    {
        if (_selectedLine is { } line)
        {
            await DeleteLineAsync(line);
            _selectedLine = null;
        }
    }

    /// <summary>İki reçete satırı AYNI emtia/hizmet mi — tüm-varyant upsert/silme eşleşme ölçütü (emtia kimliği).</summary>
    private static bool IsSameCommodityLine(ProductRecipeLineGraphDto a, ProductRecipeLineGraphDto b)
    {
        return a.ComponentType == b.ComponentType
            && a.CommodityProcessType == b.CommodityProcessType
            && a.CommodityId == b.CommodityId
            && a.CommodityVariantId == b.CommodityVariantId
            && a.SideCostKind == b.SideCostKind;
    }

    /// <summary>Checkbox açıkken Kaydet — draft'ı HER varyantın reçetesine upsert eder (aynı emtia varsa değerleri
    /// günceller, yoksa kopya ekler) + her varyantı recalc. Sonra paneli KAPATIR.</summary>
    private async Task SaveDraftToAllVariantsAsync()
    {
        if (Draft is not { } draft || AllVariants is null)
        {
            return;
        }

        foreach (var variant in AllVariants.Where(x => !x.IsDeleted))
        {
            var match = variant.RecipeLines.FirstOrDefault(l => !l.IsDeleted && IsSameCommodityLine(l, draft));
            if (match != null)
            {
                ApplyDraft(draft, match);   // aynı emtia zaten var → değerleri güncelle
            }
            else
            {
                var copy = CloneItem(draft);
                copy.Id = Guid.Empty;
                copy.ClientKey = Guid.NewGuid();   // client key (view-model) — entity id değil
                copy.LineOrder = variant.RecipeLines.Where(x => !x.IsDeleted).Select(x => x.LineOrder).DefaultIfEmpty(0).Max() + 1;
                variant.RecipeLines.Add(copy);
            }

            if (OnVariantRecipeChanged is not null)
            {
                await OnVariantRecipeChanged(variant);   // her varyant: recalc + dirty
            }
        }

        CloseDraft();   // Kaydet = uygula & kapat (seri-giriş yok)
    }

    private async Task DeleteLineAsync(ProductRecipeLineGraphDto line)
    {
        if (line.Id == Guid.Empty)
        {
            Lines.Remove(line);       // henüz DB'de yok → listeden çıkar
        }
        else
        {
            line.IsDeleted = true;    // DB'de var → graf-save siler (Id + IsDeleted diff)
        }

        await NotifyItemRemovedAsync(line);   // düzenleniyorduysa draft da atılır + form dirty
    }

    private int NextOrder()
    {
        return Lines.Where(x => !x.IsDeleted).Select(x => x.LineOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // ── Recalc (MetalProcessPanel.Recalc paritesi — draft üzerinde, ileri yön) ──────────────────────
    // Total = Amount × Factor (adetli metalde Amount = Quantity × StableQuantity türetili).
    // Normal: PayTotal = PayFactor × (işçilik adet-bazlıysa Quantity, değilse Amount).
    // Bedelli: PayTotal = Total × PayFactor.
    private void RecalcDraft(ProductRecipeLineGraphDto d)
    {
        if (SelectedMetal(d) is { IsQuantity: true, StableQuantity: > 0m } m)
        {
            d.Amount = d.Quantity * m.StableQuantity;
        }

        d.Total = d.Amount * d.Factor;

        if (d.PaymentType == ProcessPaymentType.WithCurrency)
        {
            d.PayTotal = d.Total * d.PayFactor;
        }
        else
        {
            d.PayTotal = d.PayFactor * (LaborByQuantity(d) ? d.Quantity : d.Amount);
        }
    }

    // ── Alan değişimleri (draft; ileri hesap) ───────────────────────────────────────────────────────
    private void OnQuantityChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Quantity = value;
        RecalcDraft(d);
    }

    private void OnAmountChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Amount = value;
        RecalcDraft(d);
    }

    private void OnFactorChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Factor = value;
        RecalcDraft(d);
    }

    /// <summary>Total elle düzenlendi → Factor GERİ-hesap (panel OnTotalChanged paritesi: Factor = Total / Amount).</summary>
    private void OnTotalChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Total = value;
        if (d.Amount != 0m)
        {
            d.Factor = value / d.Amount;
        }

        RecalcDraft(d);   // Total = Amount × yeni Factor = girilen değer; pay bacağı tazelenir
    }

    private void OnPayFactorChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.PayFactor = value;
        RecalcDraft(d);
    }

    /// <summary>PayTotal elle düzenlendi → PayFactor GERİ-hesap (panel Recalc EditedField.PayTotal paritesi):
    /// Normal → PayFactor = PayTotal / (adet|miktar); Bedelli → PayFactor = PayTotal / Total.
    /// Girilen PayTotal KORUNUR (geri-hesap yönü — ileri hesapla ezilmez).</summary>
    private void OnPayTotalChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.PayTotal = value;

        if (d.PaymentType == ProcessPaymentType.WithCurrency)
        {
            if (d.Total != 0m)
            {
                d.PayFactor = value / d.Total;
            }

            return;
        }

        var basis = LaborByQuantity(d) ? d.Quantity : d.Amount;
        if (basis != 0m)
        {
            d.PayFactor = value / basis;
        }
    }

    private void OnPaymentTypeChanged(ProcessPaymentType value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.PaymentType = value;

        // Bedelli→Normal geçişinde madenin işçilik default'u geri yüklenir (panel paritesi); Normal→Bedelli'de
        // bedel kullanıcı girer → PayFactor sıfırlanır (işçilik rate'i bedel sanılmasın).
        if (value == ProcessPaymentType.Normal && SelectedMetal(d) is { } mv)
        {
            d.PayFactor = mv.EntryLabor;
            var mFu = Metals.FirstOrDefault(x => x.Id == mv.CommodityId);
            d.PayUnitId = mv.EntryLaborUnitId ?? mFu?.FollowingUnitId;
        }
        else if (value == ProcessPaymentType.WithCurrency)
        {
            d.PayFactor = 0m;
        }

        RecalcDraft(d);
    }

    private void OnPayUnitChanged(Guid? value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.PayUnitId = value;
    }

    private void OnDescriptionChanged(string? value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Description = value;
    }

    // ── türev/devralan satır alan değişimleri ───────────────────────────────────────────────────────
    private void OnDerivedBaseModeChanged(RecipeDerivedBaseMode value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.DerivedBaseMode = value;
        if (value == RecipeDerivedBaseMode.AllAbove)
        {
            d.DerivedSourceKeys = new List<Guid>();   // devreden taban → seçim anlamsız, temizle
        }
    }

    private void OnDerivedOperationChanged(RecipeDerivedOperation value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.DerivedOperation = value;
    }

    private void OnDerivedOperandChanged(decimal value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.DerivedOperand = value;
    }

    private void OnDerivedSourcesChanged(IEnumerable<Guid> value)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.DerivedSourceKeys = value.ToList();
    }

    // ── Katalog seçimi → draft default'ları (OnMetalChanged paritesi) ───────────────────────────────
    private void OnMetalSelected(Guid? variantId)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityVariantId = variantId;
        var mv = variantId is { } gid ? MetalVariants.FirstOrDefault(x => x.VariantId == gid) : null;
        if (mv != null)
        {
            d.CommodityId = mv.CommodityId;
            var m = Metals.FirstOrDefault(x => x.Id == mv.CommodityId);
            if (m != null)
            {
                d.Factor = m.Factor;
                d.ValuationUnitId = m.FollowingUnitId;
                if (m is { IsQuantity: true, StableQuantity: > 0m } && d.Quantity == 0m)
                {
                    d.Quantity = 1m;
                    d.Amount = m.StableQuantity;
                }

                d.PaymentType = ProcessPaymentType.Normal;
                d.PayFactor = mv.EntryLabor;
                d.PayUnitId = mv.EntryLaborUnitId ?? m.FollowingUnitId;
            }
        }
        else
        {
            d.CommodityId = null;
        }

        RecalcDraft(d);
    }

    private void OnScrapSelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityId = id;
        var s = id is { } gid ? Scraps.FirstOrDefault(x => x.Id == gid) : null;
        if (s != null)
        {
            d.Factor = s.Factor;
            d.ValuationUnitId = s.FollowingUnitId;
        }

        RecalcDraft(d);
    }

    private void OnFutureSelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityId = id;
        var f = id is { } gid ? Futures.FirstOrDefault(x => x.Id == gid) : null;
        if (f != null)
        {
            d.Factor = f.FollowingFactor;
            d.ValuationUnitId = f.FollowingUnitId;
        }

        RecalcDraft(d);
    }

    private void OnJewelrySelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityId = id;
        var j = id is { } gid ? Jewelries.FirstOrDefault(x => x.Id == gid) : null;
        if (j != null)
        {
            d.ValuationUnitId = j.EntryPriceUnitId;
        }
    }

    private void OnStoneSelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CommodityId = id;
        var s = id is { } gid ? Stones.FirstOrDefault(x => x.Id == gid) : null;
        if (s != null)
        {
            d.ValuationUnitId = s.EntryPriceUnitId;
        }
    }

    private void OnGoodSelected(Guid? id)
    {
        if (Draft is not { } d)
        {
            return;
        }

        // Good parasal (Jewelry/Stone deseni): değerleme birimi mamülün giriş-fiyat birimi (ana varyanttan resolver'la
        // GoodListDto'ya doldurulmuş EntryPriceUnitId). Sunucu maliyet motoru fiyatı yine ana varyanttan çözer.
        d.CommodityId = id;
        var g = id is { } gid ? Goods.FirstOrDefault(x => x.Id == gid) : null;
        if (g != null)
        {
            d.ValuationUnitId = g.EntryPriceUnitId;
        }
    }

    // ── Görünüm/durum yardımcıları ──────────────────────────────────────────────────────────────────
    private MetalVariantLookupDto? SelectedMetal(ProductRecipeLineGraphDto l)
    {
        return l is { CommodityProcessType: ProcessType.Metal, CommodityVariantId: { } vid }
            ? MetalVariants.FirstOrDefault(x => x.VariantId == vid)
            : null;
    }

    /// <summary>Metal-bacaklı aile mi (Metal/Scrap/Future) — parasal (Jewelry/Stone) DEĞİL.</summary>
    private static bool IsMetalLegged(ProcessType? family)
    {
        return family is ProcessType.Metal or ProcessType.Scrap or ProcessType.Future;
    }

    /// <summary>Adet alanı gösterilir mi — panel _showAdet paritesi (adetli VEYA işçilik adet-bazlı).</summary>
    private bool ShowAdet(ProductRecipeLineGraphDto l)
    {
        return SelectedMetal(l) is { } m && (m.IsQuantity || m.LaborType == MetalLaborType.Quantity);
    }

    /// <summary>Miktar (gram) read-only mi — metal adetli + StableQuantity>0 ise adetten türetilir.</summary>
    private bool AmountReadOnly(ProductRecipeLineGraphDto l)
    {
        return SelectedMetal(l) is { IsQuantity: true, StableQuantity: > 0m };
    }

    /// <summary>Parasal emtia (Jewelry/Stone) fiyatı adet başına mı (aksi gram başına).</summary>
    private bool PriceByQuantity(ProductRecipeLineGraphDto l)
    {
        if (l.CommodityId is not { } id)
        {
            return false;
        }

        return l.CommodityProcessType switch
        {
            ProcessType.Jewelry => Jewelries.FirstOrDefault(x => x.Id == id)?.PriceByQuantity ?? false,
            ProcessType.Stone => Stones.FirstOrDefault(x => x.Id == id)?.PriceByQuantity ?? false,
            ProcessType.Good => Goods.FirstOrDefault(x => x.Id == id)?.PriceByQuantity ?? false,
            _ => false,
        };
    }

    /// <summary>Metal işçiliği adet-bazlı mı (Metal.LaborType==Quantity) — Scrap/Future'da miktar-bazlı.</summary>
    private bool LaborByQuantity(ProductRecipeLineGraphDto l)
    {
        return SelectedMetal(l) is { LaborType: MetalLaborType.Quantity };
    }

    /// <summary>İşçilik caption'ı — maden seçiliyse türünü parantezde belirtir: "İşçilik (Adet)" / "İşçilik (Miktar)"
    /// (Metal.LaborType). Maden seçili değilse (scrap/future) sade "İşçilik".</summary>
    private string LaborCaption(ProductRecipeLineGraphDto l)
    {
        if (SelectedMetal(l) is not { } m)
        {
            return L["Labor"].Value;
        }

        var typeKey = m.LaborType == MetalLaborType.Quantity
            ? "Enum:MetalLaborType:Quantity"
            : "Enum:MetalLaborType:Amount";
        return $"{L["Labor"].Value} ({L[typeKey].Value})";
    }

    private string ComponentLabel(ProductRecipeLineGraphDto l)
    {
        // Yan-maliyet (otomatik) satırı: tür etiketi gösterilir (Paketleme/Kargo/Komisyon...) — kanal gider
        // ayarlarından üretildiği grid'de ilk bakışta anlaşılır (kullanıcı satırından ayırt).
        if (l.SideCostKind is { } kind)
        {
            return SideCostKindLabel(kind);
        }

        return l.ComponentType switch
        {
            RecipeComponentType.Service => L["Service"].Value,
            _ => l.CommodityProcessType switch
            {
                ProcessType.Metal => L["Metal"].Value,
                ProcessType.Scrap => L["Scrap"].Value,
                ProcessType.Future => L["Future"].Value,
                ProcessType.Jewelry => L["Jewelry"].Value,
                ProcessType.Stone => L["Stone"].Value,
                ProcessType.Good => L["Good"].Value,
                _ => L["ComponentType"].Value,
            },
        };
    }

    /// <summary>Yan-maliyet türünün lokalize etiketi (kanal gider ayarlarından otomatik üretilen satırlar).</summary>
    private string SideCostKindLabel(SideCostKind kind)
    {
        return kind switch
        {
            SideCostKind.Packaging => L["SideCost:Kind:Packaging"].Value,
            SideCostKind.Cargo => L["SideCost:Kind:Cargo"].Value,
            SideCostKind.InsuredShipping => L["SideCost:Kind:InsuredShipping"].Value,
            SideCostKind.Commission => L["SideCost:Kind:Commission"].Value,
            SideCostKind.ChannelFixed => L["SideCost:Kind:ChannelFixed"].Value,
            _ => L["Service"].Value,
        };
    }

    // ── türev/devralan satır görünüm yardımcıları ───────────────────────────────────────────────────

    /// <summary>SelectedLines TagBox seçenekleri — YALNIZ kendinden önceki (küçük LineOrder) silinmemiş satırlar
    /// (döngüsüzlük UI hattı; kendini/sonrasını referanslayamaz).</summary>
    private IEnumerable<DerivedSourceItem> UpstreamLines(ProductRecipeLineGraphDto d)
    {
        return Lines
            .Where(x => !x.IsDeleted && x.ClientKey != d.ClientKey && x.LineOrder < d.LineOrder)
            .OrderBy(x => x.LineOrder)
            .Select(x => new DerivedSourceItem(x.ClientKey, x.LineOrder, ShortCodeOf(x), LineCostText(x)));
    }

    /// <summary>Satırın KISA kodu (tag + dropdown kolonu için) — Hizmet: hizmet kodu; fiziki: emtia kodu.</summary>
    private string ShortCodeOf(ProductRecipeLineGraphDto l)
    {
        return l.ComponentType == RecipeComponentType.Service ? ServiceCodeOf(l) : CommodityCodeOf(l);
    }

    /// <summary>Seçili hizmetin kodu (katalog etiketi) — seçili değilse boş.</summary>
    private string ServiceCodeOf(ProductRecipeLineGraphDto l)
    {
        return l.CommodityId is { } id ? Services.FirstOrDefault(s => s.Id == id)?.Code ?? string.Empty : string.Empty;
    }

    /// <summary>Devralınan kolonu — Hizmet satırının taban modu (Tüm Üst Satırlar / Belli Kalemler); fiziki: boş.</summary>
    private string InheritedBaseLabel(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType != RecipeComponentType.Service)
        {
            return string.Empty;
        }

        return _baseModeItems.FirstOrDefault(x => x.Value == l.DerivedBaseMode)?.Label ?? string.Empty;
    }

    /// <summary>Kalemler kolonu — SelectedLines modunda seçili üst satırların kodları (", " ile birleştirilmiş);
    /// AllAbove ve fiziki satırda boş.</summary>
    private string InheritedItemsText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType != RecipeComponentType.Service || l.DerivedBaseMode != RecipeDerivedBaseMode.SelectedLines)
        {
            return string.Empty;
        }

        var codes = l.DerivedSourceKeys
            .Select(k => Lines.FirstOrDefault(x => x.ClientKey == k))
            .Where(x => x is not null)
            .Select(x => ShortCodeOf(x!))
            .Where(c => !string.IsNullOrEmpty(c));
        return string.Join(", ", codes);
    }

    /// <summary>Hizmet satırlarına görsel işaret (italik + mavi); YAN-MALİYET (otomatik) satırları TEAL — kanal
    /// gider ayarlarından üretildiğini gösterir (mevcut hücre stili deseni). Kullanıcı otomatik satırı silerse
    /// kendiliğinden GERİ GELMEZ — varyant sekmesindeki "Giderleri Yeniden Uygula" ile ayarlardan tazelenir.</summary>
    private static string DerivedCellStyle(ProductRecipeLineGraphDto l)
    {
        if (l.SideCostKind is not null)
        {
            return "font-style:italic; color:#0d9488;";
        }

        return l.ComponentType == RecipeComponentType.Service ? "font-style:italic; color:#2563eb;" : string.Empty;
    }

    /// <summary>Panel şerit başlığı — süreç paneli StripText paritesi: tip adı (+ ödeme tipi, metal-bacaklıda).</summary>
    private string EditorTitle(ProductRecipeLineGraphDto d)
    {
        var title = ComponentLabel(d);
        if (d.ComponentType == RecipeComponentType.CatalogCommodity && IsMetalLegged(d.CommodityProcessType))
        {
            var payment = _paymentItems.FirstOrDefault(p => p.Value == d.PaymentType)?.Label;
            if (!string.IsNullOrEmpty(payment))
            {
                title += $"   {payment}";
            }
        }

        return title;
    }

    // ── Grid hücre metinleri ────────────────────────────────────────────────────────────────────────
    private string CommodityCodeOf(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            return ServiceCodeOf(l);   // Emtia kolonu = yalnız hizmet kodu (taban/kalemler ayrı kolonlarda)
        }

        if (l.CommodityId is not { } id)
        {
            return string.Empty;
        }

        return l.CommodityProcessType switch
        {
            ProcessType.Metal => l.CommodityVariantId is { } vid
                ? MetalVariants.FirstOrDefault(x => x.VariantId == vid)?.DisplayText ?? string.Empty
                : (l.CommodityId is { } cid ? Metals.FirstOrDefault(x => x.Id == cid)?.Code ?? string.Empty : string.Empty),
            ProcessType.Scrap => Scraps.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty,
            ProcessType.Future => Futures.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty,
            ProcessType.Jewelry => Jewelries.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty,
            ProcessType.Stone => Stones.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty,
            ProcessType.Good => Goods.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty,
            _ => string.Empty,
        };
    }

    private string PaymentLabelOf(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            return L["Enum:ProcessPaymentType:Normal"].Value;   // Hizmet satırı görünümde Normal
        }

        if (l.ComponentType != RecipeComponentType.CatalogCommodity || !IsMetalLegged(l.CommodityProcessType))
        {
            return string.Empty;
        }

        return L[$"Enum:ProcessPaymentType:{l.PaymentType}"].Value;
    }

    /// <summary>Miktar (Amount) kolonu — fiziki: l.Amount; Hizmet: devralınan taban (Uygulanacak Bedel).</summary>
    /// <summary>Adet kolonu — hizmet satırında BOŞ: hizmetin adedi yoktur, alana doğrudan bağlıyken "0"
    /// basıyor ve satır "0 adet kargo" gibi okunuyordu. Fiziki satırda adet olduğu gibi.</summary>
    private static string GridQuantityText(ProductRecipeLineGraphDto l)
    {
        return l.ComponentType == RecipeComponentType.Service ? string.Empty : l.Quantity.ToString("N0");
    }

    private string GridAmountText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            // Hizmet satırında Tutar/Toplam BOŞ (2026-07-28 Hakan): bu kolonlar fiziki satırın miktar×milyem
            // dünyasına ait. Buraya işlemin TABANI basılınca 70 TL'lik kargo "49 bin TL" gibi, %oranlı sigorta
            // da tabanı tutarıymış gibi okunuyordu. Hizmetin bedeli PayFactor/PayTotal tarafında durur —
            // fişteki hizmet satırı deseniyle hizalı. Taban zaten "Uygulanacak Bedel" kolonunda görünür.
            return string.Empty;
        }

        return l.Amount.ToString("N2");
    }

    /// <summary>Değer (Factor) kolonu — fiziki: milyem (l.Factor); Hizmet: işlem operand'ı (değer).</summary>
    private string GridFactorText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            // Operand ARTIK Fiyat kolonunda (mutlakta tutar, oransalda %): fiziki satırın "milyem" çarpanıyla
            // aynı kolonda durması, 70,00000 gibi bir bedeli çarpan sanmaya yol açıyordu.
            return string.Empty;
        }

        return l.Factor.ToString("N5");
    }

    /// <summary>İşlem yüzde-bazlı mı (Yüzde / Brütleştir) — oran %olarak Fiyat kolonunda gösterilir.</summary>
    private static bool IsPercentOperation(RecipeDerivedOperation? op)
    {
        return op is RecipeDerivedOperation.Percent or RecipeDerivedOperation.GrossUp;
    }

    /// <summary>İşlem Tipi kolonu — Hizmet: işlem adı (Ekle/Çarp/Yüzde/Brütleştir); fiziki: boş.</summary>
    private string OperationLabel(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType != RecipeComponentType.Service)
        {
            return string.Empty;
        }

        return _operationItems.FirstOrDefault(x => x.Value == l.DerivedOperation)?.Label ?? string.Empty;
    }

    /// <summary>Fiyat kolonu (eski İşçilik) — fiziki metal: işçilik rate (PayFactor); Hizmet: boş.</summary>
    private string GridPriceText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            // Yüzde/Brütleştir → oran ("%5,1"); Ekle → mutlak bedel + birimi ("70,00 TL"); Çarp → çarpan.
            if (IsPercentOperation(l.DerivedOperation))
            {
                return $"%{l.DerivedOperand:0.##}";
            }

            return l.DerivedOperation == RecipeDerivedOperation.Add
                ? $"{l.DerivedOperand:N2} {PayUnitCodeOf(l)}".TrimEnd()
                : l.DerivedOperand.ToString("N5");
        }

        if (l.ComponentType != RecipeComponentType.CatalogCommodity || !IsMetalLegged(l.CommodityProcessType))
        {
            return string.Empty;
        }

        return l.PayFactor.ToString("N5");
    }

    /// <summary>Ana bacak toplamı (doğal birimde) = Amount × Factor.</summary>
    private static decimal TotalOf(ProductRecipeLineGraphDto l)
    {
        return l.Amount * l.Factor;
    }

    /// <summary>Karşı bacak toplamı — Normal: işçilik = PayFactor × (adet|miktar); Bedelli: Total × PayFactor.</summary>
    private decimal PayTotalOf(ProductRecipeLineGraphDto l)
    {
        if (l.PaymentType == ProcessPaymentType.WithCurrency)
        {
            return TotalOf(l) * l.PayFactor;
        }

        return l.PayFactor * (LaborByQuantity(l) ? l.Quantity : l.Amount);
    }

    /// <summary>Grid "Toplam" metni — aileye göre: metal-bacaklı Total@ana-birim; parasal EntryPrice-tabanlı
    /// tutar@fiyat-birimi; manuel/hizmet ManualAmount@birim.</summary>
    private string GridTotalText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            // Hizmette Toplam BOŞ — gerekçe GridAmountText'te (bedel PayTotal'de, taban Uygulanacak Bedel'de).
            return string.Empty;
        }

        if (IsMetalLegged(l.CommodityProcessType))
        {
            return $"{TotalOf(l):N5} {MainUnitCodeOf(l)}".TrimEnd();
        }

        var entryPrice = l.CommodityId is { } id
            ? l.CommodityProcessType switch
            {
                ProcessType.Jewelry => Jewelries.FirstOrDefault(x => x.Id == id)?.EntryPrice ?? 0m,
                ProcessType.Stone => Stones.FirstOrDefault(x => x.Id == id)?.EntryPrice ?? 0m,
                ProcessType.Good => Goods.FirstOrDefault(x => x.Id == id)?.EntryPrice ?? 0m,
                _ => 0m,
            }
            : 0m;
        var monetaryTotal = entryPrice * (PriceByQuantity(l) ? l.Quantity : l.Amount);
        return $"{monetaryTotal:N2} {UnitCodeOf(l.ValuationUnitId)}".TrimEnd();
    }

    private string GridPayTotalText(ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.Service)
        {
            return LineCostText(l);   // Hizmet: PayTotal kolonu = Satır Maliyeti (uygulanan bedel/fee)
        }

        if (l.ComponentType != RecipeComponentType.CatalogCommodity || !IsMetalLegged(l.CommodityProcessType))
        {
            return string.Empty;
        }

        return $"{PayTotalOf(l):N2} {PayUnitCodeOf(l)}".TrimEnd();
    }

    /// <summary>Ana birimin kodu — seçili katalogtan canlı (FollowingUnitCode); yoksa projeksiyondan.</summary>
    private string MainUnitCodeOf(ProductRecipeLineGraphDto l)
    {
        var live = l.CommodityProcessType switch
        {
            ProcessType.Metal => SelectedMetal(l) is { } mv ? Metals.FirstOrDefault(x => x.Id == mv.CommodityId)?.FollowingUnitCode : null,
            ProcessType.Scrap => l.CommodityId is { } sid ? Scraps.FirstOrDefault(x => x.Id == sid)?.FollowingUnitCode : null,
            ProcessType.Future => l.CommodityId is { } fid ? Futures.FirstOrDefault(x => x.Id == fid)?.FollowingUnitCode : null,
            _ => null,
        };
        return live ?? l.MainUnitCode;
    }

    /// <summary>Karşı bacak biriminin kodu — Units lookup'tan canlı; yoksa projeksiyondan.</summary>
    private string PayUnitCodeOf(ProductRecipeLineGraphDto l)
    {
        return UnitCodeOf(l.PayUnitId, l.PayUnitCode);
    }

    private string UnitCodeOf(Guid? unitId, string fallback = "")
    {
        if (unitId is { } id)
        {
            var live = Units.FirstOrDefault(u => u.Id == id)?.CurrencyUnitCode;
            if (!string.IsNullOrEmpty(live))
            {
                return live;
            }
        }

        return fallback;
    }

    private string LineCostText(ProductRecipeLineGraphDto l)
    {
        if (l.LineCostMissingRate)
        {
            return L["MissingRate"].Value;
        }

        return l.LineCost is { } cost ? $"{cost:N2} {NetCostCurrency}" : string.Empty;
    }

    /// <summary>Ara Toplam — o satır dahil koşan toplam (ülke birimi).</summary>
    private string RunningSubtotalText(ProductRecipeLineGraphDto l)
    {
        return l.RunningSubtotal is { } s ? $"{s:N2} {NetCostCurrency}" : string.Empty;
    }

    // Ortak panel stilleri (ProcessPanelStyles SSOT — süreç panelleriyle AYNI görünüm).
    private string GroupStyle()
    {
        return ProcessPanelStyles.Group(_isMobile);
    }

    private string GroupStyle(int w)
    {
        return ProcessPanelStyles.Group(_isMobile, w);
    }

    /// <summary>Reçete draft popup başlık ikonu (standart header icon+caption) — bileşen ailesine göre.</summary>
    private string EditorIcon(ProductRecipeLineGraphDto l)
    {
        if (l.SideCostKind is not null)
        {
            return TradeXpressIcons.Service;
        }

        return l.ComponentType switch
        {
            RecipeComponentType.Service => TradeXpressIcons.Service,
            _ => l.CommodityProcessType switch
            {
                ProcessType.Metal => TradeXpressIcons.Metal,
                ProcessType.Scrap => TradeXpressIcons.Scrap,
                ProcessType.Future => TradeXpressIcons.Future,
                ProcessType.Jewelry => TradeXpressIcons.Jewelry,
                ProcessType.Stone => TradeXpressIcons.Stone,
                ProcessType.Good => TradeXpressIcons.Good,
                _ => TradeXpressIcons.ProductVariant,
            },
        };
    }

    // ── ISplitEditActions — reçete draft popup'ı STANDART EditToolbar'ı kullansın (edit formlarıyla AYNI). Görünür:
    //    Kaydet + Reset (Kaydet&Yeni/Sil/nav/undo GİZLİ). Sil AYRI (grid toolbar + onay dialogu). ──
    bool ISplitEditActions.CanSave => Draft is not null;
    bool ISplitEditActions.IsNew => EditingItem is null;
    bool ISplitEditActions.IsReadOnly => false;
    string? ISplitEditActions.ReadOnlyNotice => null;

    async Task ISplitEditActions.SaveAsync()
    {
        await SaveDraftDispatchAsync();
        StateHasChanged();
    }

    Task ISplitEditActions.SaveAndNewAsync() => Task.CompletedTask;

    async Task ISplitEditActions.SaveAndCloseAsync()
    {
        await SaveDraftDispatchAsync();
        StateHasChanged();
    }

    bool ISplitEditActions.SupportsSaveAndNew => false;
    bool ISplitEditActions.SupportsDelete => false;
    bool ISplitEditActions.CanDelete => false;
    Task ISplitEditActions.DeleteAsync() => Task.CompletedTask;

    bool ISplitEditActions.CanGoPrevious => false;
    bool ISplitEditActions.CanGoNext => false;
    Task ISplitEditActions.GoPreviousAsync() => Task.CompletedTask;
    Task ISplitEditActions.GoNextAsync() => Task.CompletedTask;
    bool ISplitEditActions.SupportsRecordNavigation => false;

    Task<bool> ISplitEditActions.CanLeaveAsync() => Task.FromResult(true);

    Task ISplitEditActions.ResetAsync()
    {
        ResetDraft();
        StateHasChanged();
        return Task.CompletedTask;
    }

    bool ISplitEditActions.CanUndo => false;
    bool ISplitEditActions.CanRedo => false;
    Task ISplitEditActions.UndoAsync() => Task.CompletedTask;
    Task ISplitEditActions.RedoAsync() => Task.CompletedTask;
    bool ISplitEditActions.SupportsUndoRedo => false;

    IReadOnlyList<CrudToolbarAction>? ISplitEditActions.CustomActions => null;

    void ISplitEditActions.NotifyInput() { }
    void ISplitEditActions.CommitUndoStep() { }
}
