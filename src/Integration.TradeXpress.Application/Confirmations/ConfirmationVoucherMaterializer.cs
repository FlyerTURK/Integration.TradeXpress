using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Organization;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// Teyit bacağının <b>materyalizasyon motoru</b>: bir tarafın KENDİ eliyle yazdığı satırı gerçek bir fişe
/// dönüştürür. Teyit kapanınca (<c>ConfirmationAppService.ConfirmAsync</c>) iki bacak için iki kez çalışır.
///
/// <para><b>Process-agnostik (tasarımın kilit taşı):</b> hiçbir +/− kuralı burada yoktur. Satır fişe eklenir,
/// <see cref="BalanceLedgerSynchronizer"/> onu ProcessType'ın KENDİ poster'ına yönlendirir
/// (<c>VoucherBalanceCalculator</c> → DI'daki <c>IVoucherLineBalancePoster</c>). Poster'lar yalnız SATIRI okur;
/// ledger kaydı ise kapsamı (kasa/hesap/cari) fiş BAŞLIĞINDAN alır. Sonuç: Nakit'ten Mamül'e her tip, çekirdeğe
/// tek satır dokunmadan doğal çalışır.</para>
///
/// <para><b>Karşılıklı borç/alacak kendiliğinden doğar:</b> her bacağın fiş başlığı = {kendi kasası, KARŞI
/// kasanın vault-cari'si}. Böylece A'nın satırı cari(B)'ye, B'nin satırı cari(A)'ya düşer → iki taraf birbirinin
/// carisinde ters işaretle görünür. Ayrı bir "karşı kayıt üretme" adımı YOKTUR (spec §5).</para>
///
/// <para><b>Yetki burada YOK — bilinçli:</b> yetki AUTHORING anında alınır (Propose → başlatanın kasası,
/// Declare → alıcının kasası). Teyit yeni bir yetki iddiası değil, iki zaten-yetkilendirilmiş beyanın
/// materyalizasyonudur. Bu yüzden <c>VoucherAppService.SaveLineAsync</c> (çağıran-kullanıcı guard'lı) yolu
/// KULLANILMAZ: teyidi gönderen açar, ama alıcının bacağı alıcının kasasına yazılır — gönderenin grant'ıyla
/// o kasa doğrulanamaz (VaultNotAuthorized). Domain yolu doğru semantiktir.</para>
///
/// <para><b>Atomiklik çağıranın sorumluluğu:</b> iki bacak tek transactional UoW içinde yazılmalıdır
/// (çağıran public metot <c>[UnitOfWork(isTransactional: true)]</c> açar).</para>
/// </summary>
public class ConfirmationVoucherMaterializer : ITransientDependency
{
    private readonly VoucherNumberAllocator _numberAllocator;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly OrgTreeManager _orgTree;
    private readonly IGuidGenerator _guidGenerator;

    public ConfirmationVoucherMaterializer(
        VoucherNumberAllocator numberAllocator,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        OrgTreeManager orgTree,
        IGuidGenerator guidGenerator)
    {
        _numberAllocator    = numberAllocator;
        _ledgerSynchronizer = ledgerSynchronizer;
        _orgTree            = orgTree;
        _guidGenerator      = guidGenerator;
    }

    /// <summary>Kasanın sistem carisini garanti eder (idempotent lazy — mevcut kasalar dahil).</summary>
    public async Task<SubAccount> EnsureVaultCurrentAccountAsync(Company company, Vault vault)
    {
        var (_, subAccount) = await _orgTree.EnsureVaultCurrentAccountAsync(vault, company.BaseCurrencyUnitId);
        return subAccount;
    }

    /// <summary>Bir bacağı postlar: KARŞI kasanın carisi başlıklı yeni fiş (numaralı) + tarafın KENDİ satırı +
    /// ledger senkronu. <paramref name="line"/> o tarafın yazdığı satırdır — İÇERİĞİ DEĞİŞTİRİLMEZ (WYSIWYG:
    /// beyan edilen neyse o postlanır).</summary>
    public async Task<Voucher> MaterializeAsync(
        Company company,
        Vault vault,
        SubAccount counterCari,
        VoucherLineDto line)
    {
        var voucher = new Voucher(
            company.Id,
            vault.BranchId,
            vault.Id,
            counterCari.AccountId,
            counterCari.Id,
            await _numberAllocator.NextNumberAsync(company.Id),
            line.VoucherDate,
            line.VoucherDescription);

        voucher.AddLine(_guidGenerator.Create(), VoucherLineDtoFactory.ToLineInput(line));

        await _numberAllocator.InsertNumberedAsync(voucher);
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);
        return voucher;
    }
}
