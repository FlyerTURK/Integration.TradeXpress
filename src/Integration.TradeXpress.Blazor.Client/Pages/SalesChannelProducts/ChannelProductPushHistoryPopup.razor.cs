using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannelProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Gönderim geçmişi popup'ı — bir kanal-ürünün PushHistoryni gösterir. <b>Salt okuma</b>: bu ekran hiçbir
/// şey yazmaz, silmez, düzeltmez (defter append-only'dir; düzeltilebilir bir delil delil değildir).
/// </summary>
public partial class ChannelProductPushHistoryPopup
{
    [Inject] private ISalesChannelProductAppService AppService { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;

    private bool _visible;
    private bool _loading;
    private List<SalesChannelProductPushHistoryDto> _rows = new();
    private SalesChannelProductListDto? _row;

    private string HeaderText
    {
        get
        {
            var title = L["SalesChannelProduct:History:Title"].Value;
            var code = _row?.ProductCode ?? _row?.ChannelProductCode;

            return string.IsNullOrWhiteSpace(code) ? title : $"{title} — {code}";
        }
    }

    /// <summary>Seçili satırın geçmişini açar. Kanal TÜRÜ birlikte gider: defter kanal başına ayrı tabloda
    /// tutulur ve id tek başına hangi tabloya bakılacağını söylemez.</summary>
    public async Task OpenAsync(SalesChannelProductListDto row)
    {
        _row = row;
        _rows = new List<SalesChannelProductPushHistoryDto>();
        _visible = true;
        _loading = true;

        try
        {
            _rows = await AppService.GetPushHistoryAsync(row.Id, row.ChannelType);
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Close()
    {
        _visible = false;
    }

    private string OutcomeLabel(ChannelPushOutcome outcome)
    {
        return L[$"Enum:ChannelPushOutcome:{outcome}"].Value;
    }

    private string KindLabel(ChannelPushKind kind)
    {
        return L[$"Enum:ChannelPushKind:{kind}"].Value;
    }

    /// <summary>Sonuç rozeti — kanal ürünleri grid'iyle AYNI renk dili: yeşil gerçekten ulaşan,
    /// kırmızı elini bekleyen.</summary>
    private static string OutcomeBadgeStyle(ChannelPushOutcome outcome)
    {
        var background = outcome == ChannelPushOutcome.Succeeded ? "#16a34a" : "#dc2626";

        return "display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; "
             + $"font-weight:600; color:#fff; background:{background};";
    }
}
