using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Bir voucher'ın bakiye ledger kayıtlarını fiş durumuyla SENKRON tutan TEK yol.
/// Strateji: VoucherId bazında <b>sil + yeniden yaz</b> — delta hesabı yok, sapma riski yok,
/// idempotent (aynı voucher'ı tekrar sync etmek sonucu değiştirmez).
///
/// <para>Etki tamamen <see cref="VoucherBalanceCalculator"/> (poster'lar) çıktısından gelir;
/// hiçbir +/− kuralı burada HARDCODE edilmez → kaydedilen işlemle inşaen tutarlı.</para>
///
/// <para><see cref="Volo.Abp.Uow.IUnitOfWork"/> commit'ine bırakır: kendi <c>SaveChanges</c>'ini
/// çağırmaz; voucher kaydı + ledger senkronu çağıran AppService'in tek transaction'ında birleşir.</para>
/// </summary>
public class BalanceLedgerSynchronizer : ITransientDependency
{
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly VoucherBalanceCalculator _calculator;
    private readonly IGuidGenerator _guidGenerator;

    public BalanceLedgerSynchronizer(
        IRepository<BalanceLedgerEntry, Guid> ledgerRepository,
        VoucherBalanceCalculator calculator,
        IGuidGenerator guidGenerator)
    {
        _ledgerRepository = ledgerRepository;
        _calculator       = calculator;
        _guidGenerator    = guidGenerator;
    }

    /// <summary>Voucher'ın tüm ledger kayıtlarını siler, ardından AKTİF satırlarının poster
    /// etkilerinden yeniden yazar. <paramref name="voucher"/> <c>Lines</c> yüklü gelmeli
    /// (çağıran <c>EnsureCollectionLoadedAsync</c> eder).</summary>
    public async Task SyncVoucherAsync(Voucher voucher)
    {
        await _ledgerRepository.DeleteDirectAsync(e => e.VoucherId == voucher.Id);

        var entries = new List<BalanceLedgerEntry>();
        foreach (var line in voucher.Lines.Where(l => !l.IsDeleted))
        {
            foreach (var effect in _calculator.Post(line))
            {
                if (effect.UnitId == Guid.Empty || effect.Amount == 0m)
                    continue;   // boş birim / sıfır etki ledger'a girmez

                entries.Add(new BalanceLedgerEntry(
                    _guidGenerator.Create(), voucher, line, effect.UnitId, effect.Amount));
            }
        }

        if (entries.Count > 0)
            await _ledgerRepository.InsertManyAsync(entries);
    }

    /// <summary>Fiş silindiğinde tüm ledger kayıtlarını temizler (FK yok — manuel).</summary>
    public Task DeleteVoucherAsync(Guid voucherId)
    {
        return _ledgerRepository.DeleteDirectAsync(e => e.VoucherId == voucherId);
    }
}
