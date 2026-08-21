using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Satışa-hazırlık işaretinin çizim kipi.</summary>
public enum ReadinessMarkMode
{
    /// <summary>Grid satırı / dar durum kolonu — renkli ikon + tooltip.</summary>
    Icon,

    /// <summary>Sekme ya da bölüm başlığı — karar gerektiren issue SAYACI rozeti.</summary>
    HeadingBadge,
}

/// <summary>
/// Bir kapsamın satışa-hazırlık işareti. Bileşen KURAL BİLMEZ: ağırlığı endeksten okur, rengi paletten alır.
///
/// <para><b>Ne zaman HİÇBİR ŞEY çizmez:</b> (1) endeks yoksa — panel henüz yüklenmemiş ya da bu bileşen ürün
/// formunun cascade'i altında değil; (2) kapsam boşsa VE <see cref="TreatNullScopeAsRoot"/> kapalıysa —
/// kaydedilmemiş satırın kimliği yoktur, dolayısıyla hakkında issue da olamaz (boş kapsamı "kök" sayıp TÜM
/// ürünün issue'larını bu satıra yapıştırmak yanlış olurdu); (3) en yüksek ağırlık karar gerektirmiyorsa — <c>Info</c> ve "issue yok" işaret ÜRETMEZ
/// (<see cref="SaleReadinessPalette.IsActionable"/>; KDV issue'su dersi).</para>
/// </summary>
public partial class ReadinessMark
{
    /// <summary>Ürün formunun cascade ettiği issue endeksi (<c>Name="SaleReadinessIndex"</c>). Yoksa işaret yok.</summary>
    [Parameter] public SaleReadinessIssueIndex? Index { get; set; }

    /// <summary>Bu bileşenin <see cref="SaleReadinessScope"/> yolu. Boş = kimliği henüz olmayan kayıt → işaret yok
    /// (<see cref="TreatNullScopeAsRoot"/> bunu değiştirir).</summary>
    [Parameter] public string? Scope { get; set; }

    /// <summary>Boş <see cref="Scope"/> KÖK sayılsın mı (ürünün TÜM issue'ları). <b>Varsayılan false</b>: bir
    /// satır/sekme işaretinin doğal anlamı "kimliği olmayan kayıt hakkında issue olamaz"dır. Yalnız kökü kasten
    /// gösteren bileşen (satışa hazırlık panelinin başlığı, <c>SaleReadinessCaption</c> üzerinden) <c>true</c> geçer.</summary>
    [Parameter] public bool TreatNullScopeAsRoot { get; set; }

    /// <summary>Çizim kipi — satırda ikon, başlıkta sayaç rozeti.</summary>
    [Parameter] public ReadinessMarkMode Mode { get; set; } = ReadinessMarkMode.Icon;

    /// <summary>Endekse sorulabilir mi: endeks bağlı VE kapsam adreslenebilir (dolu, ya da kasten kök).
    /// Üç okuyucu da (renk · sayaç · ipucu) bu tek guard'dan geçer; aksi hâlde biri gevşer ve yalnız o yol
    /// kökü sızdırırdı.</summary>
    private bool CanQueryIndex
    {
        get
        {
            return Index is not null && (TreatNullScopeAsRoot || !string.IsNullOrEmpty(Scope));
        }
    }

    /// <summary>Kapsamın en yüksek ağırlığı; endeks/kapsam yoksa null.</summary>
    private SaleReadinessSeverity? Severity
    {
        get
        {
            if (!CanQueryIndex)
            {
                return null;
            }

            return Index!.MaxSeverity(Scope);
        }
    }

    /// <summary>Yalnız karar gerektiren ağırlıkta çizilir.</summary>
    private bool IsVisible
    {
        get
        {
            return SaleReadinessPalette.IsActionable(Severity);
        }
    }

    /// <summary>Rozet sayacı — Info sayılmaz (bilgi satırı rozeti şişirmesin).</summary>
    private int ActionableCount
    {
        get
        {
            if (!CanQueryIndex)
            {
                return 0;
            }

            return Index!.Count(Scope);
        }
    }

    private string IconStyle
    {
        get
        {
            return "color:" + SaleReadinessPalette.ColorOf(Severity) + ";";
        }
    }

    private string BadgeStyle
    {
        get
        {
            return SaleReadinessPalette.BadgeStyle(Severity);
        }
    }

    /// <summary>Kapsamdaki İLK karar gerektiren issue'nun mesajı (sunucu en ağırı öne koyar).</summary>
    private string? Tooltip
    {
        get
        {
            if (!CanQueryIndex)
            {
                return null;
            }

            foreach (var issue in Index!.For(Scope))
            {
                if (SaleReadinessPalette.IsActionable(issue.Severity))
                {
                    return issue.Message;
                }
            }

            return null;
        }
    }
}
