using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vaults;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Vouchers;

/// <summary>Voucher testlerinin ortak org grafı: Company → Branch → Vault + Account → SubAccount kimlikleri
/// ve host-seed'li birim (TRY/HAS/GUM) Id'leri.</summary>
public sealed record VoucherTestData(
    Guid CompanyId,
    Guid BranchId,
    Guid VaultId,
    Guid AccountId,
    Guid SubAccountId,
    Guid TryUnitId,
    Guid HasUnitId,
    Guid GumUnitId);

/// <summary>
/// Voucher entegrasyon testleri için paylaşımlı veri kurucusu: bir şirket grafını (Company+Branch+Vault+
/// Account+SubAccount) repository'lerle doğrudan kurar (manager/appservice bypass — test verisi).
/// Birimler host seed'inden (CurrencyUnitSeeder) okunur; TAKOZ pseudo-birimi zaten sabittir
/// (<c>BullionConsts.PseudoUnitId</c>) — gerçek CurrencyUnit satırı yoktur, kurulmaz.
/// Bir UnitOfWork İÇİNDEN çağrılmalıdır (testler <c>WithUnitOfWorkAsync</c> ile sarar).
/// </summary>
public class VoucherTestDataSeeder : ITransientDependency
{
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    public VoucherTestDataSeeder(
        IRepository<Company, Guid> companyRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _companyRepository    = companyRepository;
        _branchRepository     = branchRepository;
        _vaultRepository      = vaultRepository;
        _accountRepository    = accountRepository;
        _subAccountRepository = subAccountRepository;
        _unitRepository       = unitRepository;
        _dataFilter           = dataFilter;
    }

    /// <summary>Tam şirket grafını kurar. <paramref name="prefix"/> ile aynı testte ikinci (yabancı)
    /// şirket kurulabilir (company-scope sızıntı senaryoları).</summary>
    public async Task<VoucherTestData> SeedCompanyGraphAsync(string prefix = "TST")
    {
        var (tryId, hasId, gumId) = await ResolveUnitIdsAsync();

        var company = await _companyRepository.InsertAsync(
            new Company($"{prefix}CO", $"{prefix} Company", "TR", tryId, isHeadquarters: true),
            autoSave: true);

        var branch = await _branchRepository.InsertAsync(
            new Branch(company.Id, $"{prefix}BR", $"{prefix} Branch", isHeadquarters: true),
            autoSave: true);

        var vault = await _vaultRepository.InsertAsync(
            new Vault(branch.Id, $"{prefix}VLT", $"{prefix} Vault", isDefault: true),
            autoSave: true);

        var account = await _accountRepository.InsertAsync(
            new Account(company.Id, $"{prefix}ACC", $"{prefix} Account", tryId, tryId),
            autoSave: true);

        var subAccount = await _subAccountRepository.InsertAsync(
            new SubAccount(account.Id, branch.Id, $"{prefix}SUB", $"{prefix} Sub Account"),
            autoSave: true);

        return new VoucherTestData(
            company.Id, branch.Id, vault.Id, account.Id, subAccount.Id, tryId, hasId, gumId);
    }

    /// <summary>Host seed'li (TenantId=null) birimleri kod ile çözer — ambient tenant ne olursa olsun
    /// görünsün diye IMultiTenant filtresi kapatılır (host satırları tenant altında filtrelenir).</summary>
    private async Task<(Guid TryId, Guid HasId, Guid GumId)> ResolveUnitIdsAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tryUnit = await _unitRepository.GetAsync(u => u.Code == CurrencyUnitCode.TRY);
            var hasUnit = await _unitRepository.GetAsync(u => u.Code == CurrencyUnitCode.HAS);
            var gumUnit = await _unitRepository.GetAsync(u => u.Code == CurrencyUnitCode.GUM);
            return (tryUnit.Id, hasUnit.Id, gumUnit.Id);
        }
    }
}

/// <summary>Testlerin ortak satır-DTO fabrikası — poster girdileriyle (birim/işaret/tutar) hizalı minimal satırlar.</summary>
public static class VoucherTestLines
{
    /// <summary>Nakit satırı: CashBalancePoster yalnız PayUnitId/PayTotal'a bakar
    /// (Inbound → +PayTotal ALACAK, aksi → −PayTotal BORÇ; peşin yansımaz).</summary>
    public static VoucherLineDto CashLine(
        VoucherTestData data,
        ProcessDirectionType direction,
        decimal payTotal,
        ProcessPaymentType paymentType = ProcessPaymentType.Normal)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Cash,
            Direction     = direction,
            PaymentType   = paymentType,
            CommodityCode = CurrencyUnitCode.TRY,
            Amount        = payTotal,
            Factor        = 1m,
            Total         = payTotal,
            MainUnitId    = data.TryUnitId,
            PayUnitId     = data.TryUnitId,
            PayTotal      = payTotal,
        };
    }

    /// <summary>Maden satırı (Normal): MetalBalancePoster İKİ bacak yazar —
    /// ana Has (MainUnitId/Total) + işçilik (PayUnitId/PayTotal); Giriş(+)/Çıkış(−).</summary>
    public static VoucherLineDto MetalLine(
        VoucherTestData data,
        ProcessDirectionType direction,
        decimal hasTotal,
        decimal laborTotal)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Metal,
            Direction     = direction,
            PaymentType   = ProcessPaymentType.Normal,
            CommodityCode = CurrencyUnitCode.HAS,
            Amount        = hasTotal,
            Factor        = 1m,
            Total         = hasTotal,
            MainUnitId    = data.HasUnitId,
            PayUnitId     = data.TryUnitId,
            PayTotal      = laborTotal,
        };
    }
}
