using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.EtsyProducts;

/// <summary>Etsy'den ürün içe aktarma paneli — kanal edit formunun içinde (yalnız kaydedilmiş kanalda) yaşar.
/// TEK düğme: Etsy mağazasındaki MEVCUT aktif listelemeleri salt GET ile çekip tam zinciri (şablon Product + GERÇEK
/// offering grafı + kanal ürünü) idempotent yazar. Kanal bu oturumda YENİ oluşturulduysa (<see cref="AutoImport"/>)
/// panel ilk görünümünde importu kendisi başlatır. SONUÇ RAPORU ekranda gösterilir (sessiz geçilmez — Trendyol import
/// paneli deseni).</summary>
public partial class EtsyMarketplaceImportPanel : CrudComponentBase
{
    /// <summary>İçe aktarılacak (kaydedilmiş) Etsy kanalının kimliği.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) → panel ilk görünümünde importu otomatik başlat.
    /// Update yoluyla açılan formda daima false (host OnAfterCreate yalnız yeni kayıtta çalışır).</summary>
    [Parameter] public bool AutoImport { get; set; }

    [Inject] private ISalesChannelEtsyProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı istek engeli (otomatik başlatma + elle yeniden-çekim aynı kilidi paylaşır).
    private bool _busy;
    private EtsyImportResultDto? _result;

    /// <summary>Sorunlu/bilgi satırları (import-geneli uyarılar + atlanan kalemler) — memo'da alt alta gösterilir.</summary>
    private List<string> IssueLines
    {
        get
        {
            if (_result is not { } r)
            {
                return new List<string>();
            }

            return r.Warnings
                .Concat(r.SkippedRows.Select(s => s.ToString()))
                .ToList();
        }
    }

    /// <summary>Create-anı otomatik importu: panel yalnız kaydedilmiş kanalda görünür olduğundan, yeni kanal
    /// kaydedilir kaydedilmez İLK yaşam döngüsünde import başlar. Elle yeniden-çekim düğmesi aynen çalışır (idempotent).</summary>
    protected override async Task OnInitializedAsync()
    {
        if (AutoImport)
        {
            await ImportAsync();
        }
    }

    // İçe aktar: pazaryerinden çek → tam zinciri yaz; raporu ekranda tut, hatayı dostane göster.
    private async Task ImportAsync()
    {
        _busy = true;
        try
        {
            _result = await ProductAppService.ImportFromMarketplaceAsync(SalesChannelId);
            UiService.ShowSuccessToast(L["EtsyImport:ImportSuccess"]);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["EtsyImport:ImportFailed"].Value);
        }
        finally
        {
            _busy = false;
        }
    }
}
