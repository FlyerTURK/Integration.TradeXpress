using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fişin karşı-taraf SNAPSHOT'ını (id'ler + kodlar) kurar — <b>tipe göre polimorfik</b>:
/// <see cref="AccountType.CurrentAccount"/> → Account/SubAccount · <see cref="AccountType.Vault"/> →
/// Şube/Kasa. Kod alanları <b>sunucu-otoriter</b>dir: istemciden gelen koda güvenilmez, kaynağın kendi
/// kodu okunup dondurulur (VoucherLine'daki emtia kod snapshot'ı deseniyle aynı).
///
/// <para>Kaynağın gerçekten var olduğu (ve fişin şirketine ait olduğu) burada doğrulanır — FK/navigation
/// olmadığı için (polimorfik kolon) bütünlüğün tek bekçisi bu adımdır.</para>
/// </summary>
public class VoucherCounterpartyResolver : ITransientDependency
{
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;

    public VoucherCounterpartyResolver(
        IRepository<Account, Guid> accountRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository)
    {
        _accountRepository    = accountRepository;
        _subAccountRepository = subAccountRepository;
        _branchRepository     = branchRepository;
        _vaultRepository      = vaultRepository;
    }

    /// <summary>Dış cari karşı tarafı: Account + SubAccount kodları (bugünkü akış).</summary>
    public async Task<VoucherCounterpartySnapshot> ResolveCurrentAccountAsync(
        Guid companyId, Guid accountId, Guid? subAccountId)
    {
        var account = await _accountRepository.FindAsync(accountId)
                      ?? throw new BusinessException("TradeXpress:Voucher:CounterpartyNotFound");
        EnsureOwnedByCompany(companyId, account.CompanyId);

        // Alt hesap ZORUNLU (yeni model): fiş şeması tipten bağımsız dört alanı da ister.
        if (subAccountId is not { } subId || subId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Voucher:SubAccountRequired");
        }

        var subAccount = await _subAccountRepository.FindAsync(subId)
                         ?? throw new BusinessException("TradeXpress:Voucher:CounterpartyNotFound");
        EnsureOwnedByCompany(companyId, subAccount.CompanyId);

        return new VoucherCounterpartySnapshot(
            AccountType.CurrentAccount, account.Id, account.Code, subAccount.Id, subAccount.Code);
    }

    /// <summary>İç kasa karşı tarafı: ŞUBE üst kimliğe, KASA alt kimliğe oturur — cari ÜRETİLMEZ.</summary>
    public async Task<VoucherCounterpartySnapshot> ResolveVaultAsync(Guid companyId, Guid counterpartyVaultId)
    {
        var vault = await _vaultRepository.FindAsync(counterpartyVaultId)
                    ?? throw new BusinessException("TradeXpress:Voucher:CounterpartyNotFound");
        EnsureOwnedByCompany(companyId, vault.CompanyId);

        var branch = await _branchRepository.FindAsync(vault.BranchId)
                     ?? throw new BusinessException("TradeXpress:Voucher:CounterpartyNotFound");

        return new VoucherCounterpartySnapshot(
            AccountType.Vault, branch.Id, branch.Code, vault.Id, vault.Code);
    }

    /// <summary>Sızıntı guard'ı: karşı taraf fişin şirketine ait olmalı (yabancı şirket = yokmuş gibi).</summary>
    private static void EnsureOwnedByCompany(Guid companyId, Guid ownerCompanyId)
    {
        if (ownerCompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Voucher:CounterpartyNotFound");
        }
    }
}

/// <summary>Fişe yazılacak karşı-taraf snapshot'ı (tip + id'ler + kodlar) — kurulumu
/// <see cref="VoucherCounterpartyResolver"/> yapar, tüketicisi <c>Voucher</c> ctor'udur.</summary>
public record VoucherCounterpartySnapshot(
    AccountType AccountType,
    Guid AccountId,
    string AccountCode,
    Guid SubAccountId,
    string SubAccountCode);
