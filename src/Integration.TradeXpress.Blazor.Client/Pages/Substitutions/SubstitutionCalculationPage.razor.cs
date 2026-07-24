using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Substitutions;

public partial class SubstitutionCalculationPage : IDisposable
{
    public SubstitutionCalculationPage()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Inject] protected ISubstitutionGroupAppService SubstitutionGroupAppService { get; set; } = default!;
    [Inject] protected ISubstitutionCalculationAppService SubstitutionCalculationAppService { get; set; } = default!;
    [Inject] protected ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] protected ISalesChannelTrN11ProductAppService N11ProductAppService { get; set; } = default!;
    [Inject] protected ISalesChannelTrTrendyolProductAppService TrendyolProductAppService { get; set; } = default!;
    [Inject] protected IProductAppService ProductAppService { get; set; } = default!;
    [Inject] protected IWorkingContextService Working { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IServiceProvider ServiceProvider { get; set; } = default!;

    private List<SubstitutionGroupListDto> _groups = new();
    private Guid? _groupId;
    private decimal _targetQuantity;
    private int _topN = SubstitutionCalculationConsts.DefaultTopN;

    private bool _busy;
    private SubstitutionCalculationResultDto? _result;
    private List<TrialRow> _rows = new();

    // ── Kanala uygulama (M4 köprüsü UI'ı) — kanal ürünü adayları + seçim + meşguliyet ──
    private List<ChannelProductOption> _channelProductOptions = new();
    private Guid? _selectedChannelProductId;
    private bool _applyBusy;

    protected override async Task OnInitializedAsync()
    {
        Working.Changed += OnWorkingChanged;
        await Working.EnsureLoadedAsync();
        await LoadGroupsAsync();
        await LoadChannelProductOptionsAsync();
    }

    // Çalışma şirketi değişince grup adayları + kanal ürünleri + eski sonuç yeni şirkete göre tazelenir.
    private void OnWorkingChanged()
    {
        _ = InvokeAsync(async () =>
        {
            _groupId = null;
            _result = null;
            _rows = new List<TrialRow>();
            _selectedChannelProductId = null;
            await LoadGroupsAsync();
            await LoadChannelProductOptionsAsync();
            StateHasChanged();
        });
    }

    // Yalnız AKTİF gruplar hesaplanabilir (pasif grup sunucuda da fail-fast) → combo aktiflerle dolar.
    private async Task LoadGroupsAsync()
    {
        var result = await SubstitutionGroupAppService.GetListAsync(
            new SubstitutionGroupListRequestDto { IsActive = true, MaxResultCount = 200 });
        _groups = result.Items.ToList();
    }

    private async Task CalculateAsync()
    {
        if (_groupId is not { } groupId || _targetQuantity <= 0m)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await SubstitutionCalculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = groupId,
                TargetQuantity      = _targetQuantity,
                TopN                = _topN > 0 ? _topN : SubstitutionCalculationConsts.DefaultTopN,
            });

            // Bayatlık kontrolü: istek uçuştayken şirket/grup değiştiyse (OnWorkingChanged _groupId'yi
            // sıfırlar) eski bağlamın sonucu ekrana YAZILMAZ — yeni şirkette eski stok tablosu görünmesin.
            if (_groupId != groupId)
            {
                return;
            }

            _result = result;
            _rows = BuildRows(result);
        }
        catch (Exception ex)
        {
            // BusinessException'ı (GroupHasNoItems, RatesMissing...) error boundary'e DÜŞÜRME:
            // in-process çağrıda mesaj lokalize gelmez → CrudErrorPresenter kodu çevirir (kardeş akış deseni).
            UiService.ShowErrorToast(
                CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["Substitution:CalculationFailed"].Value);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>N11 + Trendyol kanal ürünlerini TEK combo listesine toplar — kanal etiketi ("Ürün · Kanal (Tür)")
    /// görünür. Kanallar polymorphic listeden, kanal ürünleri her adaptörün KANAL-merkezli list ucundan;
    /// ürün kod/adı ProductAppService listesinden çözülür (drill'lerdeki mevcut desen — DTO'da ürün adı yok).</summary>
    private async Task LoadChannelProductOptionsAsync()
    {
        var options = new List<ChannelProductOption>();

        var channels = await SalesChannelAppService.GetListAsync(
            new SalesChannelListRequestDto { MaxResultCount = 200 });
        var products = await ProductAppService.GetListAsync(
            new ProductListRequestDto { MaxResultCount = 1000 });
        var productById = products.Items.ToDictionary(p => p.Id);

        foreach (var channel in channels.Items)
        {
            switch (channel.ChannelType)
            {
                case SalesChannelType.TrN11:
                    foreach (var channelProduct in await N11ProductAppService.GetListForChannelAsync(channel.Id))
                    {
                        options.Add(BuildChannelProductOption(channelProduct.Id, channelProduct.ProductId, channel, productById));
                    }

                    break;
                case SalesChannelType.TrTrendyol:
                    foreach (var channelProduct in await TrendyolProductAppService.GetListForChannelAsync(channel.Id))
                    {
                        options.Add(BuildChannelProductOption(channelProduct.Id, channelProduct.ProductId, channel, productById));
                    }

                    break;
            }
        }

        _channelProductOptions = options;
    }

    // Combo satırı: "KOD — Ürün Adı · Kanal Adı (N11)" — ürün + kanal bir bakışta ayrışır.
    private ChannelProductOption BuildChannelProductOption(
        Guid channelProductId,
        Guid productId,
        SalesChannelListDto channel,
        IReadOnlyDictionary<Guid, ProductListDto> productById)
    {
        var productLabel = productById.TryGetValue(productId, out var product)
            ? $"{product.Code} — {product.Name}"
            : productId.ToString();
        var channelTypeText = L[$"SalesChannelType:{channel.ChannelType}"].Value;

        return new ChannelProductOption
        {
            Id          = channelProductId,
            ChannelType = channel.ChannelType,
            Label       = $"{productLabel} · {channel.Name} ({channelTypeText})",
        };
    }

    /// <summary>Kanala uygulama — seçilen kanal ürününün KENDİ adaptörünün <c>ApplySubstitutionAsync</c>'ine
    /// gider (tek motor zinciri; hesap sunucuda güncel stokla YENİDEN koşulur). Pazaryerine GÖNDERMEZ —
    /// yalnız yerel kanal kaydına "Kombinasyon" özelliği + varyantları yazar (read-only pazaryeri ilkesi).</summary>
    private async Task ApplyToChannelAsync()
    {
        if (_result is not { } result || _selectedChannelProductId is not { } channelProductId)
        {
            return;
        }

        var option = _channelProductOptions.FirstOrDefault(o => o.Id == channelProductId);
        if (option == null)
        {
            return;
        }

        _applyBusy = true;
        try
        {
            var input = new SubstitutionApplyInput
            {
                SubstitutionGroupId = result.GroupId,
                TargetQuantity      = result.TargetQuantity,
                TopN                = _topN > 0 ? _topN : SubstitutionCalculationConsts.DefaultTopN,
            };

            var applied = option.ChannelType == SalesChannelType.TrN11
                ? await N11ProductAppService.ApplySubstitutionAsync(channelProductId, input)
                : await TrendyolProductAppService.ApplySubstitutionAsync(channelProductId, input);

            // Sonuç toast'ı — apply DTO'sunun taşıdığı özet: kombinasyon sayısı + yazılan varyant satırı toplamı.
            UiService.ShowSuccessToast(
                L["Substitution:ApplySuccess", applied.Items.Count, applied.Items.Sum(i => i.StockItemCount)].Value);

            // Ticari tolerans bildirimi (tolerans > 0 grupta) — push açıklamasına iliştirilecek metin, şeffaflık.
            if (!string.IsNullOrEmpty(applied.ToleranceNotice))
            {
                UiService.ShowWarningToast(applied.ToleranceNotice);
            }
        }
        catch (Exception ex)
        {
            // Hesapla ile AYNI hata yolu: BusinessException → CrudErrorPresenter kod çevirisi → toast (M5 deseni).
            UiService.ShowErrorToast(
                CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["Substitution:ApplyFailed"].Value);
        }
        finally
        {
            _applyBusy = false;
        }
    }

    // Maliyet kolonu başlığı — para birimi çözüldüyse yanına eklenir (ör. "Maliyet (TRY)").
    private string CostCaption =>
        _result is { CostCurrencyCode.Length: > 0 }
            ? $"{L["Substitution:Cost"]} ({_result.CostCurrencyCode})"
            : L["Substitution:Cost"].Value;

    // ── sonuç → tablo satırları (deneme sırası korunur; kolonlar konsept örnek tablosuyla birebir) ──
    private List<TrialRow> BuildRows(SubstitutionCalculationResultDto result)
    {
        var rows = new List<TrialRow>(result.Trials.Count);
        for (var i = 0; i < result.Trials.Count; i++)
        {
            var trial = result.Trials[i];
            rows.Add(new TrialRow
            {
                TrialNo      = i + 1,
                Combination  = SubstitutionTrialFormat.CombinationText(trial),
                Variants     = SubstitutionTrialFormat.VariantsText(trial),
                TotalWeight  = trial.TotalWeight,
                Deviation    = trial.Deviation,
                TotalCost    = trial.TotalCost,
                PieceCount   = trial.PieceCount,
                PackageCount = trial.PackageCount,
                Success      = trial.Success,
                StatusText   = BuildStatusText(trial),
                Rank         = trial.Rank,
            });
        }

        // Başarılılar üstte Rank sırasıyla (2026-07-10 kullanıcı kararı), başarısızlar altta deneme sırasıyla.
        // TrialNo orijinal deneme numarasını korur (numaralandırma izlenebilirliği bozulmaz).
        return rows
            .OrderByDescending(r => r.Success)
            .ThenBy(r => r.Rank ?? int.MaxValue)
            .ThenBy(r => r.TrialNo)
            .ToList();
    }

    // Bileşim/varyant/elenen etiketi metinleri PAYLAŞILAN biçimlendiricide (SubstitutionTrialFormat — ürün
    // Muadil sekmesiyle SSOT); lokalize durum metinleri (L gerektirir) bu sayfada kalır.
    private static string FilteredOutLabel(SubstitutionFilteredOutDto filtered)
    {
        return SubstitutionTrialFormat.FilteredOutLabel(filtered);
    }

    // Teknik başarısızlık nedeni → okunur Türkçe/İngilizce metin (Remainder:x → "Kalan {x}gr").
    private string BuildStatusText(SubstitutionTrialDto trial)
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

    // Ön-filtre teknik nedeni → okunur metin (PieceWeightExceedsTarget / NoStock).
    private string FilterReasonText(string reason)
    {
        return reason switch
        {
            SubstitutionReasonCodes.PieceWeightExceedsTarget => L["Substitution:FilterReason:PieceWeightExceedsTarget"],
            SubstitutionReasonCodes.NoStock                  => L["Substitution:FilterReason:NoStock"],
            _                                                => reason,
        };
    }

    // Satır boyama: başarılı = hafif yeşil zemin; Rank 1 = ANA kombinasyon (belirgin zemin + kalın).
    // Mevcut desen: yeni CSS dosyası YOK — DevExpress CustomizeElement ile inline stil (CrudLayout örneği).
    private void OnCustomizeRow(GridCustomizeElementEventArgs e)
    {
        if (e.ElementType != GridElementType.DataRow)
        {
            return;
        }

        if (e.Grid.GetDataItem(e.VisibleIndex) is not TrialRow row || !row.Success)
        {
            return;
        }

        e.Style = row.Rank == 1
            ? "background-color: rgba(22,163,74,0.20); font-weight: 600;"
            : "background-color: rgba(22,163,74,0.08);";
    }

    void IDisposable.Dispose()
    {
        Working.Changed -= OnWorkingChanged;
    }

    /// <summary>Kanal ürünü combo satırı — N11/Trendyol kayıtları TEK listede; ChannelType uygula
    /// çağrısını doğru adaptöre yönlendirir.</summary>
    private sealed class ChannelProductOption
    {
        public Guid Id { get; set; }
        public SalesChannelType ChannelType { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Tablo satırı görünümü — SubstitutionTrialDto'nun grid'e düzleştirilmiş hâli.</summary>
    private sealed class TrialRow
    {
        public int TrialNo { get; set; }
        public string Combination { get; set; } = string.Empty;
        /// <summary>Bileşim satırlarının seçilen varyant kodları ("1×STD + 2×ESK") — Combination ile aynı sıra.</summary>
        public string Variants { get; set; } = string.Empty;
        public decimal TotalWeight { get; set; }
        public decimal Deviation { get; set; }
        public decimal TotalCost { get; set; }
        public int PieceCount { get; set; }
        public int PackageCount { get; set; }
        public bool Success { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }
}
