using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>
/// N11 mağazasından ürün içe aktarma paneli — kanal edit formunun içinde (yalnız kaydedilmiş kanalda) yaşar.
/// TEK düğme: mağazadaki MEVCUT ürünleri salt GET ile çekip tam zinciri (şablon Product + varyantlar + kanal kaydı
/// + SKU + fiyat/stok override) idempotent yazar. Sonuç raporu ekranda gösterilir — sessiz geçilmez.
///
/// <para><b>Kanal oluşturulurken otomatik başlar</b> (<see cref="AutoImport"/>; Trendyol'la hizalı — 2026-08-04
/// Hakan kararı): panel yalnız kaydedilmiş kanalda görünür olduğundan, yeni kanal kaydedilir kaydedilmez İLK
/// yaşam döngüsünde çekim koşar. Rapor ekranda KALIR — N11 ürün listesi KDV oranı döndürmediği için her kayıt
/// KDV'si boş gelir ve kargo şablonu kanaldan tahmin edilir; kullanıcının bu uyarıları görmesi gerekir.</para>
/// </summary>
public partial class N11MarketplaceImportPanel : CrudComponentBase
{
    /// <summary>İçe aktarılacak (kaydedilmiş) N11 kanalının kimliği.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) → panel ilk görünümünde çekimi otomatik
    /// başlat. Update yoluyla açılan formda daima false (host OnAfterCreate yalnız yeni kayıtta çalışır).</summary>
    [Parameter] public bool AutoImport { get; set; }

    [Inject] private ISalesChannelTrN11ProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı istek engeli — büyük mağazada çekim uzun sürer.
    private bool _busy;
    private N11ImportResultDto? _result;

    /// <summary>Sorunlu/bilgi satırları (genel uyarılar + eklenen stok kodları + atlanan satırlar + eşleşmeyen
    /// kategoriler) — memo'da alt alta gösterilir.</summary>
    private List<string> IssueLines
    {
        get
        {
            if (_result is not { } r)
            {
                return new List<string>();
            }

            return r.Warnings
                .Concat(r.AddedStockCodes.Select(c => $"{L["N11Product:Import:AddedPrefix"]}: {c}"))
                .Concat(r.SkippedRows.Select(s => s.ToString()))
                .Concat(r.UnmatchedCategories.Select(c => $"{L["N11Product:Import:UnmatchedCategories"]}: {c}"))
                .ToList();
        }
    }

    /// <summary>Create-anı otomatik çekimi: panel yalnız kaydedilmiş kanalda görünür olduğundan, yeni kanal
    /// kaydedilir kaydedilmez İLK yaşam döngüsünde çekim başlar (ComponentBase incomplete-task akışı busy
    /// durumunu ve sonucu doğal render'larla gösterir). Elle düğme aynen çalışır (idempotent).</summary>
    protected override async Task OnInitializedAsync()
    {
        if (AutoImport)
        {
            await ImportAsync();
        }
    }

    // İçe aktar: mağazadan çek → tam zinciri yaz; raporu ekranda tut, hatayı dostane göster.
    private async Task ImportAsync()
    {
        _busy = true;
        try
        {
            _result = await ProductAppService.ImportFromMarketplaceAsync(SalesChannelId);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }
}
