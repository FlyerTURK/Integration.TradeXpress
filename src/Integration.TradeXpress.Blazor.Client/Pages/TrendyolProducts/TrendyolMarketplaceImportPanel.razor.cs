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
/// Buton pazaryerindeki MEVCUT satıcı ürünlerini salt GET ile çekip tam zinciri (şablon Product + varyant + kanal
/// ürünü grafı) idempotent yazar; SONUÇ RAPORU ekranda gösterilir (sessiz geçilmez — N11 komisyon import raporu
/// deseni). TrendyolCategorySyncPanel ile simetrik (self-contained + best-effort UI).</summary>
public partial class TrendyolMarketplaceImportPanel : CrudComponentBase
{
    /// <summary>İçe aktarılacak (kaydedilmiş) Trendyol kanalının kimliği.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    [Inject] private ISalesChannelTrTrendyolProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı import engeli — istek dönene kadar buton devre dışı.
    private bool _importing;
    private TrendyolImportResultDto? _result;

    /// <summary>Sorunlu satırlar (import-geneli uyarılar + atlanan kalemler + eşleşmeyen kategoriler) — memo'da
    /// alt alta gösterilir.</summary>
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
                .Concat(r.UnmatchedCategories.Select(c => $"{L["TrendyolProduct:Import:UnmatchedCategoryPrefix"]}: {c}"))
                .ToList();
        }
    }

    // İçe aktar: pazaryerinden çek → tam zinciri yaz; raporu ekranda tut, hatayı dostane göster.
    private async Task ImportAsync()
    {
        _importing = true;
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
            _importing = false;
        }
    }
}
