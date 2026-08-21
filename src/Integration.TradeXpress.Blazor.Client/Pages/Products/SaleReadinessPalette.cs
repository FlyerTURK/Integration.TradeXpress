using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// SATIŞA-HAZIRLIK AĞIRLIK RENKLERİ — TEK yer (2026-08-19 Hakan: <i>"devexpress renk tanımları da buna uygun
/// danger, warning tarzında renklendirme yapabiliyor"</i>).
///
/// <para>Renkler DevExpress temasının KENDİ değişkenlerinden okunur (<c>--dxbl-danger</c> / <c>--dxbl-warning</c> /
/// <c>--dxbl-success</c> / <c>--dxbl-info</c>); yedek sabit yalnız değişken tanımsızsa devreye girer. Depoda
/// yerleşik desen budur (<c>main.css</c> <c>.tx-readonly-value.danger</c>). Böylece tema değişince panel,
/// sekme başlıkları ve satır rozetleri kendiliğinden uyar; ekranlara sabit hex serpilmez.</para>
///
/// <para><b>Neden yeni CSS sınıfı açılmadı:</b> CSS oluşturmak onay ister (CLAUDE.md §1). Inline stil + tema
/// değişkeni aynı sonucu verir ve mevcut <c>InlineWarning</c>/<c>InfoNotice</c> desenine uyar.</para>
/// </summary>
public static class SaleReadinessPalette
{
    public const string Danger = "var(--dxbl-danger, #c62828)";
    public const string Warning = "var(--dxbl-warning, #b45309)";
    public const string Info = "var(--dxbl-info, #0369a1)";
    public const string Success = "var(--dxbl-success, #2e7d32)";
    public const string Muted = "var(--dxbl-text-muted, #6c757d)";

    /// <summary>Ağırlığın rengi. <c>null</c> ağırlık = issue yok → yeşil (tamam).</summary>
    public static string ColorOf(SaleReadinessSeverity? severity)
    {
        return severity switch
        {
            SaleReadinessSeverity.Error => Danger,
            SaleReadinessSeverity.Warning => Warning,
            SaleReadinessSeverity.Info => Info,
            _ => Success,
        };
    }

    /// <summary>Karar gerektiren ağırlık mı (Error/Warning) — Info ve "issue yok" bileşeni RENKLENDİRMEZ:
    /// her bilgi satırı sekme başlığını boyarsa renk anlamını yitirir (KDV issue'su dersi, aynı gün).</summary>
    public static bool IsActionable(SaleReadinessSeverity? severity)
    {
        return severity is SaleReadinessSeverity.Error or SaleReadinessSeverity.Warning;
    }

    /// <summary>Sekme başlığı / bölüm başlığı için renk — yalnız karar gerektiren ağırlıkta renk döner, aksi
    /// hâlde <c>null</c> (başlık tema varsayılanında kalır).</summary>
    public static string? HeadingColorOf(SaleReadinessSeverity? severity)
    {
        return IsActionable(severity) ? ColorOf(severity) : null;
    }

    /// <summary>Ağırlığın İKONU — TEK eşleme (2026-08-19). Panelin issue listesi, varyant grid satırı ve kanal
    /// satırları aynı ağırlığı aynı ikonla söyler; eşleme kopyalandığında ilk sapma "Error bir yerde 'dur', başka
    /// yerde 'ünlem'" biçiminde ortaya çıkmıştı. İkon merkezî sabitten gelir (ad-hoc sembol yok).</summary>
    public static string IconOf(SaleReadinessSeverity? severity)
    {
        return severity switch
        {
            SaleReadinessSeverity.Error => TradeXpressIcons.Close,
            SaleReadinessSeverity.Warning => TradeXpressIcons.Warning,
            _ => TradeXpressIcons.Lightbulb,
        };
    }

    /// <summary>Rozet (sayaç) stili — dolgulu, beyaz metin. Yalnız karar gerektiren ağırlıkta kullanılır.</summary>
    public static string BadgeStyle(SaleReadinessSeverity? severity)
    {
        return "display:inline-block; min-width:1.25rem; padding:0 0.4rem; margin-left:0.35rem; border-radius:0.75rem;"
               + " font-size:0.72rem; line-height:1.15rem; text-align:center; color:#fff; background:"
               + ColorOf(severity) + ";";
    }
}
