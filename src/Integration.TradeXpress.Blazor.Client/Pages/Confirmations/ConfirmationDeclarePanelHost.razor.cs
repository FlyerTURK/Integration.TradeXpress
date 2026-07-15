using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Confirmations;

/// <summary>
/// "Kendi Girişimi Yaz" (BEYAN) paneli host'u — teklifin process tipine karşılık gelen GERÇEK transaction
/// panelini açar. Alıcı gönderenle AYNI aracı kullanır; ayrı/sahte bir beyan formu yoktur.
///
/// <para><b>ÖN-DOLDURMA YOK (spec §6):</b> panele gönderenin hiçbir değeri (yön/emtia/miktar/tutar/birim)
/// geçirilmez — paneller kendi varsayılanlarıyla BOŞ doğar. Ön-doldurma teyidin anlamını öldürürdü: alıcı
/// kendi gözlediğini yazmalı ki iki bağımsız beyanın AYNA olup olmadığı anlamlı bir sınav olsun.</para>
///
/// <para><b>Bağlam yönü:</b> alıcı KENDİ satırını yazar → <c>VaultId</c> = alıcının kasası
/// (<see cref="ConfirmationDto.CounterpartyVaultId"/>), karşı taraf = gönderenin kasası
/// (<see cref="ConfirmationDto.InitiatorVaultId"/>). <c>DeclareConfirmationId</c> dolu olduğundan panel yeni
/// teklif AÇMAZ, bu Teyit'e beyan yazar (<see cref="VoucherLinePersister"/>).</para>
///
/// <para>Fiş başlığı (hesap/cari) BURADA seçilmez — teyit kapanınca sunucu karşı kasanın vault-cari'sinden
/// türetir. Bu yüzden AccountId/SubAccountId geçilmez.</para>
/// </summary>
public partial class ConfirmationDeclarePanelHost
{
    [Inject] private IWorkingContextService Working { get; set; } = default!;

    /// <summary>Beyanı yazılacak Teyit (gelen kutusu satırı).</summary>
    [Parameter] public ConfirmationDto? Row { get; set; }

    /// <summary>Beyan sunucuya kabul edilince (ayna tuttu) tetiklenir — çağıran popup'ı kapatıp listeyi tazeler.</summary>
    [Parameter] public EventCallback OnDeclared { get; set; }

    /// <summary>Vazgeç (panelin GERİ'si).</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    private Guid CompanyId
    {
        get { return Working.CurrentCompanyId ?? Guid.Empty; }
    }

    private Guid BranchId
    {
        get { return Working.CurrentBranchId ?? Guid.Empty; }
    }

    /// <summary>Bu tip <see cref="ProcessPanelHostBase"/> hiyerarşisinde mi (tek <see cref="VoucherLineContext"/>
    /// parametresi) yoksa düz-parametreli hiyerarşide mi? (İki hiyerarşi olgunlaştırmada birleşecek.)</summary>
    private bool UsesContext
    {
        get
        {
            if (Row is null)
            {
                return false;
            }

            switch (Row.ProcessType)
            {
                case ProcessType.Cash:
                case ProcessType.Metal:
                case ProcessType.Scrap:
                case ProcessType.Service:
                case ProcessType.Future:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Beyan bağlamı: alıcının kasası + karşı taraf gönderen + bu Teyit'in id'si. Gönderenin
    /// DEĞERLERİ taşınmaz (ön-doldurma yok).</summary>
    private VoucherLineContext BuildContext()
    {
        return new VoucherLineContext
        {
            CompanyId             = CompanyId,
            BranchId              = BranchId,
            VaultId               = Row!.CounterpartyVaultId,
            VoucherDate           = BusinessClock.Now(),
            CounterpartyVaultId   = Row.InitiatorVaultId,
            DeclareConfirmationId = Row.Id,
        };
    }

    private async Task OnSubmitted(VoucherLinePersistOutcome outcome)
    {
        if (outcome == VoucherLinePersistOutcome.Declared)
        {
            await OnDeclared.InvokeAsync();
        }
    }
}
