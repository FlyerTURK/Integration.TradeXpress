using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// Teyit bacağının <b>materyalizasyon motoru</b>: bir tarafın KENDİ eliyle yazdığı satırı gerçek bir fişe
/// dönüştürür. Teyit kapanınca (<c>ConfirmationAppService.ConfirmAsync</c>) iki bacak için iki kez çalışır.
///
/// <para><b>Process-agnostik (tasarımın kilit taşı):</b> hiçbir +/− kuralı burada yoktur. Satır fişe eklenir,
/// <see cref="BalanceLedgerSynchronizer"/> onu ProcessType'ın KENDİ poster'ına yönlendirir
/// (<c>VoucherBalanceCalculator</c> → DI'daki <c>IVoucherLineBalancePoster</c>). Poster'lar yalnız SATIRI okur;
/// ledger kaydı ise kapsamı (kasa/karşı taraf) fiş BAŞLIĞINDAN alır. Sonuç: Nakit'ten Mamül'e her tip, çekirdeğe
/// tek satır dokunmadan doğal çalışır.</para>
///
/// <para><b>Karşılıklı borç/alacak kendiliğinden doğar:</b> her bacağın fiş başlığı = {kendi kasası, karşı taraf
/// <c>AccountType=Vault</c> → KARŞI kasa}. A'nın satırı B kasasına, B'nin satırı A kasasına düşer → iki taraf
/// birbirinin defterinde ters işaretle görünür. Ayrı "karşı kayıt üretme" adımı YOKTUR (spec §5).</para>
///
/// <para><b>Sahte cari YOK (2026-07-15 ürün kararı):</b> karşı taraf eskiden kasa için üretilen sahte bir
/// Account/SubAccount ("vault-cari") idi — cari listesini kirletiyor, kodu ham GUID olduğundan okunmuyordu.
/// Artık kasa DOĞRUDAN karşı taraftır: üst kimlik = karşı kasanın ŞUBESİ, alt kimlik = KASA; kod snapshot'ları
/// <see cref="VoucherCounterpartyResolver"/> ile sunucu-otoriter dondurulur.</para>
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
    private readonly VoucherCounterpartyResolver _counterpartyResolver;
    private readonly IGuidGenerator _guidGenerator;
    private readonly VoucherLineHistoryRecorder _historyRecorder;
    private readonly CommodityStockChangeQueuer _stockChangeQueuer;

    public ConfirmationVoucherMaterializer(
        VoucherNumberAllocator numberAllocator,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        VoucherCounterpartyResolver counterpartyResolver,
        IGuidGenerator guidGenerator,
        VoucherLineHistoryRecorder historyRecorder,
        CommodityStockChangeQueuer stockChangeQueuer)
    {
        _numberAllocator      = numberAllocator;
        _ledgerSynchronizer   = ledgerSynchronizer;
        _counterpartyResolver = counterpartyResolver;
        _guidGenerator        = guidGenerator;
        _historyRecorder      = historyRecorder;
        _stockChangeQueuer    = stockChangeQueuer;
    }

    /// <summary>Bir bacağı postlar: KARŞI KASA başlıklı yeni fiş (numaralı) + tarafın KENDİ satırı + ledger
    /// senkronu. <paramref name="line"/> o tarafın yazdığı satırdır — İÇERİĞİ DEĞİŞTİRİLMEZ (WYSIWYG:
    /// beyan edilen neyse o postlanır).</summary>
    public async Task<Voucher> MaterializeAsync(
        Company company,
        Vault vault,
        Vault counterpartyVault,
        VoucherLineDto line)
    {
        // Karşı taraf = KASA (cari ÜRETİLMEZ): Şube→üst kimlik, Kasa→alt kimlik; kodlar sunucudan dondurulur.
        var counterparty = await _counterpartyResolver.ResolveVaultAsync(company.Id, counterpartyVault.Id);

        var voucher = new Voucher(
            company.Id,
            vault.BranchId,
            vault.Id,
            counterparty.AccountType,
            counterparty.AccountId,
            counterparty.AccountCode,
            counterparty.SubAccountId,
            counterparty.SubAccountCode,
            await _numberAllocator.NextNumberAsync(company.Id),
            line.VoucherDate,
            line.VoucherDescription);

        var materializedLine = voucher.AddLine(_guidGenerator.Create(), VoucherLineDtoFactory.ToLineInput(line));

        await _numberAllocator.InsertNumberedAsync(voucher);
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        // Gölge günlük — Teyit materyalizasyonu da CREATE'tir (2026-07-15 kullanıcı isteği: her cari işlem kaydı).
        await _historyRecorder.RecordAsync(voucher, materializedLine, VoucherLineChangeType.Created);

        // Maden stok tetiği (2026-07-25 inceleme bulgusu #15): teyit bacağı VoucherAppService yolunu
        // KULLANMADIĞINDAN oradaki CommodityStockChangedEto tetiği burada da kurulmalı — aksi halde teyitle giren/çıkan
        // maden, kanal stoklarını GÜNCELLETMEZDİ (oversell kapısı). Aynı sözleşme: commit-SONRASI publish.
        _stockChangeQueuer.QueueForVoucher(voucher);

        return voucher;
    }
}
