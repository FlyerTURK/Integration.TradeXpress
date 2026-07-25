using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Accounts;

/// <summary>
/// Account CRUD — <b>per-tenant + company-scoped</b> (liste <see cref="AccountListRequestDto.CompanyId"/> ile
/// daraltılır). Bakiye/limit para birimleri (host‖tenant CurrencyUnit) ZORUNLU ve görünürlük kapsamında
/// (global ‖ own) doğrulanır; kodları liste/get'te zenginleştirilir. Alt hesabı olan hesap silinemez.
/// </summary>
[Authorize(TradeXpressPermissions.Accounts.Default)]
public class AccountAppService : TradeXpressAppService, IAccountAppService
{
    private readonly IRepository<Account, Guid> _repository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;   // yalnız OKUMA (graf projeksiyonu + sil)
    private readonly ISubAccountAppService _subAccountAppService;           // YAZMA: alt hesap create/update/delete buraya delege
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;                       // güvenlik sınırı: working-context zorlaması

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "CompanyCode", "IsActive", "Limit", "Id", "CompanyId" };

    public AccountAppService(
        IRepository<Account, Guid> repository,
        IRepository<Company, Guid> companyRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        ISubAccountAppService subAccountAppService,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _companyRepository = companyRepository;
        _unitRepository = unitRepository;
        _subAccountRepository = subAccountRepository;
        _subAccountAppService = subAccountAppService;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<AccountListDto>> GetListAsync(AccountListRequestDto input)
    {
        var companies = await _companyRepository.GetQueryableAsync();
        var query = await _repository.GetQueryableAsync();
        if (input.CompanyId.HasValue)
            query = query.Where(a => a.CompanyId == input.CompanyId.Value);

        var rows = query
            .Join(companies, a => a.CompanyId, c => c.Id, (a, c) => new AccountListRow
            {
                Id = a.Id,
                CompanyId = a.CompanyId,
                CompanyCode = c.Code,
                Code = a.Code,
                Name = a.Name,
                BalanceCurrencyUnitId = a.BalanceCurrencyUnitId,
                Limit = a.Limit,
                LimitUnitId = a.LimitUnitId,
                IsActive = a.IsActive,
            })
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(rows);
        var items = await AsyncExecuter.ToListAsync(rows.ApplyPaging(input));

        var codes = await LoadCurrencyCodesAsync(
            items.SelectMany(r => new[] { r.BalanceCurrencyUnitId, r.LimitUnitId }));

        return new PagedResultDto<AccountListDto>(
            totalCount,
            items.Select(r => new AccountListDto
            {
                Id = r.Id,
                CompanyId = r.CompanyId,
                CompanyCode = r.CompanyCode,
                Code = r.Code,
                Name = r.Name,
                BalanceCurrencyUnitId = r.BalanceCurrencyUnitId,
                BalanceCurrencyCode = codes.GetValueOrDefault(r.BalanceCurrencyUnitId),
                Limit = r.Limit,
                LimitUnitId = r.LimitUnitId,
                LimitCurrencyCode = codes.GetValueOrDefault(r.LimitUnitId),
                IsActive = r.IsActive,
            }).ToList());
    }

    public virtual async Task<AccountGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.Accounts.Create)]
    public virtual async Task<AccountGetDto> CreateAsync(AccountCreateDto input)
    {
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        // Güvenlik sınırı (Voucher deseniyle aynı, fail-closed): CompanyId client'tan gelir ama
        // working-context'e EŞLEŞMELİ — sahte CompanyId ile başka şirkete hesap açılması engellenir.
        var companyId = EnsureCurrentCompanyId();
        if (input.CompanyId != companyId)
            throw new BusinessException("TradeXpress:Account:CompanyContextMismatch");

        await EnsureCompanyVisibleAsync(companyId);
        var balanceUnitId = await ResolveCurrencyAsync(input.BalanceCurrencyUnitId);
        var limitUnitId = await ResolveCurrencyAsync(input.LimitUnitId);

        // Benzersizlik ÖN-kontrolü (ürün kuralı hizası): aynı şirkette aynı kodlu hesap → dostane hata,
        // ham DB (TenantId,CompanyId,Code) unique çakışması değil. Update'le simetrik (kendisi yok → excludeId boş).
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Account.Code), EntityFieldConsts.CodeMinLength, AccountConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new Account(
            companyId,
            input.Code,
            input.Name,
            balanceUnitId,
            limitUnitId,
            input.Limit);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        await SaveSubAccountsAsync(entity.Id, input.SubAccounts);
        await EnsureDefaultSubAccountAsync(entity.Id);   // en az 1 alt hesap (ANAHESAP) garantisi
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Accounts.Update)]
    public virtual async Task<AccountGetDto> UpdateAsync(Guid id, AccountUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        var balanceUnitId = await ResolveCurrencyAsync(input.BalanceCurrencyUnitId);
        var limitUnitId = await ResolveCurrencyAsync(input.LimitUnitId);

        await ApplyCodeChangeAsync(entity, input.Code);

        entity.SetName(input.Name);
        entity.SetBalanceCurrencyUnit(balanceUnitId);
        entity.SetLimit(input.Limit);
        entity.SetLimitUnit(limitUnitId);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        await SaveSubAccountsAsync(entity.Id, input.SubAccounts);
        await EnsureDefaultSubAccountAsync(entity.Id);   // hiçbir koşulda alt hesapsız kalmasın
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Accounts.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı: hesabı ÖNCE yükle — company query filter yabancı şirketin hesabını gizler →
        // EntityNotFoundException. Bu doğrulama alt-hesap silmeden ÖNCE olmalı; aksi hâlde yabancı hesabın
        // alt hesapları (SubAccount company-filtreli değil, yalnız tenant) yıkıcı biçimde silinebilirdi.
        var entity = await _repository.GetAsync(id);

        // Alt hesapları da sil (cascade) — hesap silinince çocuksuz kalsın.
        await _subAccountRepository.DeleteAsync(s => s.AccountId == entity.Id, autoSave: true);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04: CurrencyUnit host kayıtları dışında tüm kodlar
    /// düzenlenebilir): normalize et → değiştiyse AYNI ŞİRKET altında benzersizliği doğrula (kendisi hariç;
    /// dostane hata, ham DB çakışması değil — (TenantId, CompanyId, Code) unique index'iyle hizalı) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Account entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, AccountConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId,CompanyId,Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty (kendisi yok), Update'te entity.Id (kendisi hariç).
    /// Dostane BusinessException — ham DB çakışmasını önler.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(a => a.CompanyId == companyId && a.Id != excludeId && a.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Account:CodeAlreadyExists");
        }
    }

    // ── alt hesap grafı diff (Id + IsDeleted) → SubAccountAppService'e DELEGE ───
    private async Task SaveSubAccountsAsync(Guid accountId, System.Collections.Generic.List<SubAccountGraphDto> subAccounts)
    {
        if (subAccounts == null) return;

        // Önce ekle + güncelle, sonra sil (Branch→Vault deseniyle aynı).
        foreach (var s in subAccounts.Where(x => !x.IsDeleted))
        {
            if (s.Id == Guid.Empty)
            {
                await _subAccountAppService.CreateAsync(new SubAccountCreateDto
                {
                    AccountId = accountId,
                    BranchId = null,            // drill'de şube atanmaz (nullable)
                    Code = s.Code,
                    Name = s.Name,
                    Description = s.Description,
                });
            }
            else
            {
                await _subAccountAppService.UpdateAsync(s.Id, new SubAccountUpdateDto
                {
                    Code = s.Code,          // kod düzenlenebilir (drill'deki değişiklik kaybolmasın)
                    Name = s.Name,
                    Description = s.Description,
                    IsActive = s.IsActive,
                });
            }
        }

        foreach (var s in subAccounts.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _subAccountAppService.DeleteAsync(s.Id);
        }
    }

    /// <summary>Hesabın hiç alt hesabı yoksa varsayılan "ANAHESAP" (BranchId=null) alt hesabını ekler (en az 1 kuralı).</summary>
    private async Task EnsureDefaultSubAccountAsync(Guid accountId)
    {
        var any = await AsyncExecuter.AnyAsync(
            (await _subAccountRepository.GetQueryableAsync()).Where(s => s.AccountId == accountId));
        if (any) return;

        await _subAccountAppService.CreateAsync(new SubAccountCreateDto
        {
            AccountId = accountId,
            BranchId = null,
            Code = AccountConsts.DefaultSubAccountCode,
            Name = AccountConsts.DefaultSubAccountName,
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sızıntı önleme (Voucher/BalanceSheet ile aynı desen): aktif şirket working-context'ten
    /// (<see cref="ICurrentCompany"/>) zorlanır; yoksa fail-closed. Konsolide (context yok) yazma yapılamaz.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Account:CompanyContextRequired");

        return companyId;
    }

    private async Task EnsureCompanyVisibleAsync(Guid companyId)
    {
        if (companyId == Guid.Empty || await _companyRepository.FindAsync(companyId) == null)
            throw new EntityNotFoundException(typeof(Company), companyId);
    }

    /// <summary>Para birimi zorunlu + görünürlük kapsamında (global ‖ own) var olmalı. Geçerli Id'yi döndürür.</summary>
    private async Task<Guid> ResolveCurrencyAsync(Guid? currencyUnitId)
    {
        if (currencyUnitId is not { } id || id == Guid.Empty)
            throw new BusinessException("TradeXpress:Account:CurrencyRequired");

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var exists = await AsyncExecuter.AnyAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == id && (u.TenantId == null || u.TenantId == tenantId)));
            if (!exists)
                throw new EntityNotFoundException(typeof(CurrencyUnit), id);
        }

        return id;
    }

    private async Task<Dictionary<Guid, string>> LoadCurrencyCodesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync()).Where(u => list.Contains(u.Id)));
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    private async Task<AccountGetDto> ToGetDtoAsync(Account a)
    {
        var companyCode = await AsyncExecuter.FirstOrDefaultAsync(
            (await _companyRepository.GetQueryableAsync()).Where(c => c.Id == a.CompanyId).Select(c => c.Code));
        var codes = await LoadCurrencyCodesAsync(new[] { a.BalanceCurrencyUnitId, a.LimitUnitId });

        var subs = await AsyncExecuter.ToListAsync(
            (await _subAccountRepository.GetQueryableAsync()).Where(s => s.AccountId == a.Id).OrderBy(s => s.Code));

        return new AccountGetDto
        {
            Id = a.Id,
            CompanyId = a.CompanyId,
            CompanyCode = companyCode ?? string.Empty,
            Code = a.Code,
            Name = a.Name,
            BalanceCurrencyUnitId = a.BalanceCurrencyUnitId,
            BalanceCurrencyCode = codes.GetValueOrDefault(a.BalanceCurrencyUnitId),
            Limit = a.Limit,
            LimitUnitId = a.LimitUnitId,
            LimitCurrencyCode = codes.GetValueOrDefault(a.LimitUnitId),
            Description = a.Description,
            IsActive = a.IsActive,
            SubAccounts = subs.Select(s => new SubAccountGraphDto
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                Description = s.Description,
                IsActive = s.IsActive,
            }).ToList(),
        };
    }

    private sealed class AccountListRow
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid BalanceCurrencyUnitId { get; set; }
        public decimal Limit { get; set; }
        public Guid LimitUnitId { get; set; }
        public bool IsActive { get; set; }
    }
}
