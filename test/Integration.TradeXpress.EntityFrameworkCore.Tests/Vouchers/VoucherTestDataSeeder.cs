using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vaults;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

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
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<UserScopedGrant, Guid> _grantRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDataFilter _dataFilter;

    public VoucherTestDataSeeder(
        IRepository<Company, Guid> companyRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<UserScopedGrant, Guid> grantRepository,
        ICurrentUser currentUser,
        IDataFilter dataFilter)
    {
        _companyRepository    = companyRepository;
        _branchRepository     = branchRepository;
        _vaultRepository      = vaultRepository;
        _accountRepository    = accountRepository;
        _subAccountRepository = subAccountRepository;
        _unitRepository       = unitRepository;
        _countryRepository    = countryRepository;
        _grantRepository      = grantRepository;
        _currentUser          = currentUser;
        _dataFilter           = dataFilter;
    }

    /// <summary>Tam şirket grafını kurar. <paramref name="prefix"/> ile aynı testte ikinci (yabancı)
    /// şirket kurulabilir (company-scope sızıntı senaryoları).
    /// <para><paramref name="grantTenantWideAccess"/> (varsayılan true): test kullanıcısına tenant-geneli
    /// (Company/Branch/Vault=null = "her şey") coğrafi Grant seed'ler — üretimdeki ScopedGrantSeeder'ın test
    /// eşdeğeri. Böylece Faz 4 working-context yetki katmanı mevcut voucher testlerinde NO-OP kalır (herkes
    /// tüm şubelere yazar). Yetki DARALTMASI (belirli şube/kasa) test edecek senaryolar bunu false geçip
    /// <see cref="GrantBranchAsync"/>/<see cref="GrantVaultAsync"/> ile dar grant kurar.</para></summary>
    public async Task<VoucherTestData> SeedCompanyGraphAsync(string prefix = "TST", bool grantTenantWideAccess = true)
    {
        if (grantTenantWideAccess)
        {
            await EnsureTenantWideGrantAsync();
        }

        var (tryId, hasId, gumId) = await ResolveUnitIdsAsync();

        // CountryId id-only referanstır (DB FK yok) → voucher senaryosunda sentetik id yeterli
        // (yerel para çözümü bu testlerde kullanılmaz).
        var company = await _companyRepository.InsertAsync(
            new Company($"{prefix}CO", $"{prefix} Company", SimpleGuidGenerator.Instance.Create(), tryId, isHeadquarters: true),
            autoSave: true);

        var branch = await _branchRepository.InsertAsync(
            new Branch(company.Id, $"{prefix}BR", $"{prefix} Branch", isHeadquarters: true),
            autoSave: true);

        var vault = await _vaultRepository.InsertAsync(
            new Vault(company.Id, branch.Id, $"{prefix}VLT", $"{prefix} Vault", isDefault: true),
            autoSave: true);

        var account = await _accountRepository.InsertAsync(
            new Account(company.Id, $"{prefix}ACC", $"{prefix} Account", tryId, tryId),
            autoSave: true);

        var subAccount = await _subAccountRepository.InsertAsync(
            new SubAccount(company.Id, account.Id, branch.Id, $"{prefix}SUB", $"{prefix} Sub Account"),
            autoSave: true);

        return new VoucherTestData(
            company.Id, branch.Id, vault.Id, account.Id, subAccount.Id, tryId, hasId, gumId);
    }

    /// <summary>Aynı şirket altında İKİNCİ bir şube (+ varsayılan kasa) kurar — şube/kasa seviyesi yetki
    /// daraltma testleri için (kullanıcı BranchA'ya grant'lıyken BranchB'ye yazma denemesi). Dönüş:
    /// (branchId, vaultId). UoW içinden çağrılmalıdır.</summary>
    public async Task<(Guid BranchId, Guid VaultId)> SeedExtraBranchAsync(VoucherTestData data, string prefix = "ALT")
    {
        var branch = await _branchRepository.InsertAsync(
            new Branch(data.CompanyId, $"{prefix}BR", $"{prefix} Branch", isHeadquarters: false),
            autoSave: true);

        var vault = await _vaultRepository.InsertAsync(
            new Vault(data.CompanyId, branch.Id, $"{prefix}VLT", $"{prefix} Vault", isDefault: false),
            autoSave: true);

        return (branch.Id, vault.Id);
    }

    /// <summary>AYNI şubede İKİNCİ bir kasa kurar (aynı-şube kasa→kasa transfer senaryosu — slice-1a; hedef
    /// kasa Y). Dönüş: yeni kasa Id'si. UoW içinden çağrılmalıdır.</summary>
    public async Task<Guid> SeedExtraVaultAsync(VoucherTestData data, string prefix = "ALT")
    {
        var vault = await _vaultRepository.InsertAsync(
            new Vault(data.CompanyId, data.BranchId, $"{prefix}VLT", $"{prefix} Vault", isDefault: false),
            autoSave: true);
        return vault.Id;
    }

    /// <summary>Virman testleri için karşı alt hesabı kurar (aynı üst hesap altında ikinci SubAccount —
    /// virman kuralı hesap DEĞİL alt-hesap seviyesinde ayrışır). UoW içinden çağrılmalıdır.</summary>
    public async Task<Guid> SeedCounterSubAccountAsync(VoucherTestData data, string prefix = "CNT")
    {
        var sub = await _subAccountRepository.InsertAsync(
            new SubAccount(data.CompanyId, data.AccountId, data.BranchId, $"{prefix}SUB", $"{prefix} Counter Sub"),
            autoSave: true);
        return sub.Id;
    }

    /// <summary>Şirkete YEREL PARASI ÇÖZÜLEBİLEN gerçek bir ülke bağlar: Country(DefaultCurrencyUnitId=TRY)
    /// insert + Company.CountryId güncellenir → <c>LocalCurrencyResolver</c> TRY'yi çözer; host seed'i TRY'ye
    /// ham 1/1 kur yazdığından değerleme (GetValuationByBaseAsync) dolu döner (tüm birimler 1/1 varsayılan).
    /// Kur-bağımlı senaryolar (muadil maliyet fail-fast'i) çağırır — varsayılan graf sentetik CountryId ile
    /// kalır (yerel para bilinçli ÇÖZÜLMEZ). UoW içinden çağrılmalıdır.</summary>
    public async Task AttachLocalCurrencyCountryAsync(VoucherTestData data, string prefix)
    {
        var country = await _countryRepository.InsertAsync(
            new Country(BuildCountryCode(prefix), $"{prefix} Country", data.TryUnitId),
            autoSave: true);

        var company = await _companyRepository.GetAsync(data.CompanyId);
        company.SetCountry(country.Id);
        await _companyRepository.UpdateAsync(company, autoSave: true);
    }

    /// <summary>Ülke kodu ISO alpha-2 ile sınırlı (CountryConsts.CodeMaxLength=2) — prefix'ten deterministik
    /// 2 harf türetir (aynı prefix aynı kod; farklı prefix'ler pratikte çakışmaz, test DB'leri izole).</summary>
    private static string BuildCountryCode(string prefix)
    {
        var hash = 17;
        foreach (var ch in prefix)
        {
            hash = unchecked(hash * 31 + ch);
        }

        hash = Math.Abs(hash);
        return new string(new[] { (char)('A' + hash % 26), (char)('A' + hash / 26 % 26) });
    }

    /// <summary>Test kullanıcısına tenant-geneli coğrafi Grant garantisi (idempotent) — üretimdeki
    /// <c>ScopedGrantSeeder.EnsureTenantWideGrantsAsync</c>'in test eşdeğeri, ancak IdentityUser satırı
    /// gerektirmeden (test kullanıcısı FakeCurrentPrincipal'dan gelir) doğrudan grant ekler.</summary>
    public async Task EnsureTenantWideGrantAsync()
    {
        var userId = _currentUser.GetId();
        var alreadyGranted = await _grantRepository.AnyAsync(g =>
            g.UserId == userId &&
            g.CompanyId == null &&
            g.BranchId == null &&
            g.VaultId == null &&
            g.Mode == ScopedGrantMode.Grant);
        if (alreadyGranted)
        {
            return;
        }

        await InsertGrantAsync(companyId: null, branchId: null, vaultId: null);
    }

    /// <summary>Test kullanıcısına belirli bir ŞUBE için coğrafi Grant ekler (yetki daraltma senaryoları).</summary>
    public async Task GrantBranchAsync(Guid companyId, Guid branchId)
    {
        await InsertGrantAsync(companyId, branchId, vaultId: null);
    }

    /// <summary>Test kullanıcısına belirli bir KASA için coğrafi Grant ekler (yetki daraltma senaryoları).</summary>
    public async Task GrantVaultAsync(Guid companyId, Guid branchId, Guid vaultId)
    {
        await InsertGrantAsync(companyId, branchId, vaultId, ScopedGrantMode.Grant);
    }

    /// <summary>Test kullanıcısına belirli bir KASA için Deny ekler — şube Grant'ı altında tek bir kasayı
    /// kapatmak için (kasa-seviyesi yetki daraltmasının test edilmesi).</summary>
    public async Task DenyVaultAsync(Guid companyId, Guid branchId, Guid vaultId)
    {
        await InsertGrantAsync(companyId, branchId, vaultId, ScopedGrantMode.Deny);
    }

    /// <summary>Coğrafi-only (rol/izin taşımayan) grant ekler — yalnız Company/Branch/Vault kapsamı + Mode.</summary>
    private async Task InsertGrantAsync(Guid? companyId, Guid? branchId, Guid? vaultId, ScopedGrantMode mode = ScopedGrantMode.Grant)
    {
        await _grantRepository.InsertAsync(
            new UserScopedGrant(
                userId: _currentUser.GetId(),
                roleId: null,
                permissionName: null,
                companyId: companyId,
                branchId: branchId,
                vaultId: vaultId,
                mode: mode),
            autoSave: true);
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

    /// <summary>Dekont satırı: DebitNoteBalancePoster PayUnitId/PayTotal'a bakar; Miktar YOK (0) —
    /// Giriş(ALACAK) → +PayTotal, Çıkış(BORÇ) → −PayTotal; PEŞİN MUAFİYETİ YOK (daima bakiyeye yazar).</summary>
    public static VoucherLineDto DebitNoteLine(
        VoucherTestData data,
        ProcessDirectionType direction,
        decimal payTotal)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.DebitNote,
            Direction     = direction,
            CommodityCode = "DEVIR",         // legacy kategori örneği
            Amount        = 0m,              // Miktar alanı yok — 0 gider (tip-bazlı muafiyet)
            PayUnitId     = data.TryUnitId,
            PayFactor     = payTotal,
            PayTotal      = payTotal,
        };
    }

    /// <summary>Virman satırı: TransferBalancePoster PayUnitId/PayTotal'a bakar; Miktar YOK (0) —
    /// Giriş(ALACAK) → +PayTotal, Çıkış(BORÇ) → −PayTotal. Sunucu karşı hesabın KENDİ fişinde zıt
    /// yönlü ikizi (aynı LinkId) açar; çift etki iki satırdan doğar.</summary>
    public static VoucherLineDto TransferLine(
        VoucherTestData data,
        Guid counterSubAccountId,
        ProcessDirectionType direction,
        decimal payTotal)
    {
        return new VoucherLineDto
        {
            BranchId         = data.BranchId,
            VaultId          = data.VaultId,
            AccountId        = data.AccountId,
            SubAccountId     = data.SubAccountId,
            Type             = ProcessType.Transfer,
            Direction        = direction,
            PaymentType      = ProcessPaymentType.Normal,   // kısaltma kodu VGN/VCN'in "N"i
            Amount           = 0m,                          // Miktar alanı yok — 0 gider (tip-bazlı muafiyet)
            PayUnitId        = data.TryUnitId,
            PayFactor        = payTotal,
            PayTotal         = payTotal,
            CounterAccountId = counterSubAccountId,
        };
    }

    /// <summary>Takoz GİRİŞ satırı (külçe): stok + çeşni havuzunun kaynağı — metal ölçüleri girişte otoritedir.</summary>
    public static VoucherLineDto BullionEntryLine(
        VoucherTestData data,
        string code,
        decimal amount)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Bullion,
            Direction     = ProcessDirectionType.Inbound,
            CommodityCode = code,
            Amount        = amount,
            Factor        = 0.916m,
            SilverFactor  = 0.04m,
            AssayAmount   = 5m,
            MainUnitId    = data.HasUnitId,
        };
    }

    /// <summary>Takoz ÇIKIŞ satırı: istemci yalnız külçe referansı (CommodityId) gönderir — metal verisi
    /// sunucuda GİRİŞ satırından kopyalanır (PrepareBullionExitLineAsync sözleşmesi).</summary>
    public static VoucherLineDto BullionExitLine(VoucherTestData data, Guid entryLineId)
    {
        return new VoucherLineDto
        {
            BranchId     = data.BranchId,
            VaultId      = data.VaultId,
            AccountId    = data.AccountId,
            SubAccountId = data.SubAccountId,
            Type         = ProcessType.Bullion,
            Direction    = ProcessDirectionType.Outbound,
            CommodityId  = entryLineId,
        };
    }

    /// <summary>Çeşni satırı (yön SABİT ÇIKIŞ): AssayBalancePoster HAS'a −(Miktar×Factor),
    /// GUM'a −(Miktar×SilverFactor) postlar; para bacağı yok (Total=PayTotal=0).</summary>
    public static VoucherLineDto AssayLine(
        VoucherTestData data,
        decimal amount,
        decimal auMilyem,
        decimal agMilyem)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Assay,
            Direction     = ProcessDirectionType.Outbound,
            CommodityCode = "CESNI",
            Amount        = amount,
            Factor        = auMilyem,
            SilverFactor  = agMilyem,
            MainUnitId    = data.HasUnitId,
            SilverUnitId  = data.GumUnitId,
        };
    }
}
