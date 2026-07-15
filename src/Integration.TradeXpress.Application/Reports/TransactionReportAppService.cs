using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Cari-hesap-BAĞIMSIZ işlem raporu. Kapsam Voucher header'ından: şirket DAİMA
/// <see cref="ICurrentCompany"/>'den zorlanır (client'a güvenilmez — BalanceSheet/CashReport deseni),
/// Branch/Vault opsiyonel filtre. Sorgu SQL-side projeksiyon + server-side sayfalama
/// (satır entity'leri belleğe ÇEKİLMEZ — K4 dersi); kod alanları sayfa sonrası az-sorgulu lookup'la dolar.
/// </summary>
[Authorize(TradeXpressPermissions.Reports.Transactions)]
public class TransactionReportAppService : TradeXpressAppService, ITransactionReportAppService
{
    private const int MaxPageSize = 10_000;   // Excel export tüm-satır çekişini de karşılar (üst sınırlı)

    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    public TransactionReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository = voucherRepository;
        _accountRepository = accountRepository;
        _subAccountRepository = subAccountRepository;
        _branchRepository = branchRepository;
        _vaultRepository = vaultRepository;
        _unitRepository = unitRepository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<TransactionReportRowDto>> GetListAsync(TransactionReportRequestDto request)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
        {
            return new PagedResultDto<TransactionReportRowDto>(0, new List<TransactionReportRowDto>());
        }

        var start = request.Start;
        var endExclusive = request.EndExclusive;

        var q = await _voucherRepository.GetQueryableAsync();
        var lines =
            from v in q
            where v.CompanyId == companyId
               && (request.BranchId == null || v.BranchId == request.BranchId)
               && (request.VaultId == null || v.VaultId == request.VaultId)
               && (request.SubAccountId == null || v.SubAccountId == request.SubAccountId)
               && v.VoucherDate >= start && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted
            select new
            {
                v.VoucherDate, v.VoucherNumber,
                // Karşı taraf kodları fişin KENDİ snapshot'ından gelir (Account/SubAccount join'i YOK):
                // alanlar polimorfiktir (kasa kipinde Şube/Kasa kodu) → join zaten eşleşmezdi.
                v.AccountCode, v.SubAccountCode, v.BranchId, v.VaultId,
                l.Type, l.Direction, l.PaymentType,
                l.CommodityCode, l.Quantity, l.Amount, l.Total, l.MainUnitId,
                l.PayTotal, l.PayUnitId, l.CounterAccountId,
                l.Description, l.CreationTime, l.CreatorId, LineId = l.Id,
            };

        if (request.Types is { Count: > 0 })
        {
            var types = request.Types;
            lines = lines.Where(x => types.Contains(x.Type));
        }

        var totalCount = await AsyncExecuter.LongCountAsync(lines);

        var take = Math.Clamp(request.MaxResultCount, 1, MaxPageSize);
        var skip = Math.Max(request.SkipCount, 0);
        var page = await AsyncExecuter.ToListAsync(
            lines.OrderBy(x => x.VoucherDate)
                 .ThenBy(x => x.CreationTime)
                 .ThenBy(x => x.LineId)
                 .Skip(skip)
                 .Take(take));

        // ── Kod lookup'ları (sayfa üstünden, az sorgu) ──
        var unitCodes = await CodeMapAsync(
            _unitRepository,
            page.Select(x => x.MainUnitId).Concat(page.Where(x => x.PayUnitId != null).Select(x => x.PayUnitId!.Value)),
            u => u.Id, u => u.Code, disableMultiTenant: true);
        // Alt hesap kodları: YALNIZ virman karşı hesabı için (fişin kendi karşı-taraf kodları artık
        // snapshot — lookup gerekmiyor).
        var subCodes = await CodeMapAsync(
            _subAccountRepository,
            page.Where(x => x.CounterAccountId != null).Select(x => x.CounterAccountId!.Value),
            x => x.Id, x => x.Code);
        var branchCodes = await CodeMapAsync(_branchRepository, page.Select(x => x.BranchId), x => x.Id, x => x.Code);
        var vaultCodes = await CodeMapAsync(
            _vaultRepository, page.Where(x => x.VaultId != null).Select(x => x.VaultId!.Value),
            x => x.Id, x => x.Code);
        var creatorNames = await CreatorNameMapAsync(
            page.Where(x => x.CreatorId != null).Select(x => x.CreatorId!.Value));

        var rows = new List<TransactionReportRowDto>(page.Count);
        foreach (var x in page)
        {
            rows.Add(new TransactionReportRowDto
            {
                VoucherDate    = x.VoucherDate,
                VoucherNumber  = x.VoucherNumber,
                ProcessCode    = VoucherProcessCode.Of(x.Type, x.Direction, x.PaymentType),
                AccountCode    = x.AccountCode,
                SubAccountCode = x.SubAccountCode,
                CounterAccountCode = x.CounterAccountId is { } cnt ? subCodes.GetValueOrDefault(cnt) : null,
                BranchCode     = branchCodes.GetValueOrDefault(x.BranchId),
                VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
                CommodityCode  = x.CommodityCode,
                Quantity       = x.Quantity,
                Amount         = x.Amount,
                Total          = x.Total,
                MainUnitCode   = unitCodes.GetValueOrDefault(x.MainUnitId),
                PayTotal       = x.PayTotal,
                PayUnitCode    = x.PayUnitId is { } p ? unitCodes.GetValueOrDefault(p) : null,
                Description    = x.Description,
                CreatorName    = x.CreatorId is { } c ? creatorNames.GetValueOrDefault(c) : null,
            });
        }

        return new PagedResultDto<TransactionReportRowDto>(totalCount, rows);
    }

    /// <summary>Kullanıcı adları (CreatorId → UserName). Identity modülü yoksa boş harita (VoucherAppService deseni).</summary>
    private async Task<Dictionary<Guid, string>> CreatorNameMapAsync(IEnumerable<Guid> creatorIds)
    {
        var ids = creatorIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var userRepo = LazyServiceProvider.LazyGetService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>();
        if (userRepo == null)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await AsyncExecuter.ToListAsync(
            (await userRepo.GetQueryableAsync()).Where(u => ids.Contains(u.Id)));
        return users.ToDictionary(u => u.Id, u => u.UserName);
    }

    private async Task<Dictionary<Guid, string>> CodeMapAsync<T>(
        IRepository<T, Guid> repo, IEnumerable<Guid> ids, Func<T, Guid> keyOf, Func<T, string> codeOf,
        bool disableMultiTenant = false)
        where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        if (disableMultiTenant)
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var globalRows = await AsyncExecuter.ToListAsync(
                    (await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
                return globalRows.ToDictionary(keyOf, codeOf);
            }
        }

        var rows = await AsyncExecuter.ToListAsync(
            (await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows.ToDictionary(keyOf, codeOf);
    }
}
