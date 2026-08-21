using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// Ürünün satışa hazırlık paneli (2026-08-19). Sunucunun kurduğu <see cref="ProductSaleReadinessDto"/>'yu çizer;
/// kendi başına hiçbir hazırlık kuralı türetmez. Tıklamalar sahibe gider: "Düzelt →" <see cref="OnNavigate"/> ile sekme /
/// form açtırır, "Satışa Doğrula" <see cref="OnVerifyRequested"/> ile host'un mevcut doğrulama yolunu çağırır.
///
/// <para><b>Yükleme:</b> <see cref="ProductId"/> ya da <see cref="ReloadToken"/> değişince (ilk bağlama / kayıt
/// sonrası id gelmesi / kayıt sonrası taze model / reload) ve her aksiyon sonrası. Yeni (Id boş) üründe hiç yüklemez — sunucuda değerlendirilecek kayıt yoktur.</para>
/// </summary>
public partial class ProductSaleReadinessPanel : CrudComponentBase
{
    /// <summary>Hazırlık durumu çizilecek ürün. <see cref="Guid.Empty"/> = henüz kaydedilmemiş (yalnız "önce kaydet" metni).</summary>
    [Parameter] public Guid ProductId { get; set; }

    /// <summary>Form kirli mi — kirliyken "Satışa Doğrula" pasif: kaydedilmemiş değişiklik doğrulamaya girmez.</summary>
    [Parameter] public bool IsDirty { get; set; }

    /// <summary>"Düzelt →" isteği — sahip (ProductLayout) sekme değiştirir / varyant formunu açar.</summary>
    [Parameter] public EventCallback<SaleReadinessNavigation> OnNavigate { get; set; }

    /// <summary>"Satışa Doğrula" — host'un MEVCUT doğrulama yolu (onay diyaloğu + VerifySaleReadinessAsync orada).</summary>
    [Parameter] public EventCallback OnVerifyRequested { get; set; }

    /// <summary>Panelden tetiklenen bir aksiyon sunucuda bir şey değiştirdi (kanal push/senkron, doğrulama).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>
    /// ISSUE ENDEKSİ YUKARI AKAR (2026-08-19): panel her yüklemede issue'lardan bir
    /// <see cref="SaleReadinessIssueIndex"/> kurar ve sahibe bildirir. Sahip onu cascade eder; sekme
    /// başlıkları, varyant satırları ve reçete bölümü kendi kapsamlarını sorup renklenir.
    ///
    /// <para><b>Neden endeksi PANEL üretir:</b> veri zaten burada (<c>GetSaleReadinessAsync</c>). Sahibin
    /// aynı ucu ikinci kez çağırması ürün formu her açıldığında GEREKSİZ bir sunucu turu demekti; üstelik iki
    /// kopya farklı anlarda tazelenip birbirinden ayrışırdı (panel "3 engel" derken sekme başlığı temiz).</para>
    /// </summary>
    [Parameter] public EventCallback<SaleReadinessIssueIndex> OnIndexChanged { get; set; }

    /// <summary>Tazeleme anahtarı — REFERANSI değişince panel yeniden yüklenir (<see cref="ProductId"/> aynı kalsa bile).
    /// Sahip, kaydedilen ürünün taze modelini bağlar: kayıt sonrası Id değişmez ama host yeni bir model örneği
    /// kurar; yalnız Id'ye bakılsaydı "fiyat girdim, kaydettim" sonrası sayaçlar eski değerde kalır ve kullanıcı
    /// el ile "Yenile" basmak zorunda kalırdı. <c>null</c> bırakılırsa yalnız Id değişimi yükler.</summary>
    [Parameter] public object? ReloadToken { get; set; }

    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private ProductSaleReadinessDto? _data;
    private bool _loading;
    private bool _verifyBusy;

    // Yüklenmiş verinin ürün id'si + tazeleme anahtarı — yalnız biri DEĞİŞİNCE yeniden çekilir
    // (OnParametersSet her ebeveyn render'ında koşar; her render'da sunucuya gitmek olmaz).
    private Guid? _loadedProductId;
    private object? _loadedToken;

    // Sahibe EN SON bildirilen endeks — aynı endeksi yeniden yayımlamak sahibi boşuna yeniden çizer ve
    // (sahip render'ı → OnParametersSet → yayım) döngüsünü besler. Yalnız DEĞİŞİNCE bildirilir.
    private SaleReadinessIssueIndex _publishedIndex = SaleReadinessIssueIndex.Empty;

    protected override async Task OnParametersSetAsync()
    {
        if (ProductId == Guid.Empty)
        {
            _loadedProductId = null;
            _loadedToken = null;
            _data = null;
            await PublishIndexAsync(SaleReadinessIssueIndex.Empty);
            return;
        }

        if (_loadedProductId == ProductId && ReferenceEquals(_loadedToken, ReloadToken))
        {
            return;
        }

        _loadedProductId = ProductId;
        _loadedToken = ReloadToken;
        await ReloadAsync();
    }

    /// <summary>Panel verisini sunucudan tazeler. Hata → toast + panelde "yüklenemedi" metni (boş bırakılmaz).</summary>
    public async Task ReloadAsync()
    {
        if (ProductId == Guid.Empty)
        {
            return;
        }

        _loading = true;
        StateHasChanged();
        try
        {
            _data = await ProductAppService.GetSaleReadinessAsync(ProductId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Satışa hazırlık paneli yüklenemedi (ürün {ProductId}).", ProductId);
            _data = null;
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }

        // Yükleme BAŞARISIZSA boş endeks yayımlanır: "bilinmiyor" ile "issue yok" aynı sonucu verir — hiçbir
        // bileşen renklenmez. Bayat bir endeksi ayakta tutmak, düzeltilmiş bir issue'yu kırmızı bırakırdı.
        await PublishIndexAsync(new SaleReadinessIssueIndex(_data?.Issues ?? new List<SaleReadinessIssueDto>()));
    }

    private async Task PublishIndexAsync(SaleReadinessIssueIndex index)
    {
        if (ReferenceEquals(_publishedIndex, index))
        {
            return;
        }

        _publishedIndex = index;
        await OnIndexChanged.InvokeAsync(index);
    }

    // ── Doğrulama ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Doğrula düğmesi açık mı: sunucu "doğrulanabilir varyant var" demiş · form temiz · iş sürmüyor.</summary>
    private bool CanVerifyNow
    {
        get { return _data is { CanVerify: true } && !IsDirty && !_verifyBusy && !_loading; }
    }

    private string VerifyTooltip
    {
        get
        {
            if (IsDirty)
            {
                return L["Product:SaleReadinessSaveBeforeVerify"].Value;
            }

            return L["Product:VerifyForSale"].Value;
        }
    }

    private async Task VerifyAsync()
    {
        if (!CanVerifyNow)
        {
            return;
        }

        _verifyBusy = true;
        try
        {
            // Doğrulamanın kendisi (onay diyaloğu · servis çağrısı · rozet tazeleme) HOST'ta — burada kopyalanmaz.
            await OnVerifyRequested.InvokeAsync();
            await OnChanged.InvokeAsync();
        }
        finally
        {
            _verifyBusy = false;
        }

        await ReloadAsync();
    }

    // ── Gezinme ─────────────────────────────────────────────────────────────────────────────────────

    private Task NavigateAsync(SaleReadinessFixTarget target, Guid? targetId)
    {
        // Verify hedefi sekme değil EYLEMDİR: doğrudan doğrulama yoluna gider.
        if (target == SaleReadinessFixTarget.Verify)
        {
            return VerifyAsync();
        }

        return OnNavigate.InvokeAsync(new SaleReadinessNavigation(target, targetId));
    }

    private async Task OnChannelActionCompletedAsync()
    {
        await OnChanged.InvokeAsync();
        await ReloadAsync();
    }

    // ── Görünüm yardımcıları (yalnız metin/ikon eşlemesi — kural yok) ───────────────────────────────

    private string StockPolicyText
    {
        get { return _data is null ? string.Empty : L[$"Enum:ProductStockPolicy:{_data.StockPolicy}"].Value; }
    }

    private string VatRateText
    {
        get
        {
            if (_data?.VatRate is { } rate)
            {
                return $"%{rate}";
            }

            // KDV eksikliği ENGEL DEĞİL (Hakan 2026-08-19) — yalnız "seçilmedi" yazılır, kırmızı değil.
            return L["Product:VatRateNotSet"].Value;
        }
    }

    /// <summary>Sayaç rengi: aktif varyantların tamamı karşılıyorsa yeşil, hiçbiri karşılamıyorsa kırmızı, arası amber.</summary>
    private static string CounterStyle(int count, int total)
    {
        if (total == 0)
        {
            return string.Empty;
        }

        if (count == 0)
        {
            return "color:#b91c1c; font-weight:600;";
        }

        return count < total ? "color:#b45309; font-weight:600;" : "color:#16a34a; font-weight:600;";
    }

    private static string StepIcon(SaleReadinessStepState state)
    {
        return state switch
        {
            SaleReadinessStepState.Done => TradeXpressIcons.CheckCircle,
            SaleReadinessStepState.Attention => TradeXpressIcons.Warning,
            SaleReadinessStepState.Blocked => TradeXpressIcons.Close,
            _ => TradeXpressIcons.History,
        };
    }

    private static string StepColor(SaleReadinessStepState state)
    {
        return state switch
        {
            SaleReadinessStepState.Done => "#16a34a",
            SaleReadinessStepState.Attention => "#b45309",
            SaleReadinessStepState.Blocked => "#b91c1c",
            _ => "#6c757d",
        };
    }

    private string StepStateText(SaleReadinessStepState state)
    {
        return L[$"Enum:SaleReadinessStepState:{state}"].Value;
    }

    /// <summary>Issue satırının ikonu — eşleme paletten gelir (TEK yer). Panelin listesi ile sekme/grid
    /// işaretleri aynı ağırlığı aynı ikonla söylemek zorundadır; eşleme burada ikinci kez yazıldığında ilk sapma
    /// "Error panelde 'dur', grid'de 'ünlem'" biçiminde çıkmıştı.</summary>
    private static string SeverityIcon(SaleReadinessSeverity severity)
    {
        return SaleReadinessPalette.IconOf(severity);
    }

    private static string SeverityColor(SaleReadinessSeverity severity)
    {
        return severity switch
        {
            SaleReadinessSeverity.Error => "#b91c1c",
            SaleReadinessSeverity.Warning => "#b45309",
            _ => "#0369a1",
        };
    }

    private string SeverityText(SaleReadinessSeverity severity)
    {
        return L[$"Enum:SaleReadinessSeverity:{severity}"].Value;
    }

    /// <summary>Kanal etiketi: kod + tür (kanal ürünleri listesiyle aynı biçim).</summary>
    private string ChannelLabel(ChannelReadinessRowDto row)
    {
        var type = L[$"Enum:SalesChannelType:{row.ChannelType}"];
        var typeText = type.ResourceNotFound ? row.ChannelType.ToString() : type.Value;
        return string.IsNullOrWhiteSpace(row.SalesChannelCode) ? typeText : $"{row.SalesChannelCode} — {typeText}";
    }

    /// <summary>Senkron rozeti — nötr durum sözlüğüyle (<see cref="ChannelProductSyncState"/>) AYNI kelimeler, ama
    /// <see cref="ChannelReadinessRowDto"/> o enum'u taşımadığı için bayraklardan OKUNUR (kural değil görüntü eşlemesi): işleniyor →
    /// Pending; son deneme hatalı → Failed; başarılı gönderim var → Sent; kanalda var ama biz göndermedik →
    /// Imported; hiçbiri → NotSent.</summary>
    private ChannelProductSyncState SyncStateOf(ChannelReadinessRowDto row)
    {
        if (row.IsPending)
        {
            return ChannelProductSyncState.Pending;
        }

        if (!string.IsNullOrWhiteSpace(row.LastError))
        {
            return ChannelProductSyncState.Failed;
        }

        if (row.LastPushedAt is not null || row.LastSyncedAt is not null)
        {
            return ChannelProductSyncState.Sent;
        }

        return row.IsListed ? ChannelProductSyncState.Imported : ChannelProductSyncState.NotSent;
    }

    private string SyncBadgeText(ChannelReadinessRowDto row)
    {
        return L[$"Enum:ChannelProductSyncState:{SyncStateOf(row)}"].Value;
    }

    private string SyncBadgeStyle(ChannelReadinessRowDto row)
    {
        var color = SyncStateOf(row) switch
        {
            ChannelProductSyncState.Sent => "#16a34a",
            ChannelProductSyncState.Failed => "#b91c1c",
            ChannelProductSyncState.Pending => "#b45309",
            ChannelProductSyncState.Imported => "#0369a1",
            _ => "#6c757d",
        };

        return $"display:inline-block; padding:2px 10px; border-radius:10px; font-size:0.8rem; color:#fff; background:{color};";
    }
}
