using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş satırı DTO'larının DB'de saklanmayan (denormalize) gösterim alanlarını okuma anında çözer:
/// birim kodları (MainUnitCode/PayUnitCode), virman karşı hesap kodu, satırı yazan kullanıcı adı,
/// hesabın bakiye birimi ve bakiye ekranlarının görünür-birim sırası. Durumsuz; multi-tenant filter
/// scope'larını kendi açar (birim kataloğu host‖own görünür).
/// </summary>
public class VoucherCodeResolver : ITransientDependency
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IAbpLazyServiceProvider _lazyServiceProvider;

    public VoucherCodeResolver(
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<Company, Guid> companyRepository,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter,
        IAbpLazyServiceProvider lazyServiceProvider)
    {
        _unitRepository       = unitRepository;
        _subAccountRepository = subAccountRepository;
        _accountRepository    = accountRepository;
        _companyRepository    = companyRepository;
        _dataFilter           = dataFilter;
        _currentTenant        = currentTenant;
        _asyncExecuter        = asyncExecuter;
        _lazyServiceProvider  = lazyServiceProvider;
    }

    /// <summary>MainUnitCode / PayUnitCode'u (DB'de saklanmaz) PayUnitId/MainUnitId'den okuma anında çözer.</summary>
    public async Task ResolveUnitCodesAsync(List<VoucherLineDto> dtos)
    {
        var unitIds = dtos.Select(d => d.MainUnitId)
            .Concat(dtos.Where(d => d.PayUnitId.HasValue).Select(d => d.PayUnitId!.Value))
            .Where(id => id != Guid.Empty)
            .Distinct().ToList();
        if (unitIds.Count == 0)
        {
            return;
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var codeMap = (await _asyncExecuter.ToListAsync(
                    (await _unitRepository.GetQueryableAsync())
                        .Where(u => unitIds.Contains(u.Id))
                        .Select(u => new { u.Id, u.Code })))
                .ToDictionary(x => x.Id, x => x.Code);

            foreach (var d in dtos)
            {
                if (codeMap.TryGetValue(d.MainUnitId, out var mc))
                {
                    d.MainUnitCode = mc;
                }

                if (d.PayUnitId is { } pid && codeMap.TryGetValue(pid, out var pc))
                {
                    d.PayUnitCode = pc;
                }
            }
        }
    }

    /// <summary>Virman satırlarının karşı hesap kodunu (DB'de saklanmaz) CounterAccountId'den okuma
    /// anında çözer — grid "Karşı Hesap" kolonu (MainUnitCode/PayUnitCode ile aynı desen).</summary>
    public async Task ResolveCounterAccountCodesAsync(List<VoucherLineDto> dtos)
    {
        var counterIds = dtos.Where(d => d.CounterAccountId.HasValue)
            .Select(d => d.CounterAccountId!.Value)
            .Distinct().ToList();
        if (counterIds.Count == 0)
        {
            return;
        }

        var codeMap = (await _asyncExecuter.ToListAsync(
                (await _subAccountRepository.GetQueryableAsync())
                    .Where(s => counterIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.Code })))
            .ToDictionary(x => x.Id, x => x.Code);

        foreach (var d in dtos)
        {
            if (d.CounterAccountId is { } cid && codeMap.TryGetValue(cid, out var code))
            {
                d.CounterAccountCode = code;
            }
        }
    }

    /// <summary>Satırları yazan kullanıcıların adlarını (CreatorId → UserName) doldurur.</summary>
    public async Task ResolveCreatorNamesAsync(List<VoucherLineDto> dtos)
    {
        var creatorIds = dtos.Where(x => x.CreatorId.HasValue).Select(x => x.CreatorId!.Value).Distinct().ToList();
        if (creatorIds.Count == 0)
        {
            return;
        }

        var userRepo = _lazyServiceProvider.LazyGetService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>();
        if (userRepo == null)
        {
            return;
        }

        var users = await _asyncExecuter.ToListAsync(
            (await userRepo.GetQueryableAsync()).Where(u => creatorIds.Contains(u.Id)));
        var userDict = users.ToDictionary(u => u.Id, u => u.UserName);

        foreach (var dto in dtos)
        {
            if (dto.CreatorId.HasValue && userDict.TryGetValue(dto.CreatorId.Value, out var name))
            {
                dto.CreatorName = name;
            }
        }
    }

    /// <summary>Tek birimin kodunu çözer (host‖own katalog scope'unda).</summary>
    public async Task<string?> ResolveUnitCodeAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await _asyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == unitId)
                    .Select(u => u.Code));
        }
    }

    /// <summary>Karşı tarafın bakiye para birimi — TİPE göre kaynak değişir:
    /// <list type="bullet">
    /// <item><b>Cari:</b> SubAccount → Account → BalanceCurrencyUnit (bugünkü davranış, aynen).</item>
    /// <item><b>Kasa:</b> cari YOKTUR (<paramref name="subAccountId"/> burada bir KASA id'sidir) →
    /// şirketin bilanço/base birimi. Bu, emekli edilen sahte vault-cari'nin taşıdığı birimin AYNISIDIR
    /// (<c>OrgTreeManager</c> onu company base'iyle kuruyordu) → kasa bakiye ekranının birimi model
    /// değişiminden ETKİLENMEZ.</item>
    /// </list></summary>
    public async Task<(Guid Id, string Code)> ResolveBalanceUnitAsync(
        Guid companyId, AccountType accountType, Guid subAccountId)
    {
        if (accountType == AccountType.Vault)
        {
            var company = await _companyRepository.FindAsync(companyId);
            if (company is null || company.BaseCurrencyUnitId == Guid.Empty)
            {
                return (Guid.Empty, string.Empty);
            }

            var baseCode = await ResolveUnitCodeAsync(company.BaseCurrencyUnitId);
            return (company.BaseCurrencyUnitId, baseCode ?? string.Empty);
        }

        return await ResolveBalanceUnitAsync(subAccountId);
    }

    /// <summary>Hesabın bakiye para birimi (konsolide hedefi): SubAccount → Account → BalanceCurrencyUnit.</summary>
    public async Task<(Guid Id, string Code)> ResolveBalanceUnitAsync(Guid subAccountId)
    {
        var sub = await _subAccountRepository.FindAsync(subAccountId);
        if (sub is null)
        {
            return (Guid.Empty, string.Empty);
        }

        var account = await _accountRepository.FindAsync(sub.AccountId);
        if (account is null)
        {
            return (Guid.Empty, string.Empty);
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var code = await _asyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == account.BalanceCurrencyUnitId)
                    .Select(u => u.Code));
            return (account.BalanceCurrencyUnitId, code ?? string.Empty);
        }
    }

    /// <summary>Bakiye Gösterim Modu = AccountScoped iken bakiye birimi: <paramref name="accountId"/> tip-agnostik
    /// (cari kipte Account, iç kipte Şube) — <see cref="ResolveBalanceUnitAsync(Guid,AccountType,Guid)"/> ile
    /// AYNI kaynaklar, ama SubAccount/Kasa'ya değil doğrudan üst kimliğe göre çözülür (tek alt hesap/kasa şart
    /// değil — konsolide görünümün amacı budur).</summary>
    public async Task<(Guid Id, string Code)> ResolveBalanceUnitByAccountScopeAsync(
        Guid companyId, AccountType accountType, Guid accountId)
    {
        if (accountType == AccountType.Vault)
        {
            var company = await _companyRepository.FindAsync(companyId);
            if (company is null || company.BaseCurrencyUnitId == Guid.Empty)
            {
                return (Guid.Empty, string.Empty);
            }

            var baseCode = await ResolveUnitCodeAsync(company.BaseCurrencyUnitId);
            return (company.BaseCurrencyUnitId, baseCode ?? string.Empty);
        }

        var account = await _accountRepository.FindAsync(accountId);
        if (account is null)
        {
            return (Guid.Empty, string.Empty);
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var code = await _asyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == account.BalanceCurrencyUnitId)
                    .Select(u => u.Code));
            return (account.BalanceCurrencyUnitId, code ?? string.Empty);
        }
    }

    /// <summary>Görünür birimleri (host‖own) gösterim sırasıyla döndürür: her zaman gösterilecekler
    /// (AlwaysShowInBalance) + <paramref name="includeIds"/> (hareketi olanlar). TAKOZ pseudo-birim
    /// daima en başta.</summary>
    public async Task<List<(Guid Id, string Code)>> OrderedVisibleUnitsAsync(IEnumerable<Guid> includeIds)
    {
        var ids = includeIds.ToList();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = _currentTenant.Id;
            var units = await _unitRepository.GetQueryableAsync();
            var ordered = await _asyncExecuter.ToListAsync(
                units.Where(u => (u.TenantId == null || u.TenantId == tenantId)
                              && (u.AlwaysShowInBalance || ids.Contains(u.Id)))
                     .OrderBy(u => u.TenantId == null ? 0 : 1)
                     .ThenByDescending(u => u.AlwaysShowInBalance)
                     .ThenBy(u => u.DisplayOrder)
                     .ThenBy(u => u.Code)
                     .Select(u => new { u.Id, u.Code }));

            var result = ordered.Select(u => (u.Id, u.Code)).ToList();

            // TAKOZ pseudo-birim (gerçek CurrencyUnit DEĞİL → tabloda yok): bakiye listesinde DAİMA EN BAŞTA
            // görünür (bakiye olmasa da 0; kullanıcı kararı). Önce varsa çıkar, sonra başa ekle.
            result.RemoveAll(r => r.Id == BullionConsts.PseudoUnitId);
            result.Insert(0, (BullionConsts.PseudoUnitId, CurrencyUnitCode.Bullion));

            return result;
        }
    }
}
