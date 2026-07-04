using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Fiş satırı panellerinin ortak kabuğu: renkli yön şeridi + içerik + Kaydet/Geri butonları.
/// </summary>
public partial class ProcessPanelBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public ProcessDirectionType Direction { get; set; }
    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnBack { get; set; }

    /// <summary>Kaydet butonu aktifliği — kaydetme sürerken panel false geçer (çift-gönderim koruması).</summary>
    [Parameter] public bool SaveEnabled { get; set; } = true;

    [Parameter] public string? ProcessTypeName { get; set; }
    [Parameter] public string? PaymentTypeName { get; set; }
    [Parameter] public string? AccountCode { get; set; }
    [Parameter] public string? SubAccountCode { get; set; }

    private string StripText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ProcessTypeName)) parts.Add(ProcessTypeName.ToUpperInvariant());
        parts.Add(L[$"Enum:ProcessDirectionType:{Direction}"].Value.ToUpperInvariant());
        if (!string.IsNullOrEmpty(PaymentTypeName)) parts.Add(PaymentTypeName.ToUpperInvariant());

        var text = string.Join("   ", parts);

        if (!string.IsNullOrEmpty(AccountCode))
        {
            var account = string.IsNullOrEmpty(SubAccountCode)
                ? $"[{AccountCode}]"
                : $"[{AccountCode} / {SubAccountCode}]";
            text += $"   {account}";
        }

        return text;
    }

    private string StripStyle()
    {
        // inflow (Giriş/Alacak/Alış) → yeşil; aksi (Çıkış/Borç/Satış) → kırmızı.
        var isInflow = Direction.IsInflow();
        var gradient = isInflow
            ? "var(--gradient-green)"
            : "var(--gradient-red)";

        return $"height:34px; border-radius:4px 4px 0 0; background:{gradient};";
    }
}
