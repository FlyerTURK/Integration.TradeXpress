using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Mevcut voucher'lardan bakiye ledger'ını (yeniden) doldurur — Path B'ye geçişte tek seferlik,
/// poster değişince ise <c>force</c> ile yeniden. AKTİF tenant kapsamında çalışır; çağıran
/// (seed contributor) her tenant için ayrı tetikler. Etki posterden gelir (<see cref="BalanceLedgerSynchronizer"/>),
/// kural HARDCODE edilmez → ledger her zaman canlı poster davranışıyla aynı.
/// </summary>
public class BalanceLedgerBackfiller : DomainService
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly BalanceLedgerSynchronizer _synchronizer;

    public BalanceLedgerBackfiller(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<BalanceLedgerEntry, Guid> ledgerRepository,
        BalanceLedgerSynchronizer synchronizer)
    {
        _voucherRepository = voucherRepository;
        _ledgerRepository  = ledgerRepository;
        _synchronizer      = synchronizer;
    }

    /// <summary>Aktif tenant'ın ledger'ını doldurur. <paramref name="force"/>=false ise zaten doluysa
    /// atlar (idempotent, ucuz — her DbMigrator'da güvenli). force=true → önce TÜM ledger'ı siler
    /// (orphan/eski poster temizliği) sonra mevcut tüm voucher'lardan yeniden yazar.</summary>
    public async Task BackfillCurrentTenantAsync(bool force = false)
    {
        if (!force && await _ledgerRepository.AnyAsync())
            return;

        if (force)
            await _ledgerRepository.DeleteDirectAsync(e => true);

        var query = await _voucherRepository.WithDetailsAsync(v => v.Lines);
        var vouchers = await AsyncExecuter.ToListAsync(query);
        foreach (var voucher in vouchers)
            await _synchronizer.SyncVoucherAsync(voucher);
    }
}
