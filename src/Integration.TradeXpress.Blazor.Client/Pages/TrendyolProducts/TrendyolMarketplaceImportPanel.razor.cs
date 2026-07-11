using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>Trendyol'dan ürün içe aktarma paneli — kanal edit formunun içinde (yalnız kaydedilmiş kanalda) yaşar.
/// TEK düğme: pazaryerindeki MEVCUT satıcı ürünlerini salt GET ile çekip tam zinciri (şablon Product + varyant +
/// kanal ürünü grafı) idempotent yazar; remote'ta olup yerelde OLMAYAN barkodlu kalemler şablona OTOMATİK varyant
/// olarak eklenir (2026-07-11 kullanıcı kararı — eski "Eksik Varyantları Tamamla" düğmesi import'a gömüldü).
/// Kanal bu oturumda YENİ oluşturulduysa (<see cref="AutoImport"/>) panel ilk görünümünde importu kendisi başlatır:
/// create-success = kimlik sunucuda doğrulandı (verifier geçemeseydi kayıt hiç açılmazdı). SONUÇ RAPORU ekranda
/// gösterilir (sessiz geçilmez — N11 komisyon import raporu deseni).</summary>
public partial class TrendyolMarketplaceImportPanel : CrudComponentBase
{
    /// <summary>İçe aktarılacak (kaydedilmiş) Trendyol kanalının kimliği.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) → panel ilk görünümünde importu otomatik başlat.
    /// Update yoluyla açılan formda daima false (host OnAfterCreate yalnız yeni kayıtta çalışır).</summary>
    [Parameter] public bool AutoImport { get; set; }

    [Inject] private ISalesChannelTrTrendyolProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı istek engeli (otomatik başlatma + elle yeniden-çekim aynı kilidi paylaşır).
    private bool _busy;
    private TrendyolImportResultDto? _result;

    /// <summary>Sorunlu/bilgi satırları (import-geneli uyarılar + eklenen varyant barkodları + atlanan kalemler +
    /// eşleşmeyen kategoriler) — memo'da alt alta gösterilir.</summary>
    private List<string> IssueLines
    {
        get
        {
            if (_result is not { } r)
            {
                return new List<string>();
            }

            return r.Warnings
                .Concat(r.AddedBarcodes.Select(b => $"{L["TrendyolProduct:Import:AddedPrefix"]}: {b}"))
                .Concat(r.SkippedRows.Select(s => s.ToString()))
                .Concat(r.UnmatchedCategories.Select(c => $"{L["TrendyolProduct:Import:UnmatchedCategoryPrefix"]}: {c}"))
                .ToList();
        }
    }

    /// <summary>Create-anı otomatik importu: panel yalnız kaydedilmiş kanalda görünür olduğundan, yeni kanal
    /// kaydedilir kaydedilmez İLK yaşam döngüsünde import başlar (ComponentBase incomplete-task akışı busy
    /// durumunu ve sonucu doğal render'larla gösterir). Elle yeniden-çekim düğmesi aynen çalışır (idempotent).</summary>
    protected override async Task OnInitializedAsync()
    {
        if (AutoImport)
        {
            await ImportAsync();
        }
    }

    // İçe aktar: pazaryerinden çek → tam zinciri yaz (eksik varyantlar dahil); raporu ekranda tut, hatayı dostane göster.
    private async Task ImportAsync()
    {
        _busy = true;
        try
        {
            _result = await ProductAppService.ImportFromMarketplaceAsync(SalesChannelId);
            UiService.ShowSuccessToast(L["TrendyolProduct:ImportSuccess"]);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["TrendyolProduct:ImportFailed"].Value);
        }
        finally
        {
            _busy = false;
        }
    }
}
