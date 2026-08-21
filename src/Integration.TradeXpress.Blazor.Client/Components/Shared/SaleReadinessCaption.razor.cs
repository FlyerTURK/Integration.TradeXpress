using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Satışa-hazırlık işaretli başlık — verilen <see cref="Scope"/> kapsamının en yüksek ağırlığıyla renklenir ve
/// karar gerektiren issue sayısını rozetler. <b>Başlıkların TEK bileşenidir</b>: ürün formu sekmeleri,
/// varyant reçete grubu ve kanal ürünü formlarının iç sekme/bölüm başlıkları hep buradan geçer.
///
/// <para>Endeks iki yoldan gelebilir: sahibin doğrudan verdiği <see cref="Index"/> ya da formun cascade ettiği
/// <c>SaleReadinessIndex</c>. Açık parametre kazanır; ikisi de yoksa boş endeks kullanılır ve hiçbir şey
/// renklenmez — yani bu bileşen satışa hazırlık paneli OLMAYAN formlarda (Good/Jewelry/Metal) sessizce nötr kalır.</para>
/// </summary>
public partial class SaleReadinessCaption
{
    /// <summary>Başlık metni (lokalize edilmiş hâli — bileşen çeviri yapmaz).</summary>
    [Parameter, EditorRequired] public string Text { get; set; } = string.Empty;

    /// <summary>Kapsam yolu (<see cref="SaleReadinessScope"/>). <c>null</c> davranışı için bkz.
    /// <see cref="TreatNullScopeAsRoot"/>.</summary>
    [Parameter] public string? Scope { get; set; }

    /// <summary>Boş <see cref="Scope"/> KÖK sayılsın mı (ürünün TÜM issue'ları).
    ///
    /// <para><b>Varsayılan true</b>: ürünün satışa hazırlık panelinin kendi başlığı kapsamsız çizilir ve orada "kapsam yok"
    /// gerçekten "tüm ürün" demektir. <b>Kanal bileşenleri false geçer</b>: orada kapsamın boş olması "kanal ürünü
    /// henüz kaydedilmedi, kimliği yok" demektir; kök sayılsaydı yeni açılan bir kanal sekmesi ürünün TÜM
    /// issue'larıyla kırmızıya boyanır ve kullanıcıyı olmayan bir soruna yönlendirirdi.</para></summary>
    [Parameter] public bool TreatNullScopeAsRoot { get; set; } = true;

    /// <summary>Endeksi doğrudan veren sahip için (cascade yoksa ya da ezilecekse).</summary>
    [Parameter] public SaleReadinessIssueIndex? Index { get; set; }

    [CascadingParameter(Name = "SaleReadinessIndex")] private SaleReadinessIssueIndex? CascadedIndex { get; set; }

    private SaleReadinessIssueIndex EffectiveIndex
    {
        get { return Index ?? CascadedIndex ?? SaleReadinessIssueIndex.Empty; }
    }

    /// <summary>Kapsamın en yüksek ağırlığı — yalnız METİN RENGİ için; rozeti <c>ReadinessMark</c> çizer.</summary>
    private SaleReadinessSeverity? Severity
    {
        get
        {
            if (!TreatNullScopeAsRoot && string.IsNullOrEmpty(Scope))
            {
                return null;
            }

            return EffectiveIndex.MaxSeverity(Scope);
        }
    }

    private string? TextStyle
    {
        get
        {
            var color = SaleReadinessPalette.HeadingColorOf(Severity);
            if (color is null)
            {
                return null;
            }

            return "color:" + color + "; font-weight:600;";
        }
    }
}
