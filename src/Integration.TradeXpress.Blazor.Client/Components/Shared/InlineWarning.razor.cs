using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Satır içi bildirim kutusu — engellemeyen uyarı (varsayılan) ya da engelleyici hata
/// (<see cref="InlineNoticeTone.Danger"/>). İçerik <see cref="ChildContent"/> ile verilir.</summary>
public partial class InlineWarning
{
    // Kutunun renk paleti TEK yerde: arka plan · kenarlık · metin. Hex'ler ekranlara serpilmesin diye
    // burada durur (SaleReadinessPalette'in tema değişkenleri METİN rengi içindir; pastel dolgu karşılığı
    // DevExpress temasında yok — bu yüzden dolgu/kenarlık sabit tutuldu).
    private const string WarningSurface = "#fff3cd";
    private const string WarningBorder = "#ffe69c";
    private const string WarningText = "#664d03";
    private const string DangerSurface = "#f8d7da";
    private const string DangerBorder = "#f1aeb5";
    private const string DangerText = "#842029";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Kutunun ağırlığı. Varsayılan <see cref="InlineNoticeTone.Warning"/> — parametreyi vermeyen
    /// mevcut kullanımlar eskisiyle birebir aynı görünür.</summary>
    [Parameter] public InlineNoticeTone Tone { get; set; } = InlineNoticeTone.Warning;

    private string IconCssClass
    {
        get
        {
            // Engelde "kapat/dur" ikonu, uyarıda ünlem — satışa hazırlık panelinin issue listesiyle AYNI eşleme
            // (ProductSaleReadinessPanel.SeverityIcon), böylece iki bileşen aynı şeyi aynı ikonla söyler.
            if (Tone == InlineNoticeTone.Danger)
            {
                return TradeXpressIcons.Close;
            }

            return TradeXpressIcons.Warning;
        }
    }

    private string BoxStyle
    {
        get
        {
            var surface = Tone == InlineNoticeTone.Danger ? DangerSurface : WarningSurface;
            var border = Tone == InlineNoticeTone.Danger ? DangerBorder : WarningBorder;
            var text = Tone == InlineNoticeTone.Danger ? DangerText : WarningText;

            return $"background:{surface}; color:{text}; border:1px solid {border}; border-radius:0.375rem;"
                   + " padding:0.5rem 0.75rem; font-size:0.8rem; display:flex; align-items:flex-start; gap:0.5rem;";
        }
    }
}
