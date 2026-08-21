using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Bir satır kaydının nereye gittiği.</summary>
public enum VoucherLinePersistOutcome
{
    /// <summary>Normal cari akışı — fiş satırı POSTLANDI (bugünkü davranış).</summary>
    Posted,

    /// <summary>İç kasa kipi — Teyit TEKLİFİ kuruldu (Proposed). Fiş YOK, ledger kımıldamadı.</summary>
    Proposed,

    /// <summary>İç kasa kipi — alıcının BEYANI kaydedildi (Declared). Fiş YOK; gönderenin teyidi bekleniyor.</summary>
    Declared,

    /// <summary>Ön koşul sağlanmadı (ör. başlatan kasa seçili değil) — hiçbir şey yazılmadı, kullanıcı uyarıldı.</summary>
    Blocked,
}

/// <summary>Kalıcılaştırma isteği. <see cref="DeclareConfirmationId"/> doluysa bu bir BEYAN'dır (alıcı kendi
/// satırını yazıyor); değilse <see cref="CounterpartyVaultId"/> kipi belirler.</summary>
public sealed record VoucherLinePersistRequest(
    VoucherLineDto Line,
    Guid? CounterpartyVaultId,
    Guid? InitiatorVaultId,
    Guid? DeclareConfirmationId = null);

/// <summary>Sonuç — <see cref="VoucherLinePersistResult.Line"/> yalnız <see cref="VoucherLinePersistOutcome.Posted"/>
/// için doludur (fiş oluştu). Diğer hâllerde null → çağıran fiş/grid durumuna DOKUNMAZ.</summary>
public sealed record VoucherLinePersistResult(VoucherLinePersistOutcome Outcome, VoucherLineDto? Line);

/// <summary>
/// Fiş satırı kaydının <b>TEK KARAR NOKTASI</b> (SSOT): satır normal fiş yoluna mı gidecek, yoksa Teyit
/// (organizasyon-içi mirror onayı) yoluna mı?
///
/// <para><b>Neden servis, neden base sınıf değil:</b> paneller İKİ ayrı hiyerarşide yaşıyor
/// (<see cref="ProcessPanelHostBase"/> → Nakit/Maden/Hurda/Vadeli/Hizmet · <c>CommodityProcessPanelBase</c> →
/// Taş/Mücevher/Mamül · <c>DebitNoteProcessPanel</c> standalone). Kuralı base'e koymak onu 3 yere KOPYALARDI
/// → kompozisyon: üç çağıran da bu servisi tüketir, kural tek yerde yaşar (DRY + Composition-over-Inheritance).</para>
///
/// <para><b>Dış cari akışı DEĞİŞMEZ:</b> <see cref="VoucherLinePersistRequest.CounterpartyVaultId"/> null ise
/// davranış bugünküyle birebir aynıdır (<c>SaveLineAsync</c>).</para>
///
/// <para>İç kip toast'ları BURADA verilir (kural nerede, bildirimi orada); normal akışın Eklendi/Güncellendi
/// toast'ı panellerde kalır (metinleri akışa özel).</para>
/// </summary>
public class VoucherLinePersister
{
    private readonly IVoucherAppService _voucherService;
    private readonly IConfirmationAppService _confirmationService;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<TradeXpressResource> _l;

    public VoucherLinePersister(
        IVoucherAppService voucherService,
        IConfirmationAppService confirmationService,
        IUiInteractionService ui,
        IStringLocalizer<TradeXpressResource> l)
    {
        _voucherService      = voucherService;
        _confirmationService = confirmationService;
        _ui                  = ui;
        _l                   = l;
    }

    public async Task<VoucherLinePersistResult> PersistAsync(VoucherLinePersistRequest request)
    {
        // BEYAN: alıcı kendi satırını yazdı → sunucu MIRROR doğrular (tutmazsa MirrorMismatch fırlatır, çağıran gösterir).
        if (request.DeclareConfirmationId is { } confirmationId)
        {
            await _confirmationService.DeclareAsync(new DeclareConfirmationInput
            {
                Id   = confirmationId,
                Line = request.Line,
                Note = request.Line.Description,
            });
            _ui.ShowSuccessToast(_l["Confirmation:Declared"].Value);
            return new VoucherLinePersistResult(VoucherLinePersistOutcome.Declared, null);
        }

        // Dış cari: bugünkü normal yol — davranış AYNEN korunur.
        if (request.CounterpartyVaultId is not { } counterpartyVaultId)
        {
            var saved = await _voucherService.SaveLineAsync(request.Line);
            return new VoucherLinePersistResult(VoucherLinePersistOutcome.Posted, saved);
        }

        if (request.InitiatorVaultId is not { } initiatorVaultId)
        {
            _ui.ShowWarningToast(_l["Confirmation:InitiatorVaultRequired"].Value);
            return new VoucherLinePersistResult(VoucherLinePersistOutcome.Blocked, null);
        }

        // TEKLİF: iç kasa karşı taraf → satır POSTLANMAZ, Teyit kurulur (ledger kımıldamaz — zero-trust).
        await _confirmationService.ProposeAsync(new ProposeConfirmationInput
        {
            InitiatorVaultId    = initiatorVaultId,
            CounterpartyVaultId = counterpartyVaultId,
            Line                = request.Line,
            Note                = request.Line.Description,
        });
        _ui.ShowSuccessToast(_l["Confirmation:Proposed"].Value);
        return new VoucherLinePersistResult(VoucherLinePersistOutcome.Proposed, null);
    }
}
