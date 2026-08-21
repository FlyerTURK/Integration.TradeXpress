using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Kapsam-duyarlı satışa-hazırlık bandı. Bir bileşen (kanal ürünleri paneli, varyant paneli, kanal ürünü formunun
/// kombinasyon sekmesi) kendi <see cref="Scope"/>'unu verir; bant o kapsamdaki engelleri/uyarıları yazar.
///
/// <para><b>Neden kapsamlı:</b> aynı issue birden çok seviyede görünür (<see cref="SaleReadinessScope"/>), ama
/// UYARI METNİ kullanıcının o an baktığı yerde anlamlı olmalı. Kanal eksikliğini ürün formunun tepesinde
/// göstermek onu "ürün hatası" gibi okutuyordu (2026-08-20 Hakan düzeltmesi).</para>
///
/// <para><b>Endeks opsiyoneldir:</b> aynı paneller satışa hazırlık paneli olmayan formlarda da kullanılıyor (Good/Jewelry/Metal
/// formları, kanal board'u). Cascade yoksa bant hiç çizilmez — parametre eklemek gerekmez.</para>
/// </summary>
public partial class ReadinessNotice
{
    /// <summary>Bu bileşenin kapsamı (<see cref="SaleReadinessScope"/>). <c>null</c> = kök (tüm issue'lar) — yalnız
    /// satışa hazırlık paneli gibi ürünün tamamını temsil eden bileşenler verir.</summary>
    [Parameter] public string? Scope { get; set; }

    /// <summary>Bandın sonundaki bağlantının metni (ör. "Satışa Hazırlık'a git"). <see cref="OnOpenDetails"/>
    /// bağlı değilse çizilmez.</summary>
    [Parameter] public string? DetailsText { get; set; }

    /// <summary>Ayrıntıya götüren isteğe bağlı eylem (ör. Satışa Hazırlık sekmesini aç).</summary>
    [Parameter] public EventCallback OnOpenDetails { get; set; }

    /// <summary>Metinde en fazla kaç issue mesajı sıralanır; kalanı "+N daha" olarak özetlenir.</summary>
    [Parameter] public int MaxMessages { get; set; } = 3;

    /// <summary>Ürün formunun cascade ettiği issue endeksi; yoksa bant çizilmez.</summary>
    [CascadingParameter(Name = "SaleReadinessIndex")] private SaleReadinessIssueIndex? Readiness { get; set; }

    /// <summary>Kapsamdaki en yüksek ağırlık — bandın çizilip çizilmeyeceğini ve rengini belirler.</summary>
    private SaleReadinessSeverity? Severity
    {
        get { return Readiness?.MaxSeverity(Scope); }
    }

    private InlineNoticeTone Tone
    {
        get
        {
            if (Severity == SaleReadinessSeverity.Error)
            {
                return InlineNoticeTone.Danger;
            }

            return InlineNoticeTone.Warning;
        }
    }

    /// <summary>Bant metni: kapsamdaki KARAR GEREKTİREN issue'ların mesajları (Info elenir — bandı açmayan issue
    /// bandın içinde de yazılmaz). Fazlası "+N daha" ile özetlenir: bant bir liste değil, bir işarettir.</summary>
    private string Text
    {
        get
        {
            var actionable = ActionableIssues();
            if (actionable.Count == 0)
            {
                return string.Empty;
            }

            var shown = actionable.Take(MaxMessages).Select(i => i.Message);
            var text = string.Join(" · ", shown);

            var remaining = actionable.Count - MaxMessages;
            if (remaining > 0)
            {
                text += " · " + L["SaleReadiness:MoreIssues", remaining].Value;
            }

            return text;
        }
    }

    private List<SaleReadinessIssueDto> ActionableIssues()
    {
        if (Readiness is null)
        {
            return new List<SaleReadinessIssueDto>();
        }

        return Readiness.For(Scope)
            .Where(i => SaleReadinessPalette.IsActionable(i.Severity))
            .ToList();
    }
}
