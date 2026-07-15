using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.Confirmations;

/// <summary>
/// Teyit gelen/giden kutusu (MDI sekme). Server-otoriter iki-taraflı liste
/// (<see cref="IConfirmationAppService.GetListAsync"/>); sekmeler DTO'daki <see cref="ConfirmationDto.IsInitiatorMine"/> /
/// <see cref="ConfirmationDto.IsCounterpartyMine"/> UI-gating bayraklarıyla bucket'lanır.
///
/// <para><b>Zero-trust:</b> "Kendi Girişimi Yaz" (BEYAN) GERÇEK process panelini açar
/// (<see cref="ConfirmationDeclarePanelHost"/>) — alıcı tam bir satır yazar, gönderenin değerleri
/// ÖN-DOLDURULMAZ. Sistem aynalamaz; sunucu iki bağımsız satırın ayna olduğunu doğrular. Uyuşmazlıkta
/// (<c>TradeXpress:Confirmation:MirrorMismatch</c>) fark panelin hata toast'ında yüzeye çıkar.</para>
///
/// <para>Teyit/Red satır yazımı gerektirmez → sade karar+not popup'ında kalır.</para>
/// </summary>
public partial class ConfirmationInboxPage
{
    [Inject] private IConfirmationAppService ConfirmationService { get; set; } = default!;
    [Inject] private IWorkingContextService Working { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;

    private List<ConfirmationDto> _all = new();

    private int _activeTabIndex;

    private bool _canDeclare;
    private bool _canConfirm;
    private bool _canReject;

    // ── Aksiyon popup durumu ──
    private bool _popupVisible;      // Teyit/Red (karar + not)
    private bool _declareVisible;    // Beyan (gerçek process paneli)
    private ConfirmationAction _action;
    private ConfirmationDto? _row;
    private string? _note;
    private string? _error;
    private bool _busy;

    /// <summary>GELEN kutusu: karşı kasa benim → kaydımı (Proposed) ya da bilgimi (Declared) bekleyenler.</summary>
    private IReadOnlyList<ConfirmationDto> IncomingRows
    {
        get { return _all.Where(c => c.IsCounterpartyMine).ToList(); }
    }

    /// <summary>GİDEN kutusu: başlatan kasa benim → benim açtığım teyitler.</summary>
    private IReadOnlyList<ConfirmationDto> OutgoingRows
    {
        get { return _all.Where(c => c.IsInitiatorMine).ToList(); }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Working.EnsureLoadedAsync();

        _canDeclare = await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.Confirmations.Declare);
        _canConfirm = await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.Confirmations.Confirm);
        _canReject  = await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.Confirmations.Reject);

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _all = await ConfirmationService.GetListAsync(new ConfirmationListRequest());
        await InvokeAsync(StateHasChanged);
    }

    private void OnActionRequested(ConfirmationActionRequest request)
    {
        _row    = request.Row;
        _action = request.Action;
        _note   = null;
        _error  = null;

        // BEYAN = satır YAZMA → gerçek process paneli (ön-doldurma YOK). Teyit/Red = KARAR → sade popup.
        if (request.Action == ConfirmationAction.Declare)
        {
            _declareVisible = true;
            return;
        }

        _popupVisible = true;
    }

    /// <summary>Beyan sunucuca kabul edildi (ayna tuttu) → popup kapanır, liste tazelenir. Başarı toast'ını
    /// <c>VoucherLinePersister</c> zaten verdi (kural nerede, bildirimi orada).</summary>
    private async Task OnDeclaredAsync()
    {
        _declareVisible = false;
        await ReloadAsync();
    }

    private async Task ExecuteActionAsync()
    {
        if (_busy || _row is not { } row)
        {
            return;
        }

        var note = string.IsNullOrWhiteSpace(_note) ? null : _note!.Trim();

        _busy  = true;
        _error = null;
        try
        {
            switch (_action)
            {
                case ConfirmationAction.Confirm:
                    await ConfirmationService.ConfirmAsync(new ConfirmConfirmationInput { Id = row.Id, Note = note });
                    break;
                case ConfirmationAction.Reject:
                    await ConfirmationService.RejectAsync(new RejectConfirmationInput { Id = row.Id, Reason = note });
                    break;
            }

            _popupVisible = false;
            Ui.ShowSuccessToast(L["Confirmation:ActionSucceeded"]);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            // Yetki reddi/durum çakışması dialogda KALICI kalır (toast kaçar); circuit'i düşürme.
            _error = Describe(ex);
            Ui.ShowErrorToast(_error);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Sunucu hatasını kullanıcı diline çevirir (lokalize error-code).</summary>
    private string Describe(Exception ex)
    {
        if (ex is not BusinessException { Code: { } code } || string.IsNullOrWhiteSpace(code))
        {
            return ex.Message;
        }

        return L[code].Value;
    }

    private string ActionTitle
    {
        get
        {
            switch (_action)
            {
                case ConfirmationAction.Declare:
                    return L["Confirmation:Action:Declare"].Value;
                case ConfirmationAction.Confirm:
                    return L["Confirmation:Action:Confirm"].Value;
                case ConfirmationAction.Reject:
                    return L["Confirmation:Action:Reject"].Value;
                default:
                    return string.Empty;
            }
        }
    }

    private ButtonRenderStyle ActionButtonStyle
    {
        get
        {
            switch (_action)
            {
                case ConfirmationAction.Confirm:
                    return ButtonRenderStyle.Success;
                case ConfirmationAction.Reject:
                    return ButtonRenderStyle.Danger;
                default:
                    return ButtonRenderStyle.Primary;
            }
        }
    }
}
