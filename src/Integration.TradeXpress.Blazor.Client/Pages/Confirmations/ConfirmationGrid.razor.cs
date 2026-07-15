using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Confirmations;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Confirmations;

/// <summary>Kutu tarafı: GELEN = benim kaydımı/teyidimi bekleyenler (karşı kasa benim) · GİDEN = benim başlattıklarım.</summary>
public enum ConfirmationSide
{
    Incoming,
    Outgoing,
}

/// <summary>Satır aksiyonu — durum + taraf + izinle gate'lenir; ilgili AppService metoduna eşlenir.
/// <b>Cancel YOKTUR</b> (iptal kavramı yok — süreci yalnız alıcı Reject ile durdurur).</summary>
public enum ConfirmationAction
{
    Declare,
    Confirm,
    Reject,
}

/// <summary>Bir aksiyon isteği (satır + ne yapılacağı) — grid'den sayfaya taşınır.</summary>
public sealed record ConfirmationActionRequest(ConfirmationDto Row, ConfirmationAction Action);

/// <summary>
/// Teyit gelen/giden grid'i — iki sekmenin ORTAK gövdesi (kolon seti + aksiyon gate'leri tek yerde).
/// Aksiyon matrisi (spec §6): GELEN+Proposed → Kendi Girişimi Yaz / Reddet · GELEN+Declared → aksiyon yok
/// (gönderenin teyidi bekleniyor) · GİDEN+Declared → Teyit Et · GİDEN+Proposed → aksiyon yok (İPTAL YOK) ·
/// Confirmed/Rejected → aksiyon yok (kapandı).
/// </summary>
public partial class ConfirmationGrid
{
    [Parameter] public IReadOnlyList<ConfirmationDto> Rows { get; set; } = new List<ConfirmationDto>();

    /// <summary>Hangi kutu çiziliyor — aksiyon matrisinin taraf ekseni.</summary>
    [Parameter] public ConfirmationSide Side { get; set; }

    [Parameter] public bool CanDeclare { get; set; }
    [Parameter] public bool CanConfirm { get; set; }
    [Parameter] public bool CanReject { get; set; }

    [Parameter] public EventCallback<ConfirmationActionRequest> OnAction { get; set; }

    /// <summary>GELEN + Proposed: alıcı KENDİ girişini KENDİ ELİYLE yazar (sistem aynalamaz).</summary>
    private bool CanDeclareRow(ConfirmationDto row)
        => CanDeclare && Side == ConfirmationSide.Incoming && row.IsCounterpartyMine && row.Status == ConfirmationStatus.Proposed;

    /// <summary>GİDEN + Declared: gönderen alıcının kaydını teyit eder → iki bacak postlanır.</summary>
    private bool CanConfirmRow(ConfirmationDto row)
        => CanConfirm && Side == ConfirmationSide.Outgoing && row.IsInitiatorMine && row.Status == ConfirmationStatus.Declared;

    /// <summary>GELEN + Proposed: süreci durdurmanın TEK yolu (gönderenin iptali yoktur).</summary>
    private bool CanRejectRow(ConfirmationDto row)
        => CanReject && Side == ConfirmationSide.Incoming && row.IsCounterpartyMine && row.Status == ConfirmationStatus.Proposed;

    private Task RaiseAsync(ConfirmationDto row, ConfirmationAction action)
        => OnAction.InvokeAsync(new ConfirmationActionRequest(row, action));

    /// <summary>Durum rozeti — yaşam döngüsü renkleri (bekleyen amber/mavi, kapanan yeşil/kırmızı).</summary>
    private static string StatusBadgeStyle(ConfirmationStatus status)
    {
        var background = status switch
        {
            ConfirmationStatus.Proposed  => "#f59e0b",   // amber — alıcının kaydı bekleniyor
            ConfirmationStatus.Declared  => "#3b82f6",   // mavi — gönderenin teyidi bekleniyor
            ConfirmationStatus.Confirmed => "#16a34a",   // yeşil — postlandı, kapandı
            ConfirmationStatus.Rejected  => "#dc2626",   // kırmızı — alıcı reddetti
            _ => "#6b7280",
        };
        return $"display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; font-weight:600; color:#fff; background:{background};";
    }
}
