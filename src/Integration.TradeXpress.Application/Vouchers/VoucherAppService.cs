using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş + satır servisi. VoucherNumber = MAX(VoucherNumber for company) + 1 (lazy, ilk satırda).
/// Cash satırları WYSIWYG kaydedilir (istemci aynı motoru çalıştırır; sunucu recompute YOK).
/// Liste/edit formu YOK — Cari İşlemler formu doğrudan bu servisi kullanır.
/// </summary>
[Authorize]
public class VoucherAppService : TradeXpressAppService, IVoucherAppService
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly VoucherBalanceCalculator _balanceCalculator;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly IDataFilter _dataFilter;

    public VoucherAppService(
        IRepository<Voucher, Guid> repository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        VoucherBalanceCalculator balanceCalculator,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        IDataFilter dataFilter)
    {
        _repository           = repository;
        _branchRepository     = branchRepository;
        _vaultRepository      = vaultRepository;
        _unitRepository       = unitRepository;
        _subAccountRepository = subAccountRepository;
        _accountRepository    = accountRepository;
        _balanceCalculator    = balanceCalculator;
        _ledgerSynchronizer   = ledgerSynchronizer;
        _dataFilter           = dataFilter;
    }

    public async Task<VoucherGetDto> CreateAsync(VoucherCreateDto input)
    {
        var maxNumber = await NextNumberAsync(input.CompanyId);

        var entity = new Voucher(
            input.CompanyId,
            input.BranchId,
            input.VaultId,
            input.AccountId,
            input.SubAccountId,
            maxNumber,
            input.VoucherDate,
            input.Description);

        await _repository.InsertAsync(entity, autoSave: true);

        return new VoucherGetDto
        {
            Id            = entity.Id,
            CompanyId     = entity.CompanyId,
            BranchId      = entity.BranchId,
            VaultId       = entity.VaultId,
            AccountId     = entity.AccountId,
            SubAccountId  = entity.SubAccountId,
            VoucherNumber = entity.VoucherNumber,
            VoucherDate   = entity.VoucherDate,
            Description   = entity.Description,
        };
    }

    public async Task<VoucherLineDto> SaveLineAsync(VoucherLineDto input)
    {
        // WYSIWYG: ekranda görünen değerler AYNEN kaydedilir (sunucu recompute yok).
        var lineInput = ToLineInput(input);

        Voucher voucher;
        Guid lineId;

        if (input.VoucherId is { } voucherId)
        {
            voucher = await _repository.GetAsync(voucherId);
            await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

            if (input.Id != Guid.Empty)
            {
                voucher.UpdateLine(input.Id, lineInput);
                lineId = input.Id;
            }
            else
            {
                lineId = voucher.AddLine(GuidGenerator.Create(), lineInput).Id;
            }

            await _repository.UpdateAsync(voucher, autoSave: true);
        }
        else
        {
            // Fiş lazy oluşturulur + numara atanır.
            voucher = new Voucher(
                input.CompanyId,
                input.BranchId,
                input.VaultId,
                input.AccountId,
                input.SubAccountId,
                await NextNumberAsync(input.CompanyId),
                input.VoucherDate,
                input.VoucherDescription);

            lineId = voucher.AddLine(GuidGenerator.Create(), lineInput).Id;
            await _repository.InsertAsync(voucher, autoSave: true);
        }

        // Ledger senkronu (poster çıktısı → kalıcı): voucher kaydedildikten sonra, aynı UoW içinde.
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        input.Id            = lineId;
        input.VoucherId     = voucher.Id;
        input.VoucherNumber = voucher.VoucherNumber;
        return input;
    }

    public async Task<PagedResultDto<VoucherListDto>> GetListAsync(VoucherListRequestDto input)
    {
        var voucherQ = (await _repository.GetQueryableAsync())
            .Where(v => v.SubAccountId == input.SubAccountId)
            .OrderByDescending(v => v.VoucherDate);

        var branchQ = await _branchRepository.GetQueryableAsync();
        var vaultQ  = await _vaultRepository.GetQueryableAsync();

        var joined = from v in voucherQ
                     join b in branchQ on v.BranchId equals b.Id into bj
                     from b in bj.DefaultIfEmpty()
                     join vault in vaultQ on v.VaultId equals vault.Id into vj
                     from vault in vj.DefaultIfEmpty()
                     select new
                     {
                         v.Id,
                         v.VoucherNumber,
                         v.VoucherDate,
                         v.Description,
                         BranchCode = b == null ? string.Empty : b.Code,
                         VaultCode  = vault == null ? (string?)null : vault.Code,
                         LineCount  = v.Lines.Count(),
                     };

        var total = await AsyncExecuter.CountAsync(joined);
        var items = await AsyncExecuter.ToListAsync(
            joined.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<VoucherListDto>(
            total,
            items.Select(v => new VoucherListDto
            {
                Id            = v.Id,
                VoucherNumber = v.VoucherNumber,
                VoucherDate   = v.VoucherDate,
                Description   = v.Description,
                BranchCode    = v.BranchCode,
                VaultCode     = v.VaultCode,
                LineCount     = v.LineCount,
            }).ToList());
    }

    public async Task<List<VoucherLineDto>> GetLinesAsync(Guid voucherId)
    {
        var voucher = await _repository.GetAsync(voucherId);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

        // Görüntülenen satırlar — kronolojik (CreationTime, eşitte Id) sıra.
        var displayed = voucher.Lines
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.CreationTime).ThenBy(l => l.Id)
            .ToList();

        var dtos = displayed.Select(MapLine).ToList();
        foreach (var d in dtos) { d.VoucherDate = voucher.VoucherDate; d.VoucherNumber = voucher.VoucherNumber; }
        await ResolveUnitCodesAsync(dtos);
        await ResolveCreatorNamesAsync(dtos);

        // Yürüyen bakiye: devreden (ilk satırdan ÖNCEKİ tüm satırlar) + satır-satır birikim.
        if (displayed.Count > 0 && voucher.SubAccountId is { } subId)
        {
            var boundary = displayed[0].CreationTime;
            var carryLines = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(v => v.SubAccountId == subId)
                    .SelectMany(v => v.Lines)
                    .Where(l => !l.IsDeleted && l.CreationTime < boundary));

            await AssignRunningBalancesAsync(displayed, dtos, carryLines);
        }

        return dtos;
    }

    /// <summary>Liste modu: cari'nin [start, endExclusive) tarih aralığındaki tüm satırları (fiş-bağımsız),
    /// kronolojik (VoucherDate → CreationTime), yürüyen bakiyeyle (devreden = start'tan ÖNCESİ).</summary>
    public async Task<List<VoucherLineDto>> GetLinesByDateRangeAsync(Guid subAccountId, DateTime start, DateTime endExclusive)
    {
        var q = await _repository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.SubAccountId == subAccountId && v.VoucherDate >= start && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted
            select new { Line = l, v.VoucherDate, v.VoucherNumber });

        var ordered = rows
            .OrderBy(r => r.VoucherDate).ThenBy(r => r.Line.CreationTime).ThenBy(r => r.Line.Id)
            .ToList();
        var displayed = ordered.Select(r => r.Line).ToList();

        var dtos = displayed.Select(MapLine).ToList();
        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].VoucherDate   = ordered[i].VoucherDate;
            dtos[i].VoucherNumber = ordered[i].VoucherNumber;
        }

        await ResolveUnitCodesAsync(dtos);
        await ResolveCreatorNamesAsync(dtos);

        if (displayed.Count > 0)
        {
            var carryLines = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(v => v.SubAccountId == subAccountId && v.VoucherDate < start)
                    .SelectMany(v => v.Lines)
                    .Where(l => !l.IsDeleted));

            await AssignRunningBalancesAsync(displayed, dtos, carryLines);
        }

        return dtos;
    }

    public async Task<VoucherLineDto> GetLineForEditAsync(Guid lineId)
    {
        var line = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .SelectMany(v => v.Lines)
                .Where(l => l.Id == lineId && !l.IsDeleted))
            ?? throw new EntityNotFoundException(typeof(VoucherLine), lineId);

        var dto = MapLine(line);
        if (line.MainUnitId != Guid.Empty)
            dto.MainUnitCode = await ResolveUnitCodeAsync(line.MainUnitId);
        if (line.PayUnitId is { } pid)
            dto.PayUnitCode = await ResolveUnitCodeAsync(pid);
        return dto;
    }

    /// <summary>MainUnitCode / PayUnitCode'u (DB'de saklanmaz) PayUnitId/MainUnitId'den okuma anında çözer.</summary>
    private async Task ResolveUnitCodesAsync(List<VoucherLineDto> dtos)
    {
        var unitIds = dtos.Select(d => d.MainUnitId)
            .Concat(dtos.Where(d => d.PayUnitId.HasValue).Select(d => d.PayUnitId!.Value))
            .Where(id => id != Guid.Empty)
            .Distinct().ToList();
        if (unitIds.Count == 0) return;

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var codeMap = (await AsyncExecuter.ToListAsync(
                    (await _unitRepository.GetQueryableAsync())
                        .Where(u => unitIds.Contains(u.Id))
                        .Select(u => new { u.Id, u.Code })))
                .ToDictionary(x => x.Id, x => x.Code);

            foreach (var d in dtos)
            {
                if (codeMap.TryGetValue(d.MainUnitId, out var mc)) d.MainUnitCode = mc;
                if (d.PayUnitId is { } pid && codeMap.TryGetValue(pid, out var pc)) d.PayUnitCode = pc;
            }
        }
    }

    /// <summary>Devreden (<paramref name="carryLines"/>) + sıralı görüntülenen satırlardan her satıra
    /// kadarki yürüyen bakiyeyi (birim-bazlı) hesaplar ve <paramref name="dtos"/>'ya yazar.</summary>
    private async Task AssignRunningBalancesAsync(
        List<VoucherLine> displayed, List<VoucherLineDto> dtos, List<VoucherLine> carryLines)
    {
        if (displayed.Count == 0) return;

        var running = new Dictionary<Guid, decimal>(_balanceCalculator.Aggregate(carryLines));

        var ids = new HashSet<Guid>(running.Keys);
        foreach (var l in displayed)
            foreach (var e in _balanceCalculator.Post(l))
                ids.Add(e.UnitId);

        var orderedUnits = await OrderedVisibleUnitsAsync(ids);

        for (var i = 0; i < displayed.Count; i++)
        {
            foreach (var e in _balanceCalculator.Post(displayed[i]))
            {
                running.TryGetValue(e.UnitId, out var cur);
                running[e.UnitId] = cur + e.Amount;
            }

            dtos[i].RunningBalances = orderedUnits
                .Select(u => new VoucherBalanceLineDto
                {
                    UnitId   = u.Id,
                    UnitCode = u.Code,
                    Net      = running.GetValueOrDefault(u.Id),
                })
                .ToList();
        }
    }

    private async Task<string?> ResolveUnitCodeAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == unitId)
                    .Select(u => u.Code));
        }
    }

    public async Task DeleteLineAsync(Guid voucherId, Guid lineId, string reason)
    {
        var voucher = await _repository.GetAsync(voucherId);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);
        voucher.RemoveLine(lineId);
        await _repository.UpdateAsync(voucher, autoSave: true);
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        // VoucherLineLog gelene kadar nedeni log'a yaz (kalıcı geçmiş ertelendi).
        Logger.LogInformation("VoucherLine {LineId} silindi. Neden: {Reason}", lineId, reason);
    }

    public async Task<AccountBalanceDto> GetBalancesAsync(Guid subAccountId, DateTime? upTo = null)
    {
        var q = (await _repository.GetQueryableAsync()).Where(v => v.SubAccountId == subAccountId);
        if (upTo.HasValue)
            q = q.Where(v => v.VoucherDate <= upTo.Value);

        var lines = await AsyncExecuter.ToListAsync(
            q.SelectMany(v => v.Lines).Where(l => !l.IsDeleted));

        var net = _balanceCalculator.Aggregate(lines);   // UnitId → işaretli net

        var ordered = await OrderedVisibleUnitsAsync(net.Keys);
        var rows = ordered
            .Select(u => new VoucherBalanceLineDto { UnitId = u.Id, UnitCode = u.Code, Net = net.GetValueOrDefault(u.Id) })
            .ToList();

        // Hesabın bakiye para birimi (konsolide hedefi): SubAccount → Account → BalanceCurrencyUnit.
        var (baseUnitId, baseCode) = await ResolveBalanceUnitAsync(subAccountId);

        return new AccountBalanceDto
        {
            BalanceUnitId = baseUnitId,
            BalanceCode   = baseCode,
            Lines         = rows,
        };
    }

    private async Task<(Guid Id, string Code)> ResolveBalanceUnitAsync(Guid subAccountId)
    {
        var sub = await _subAccountRepository.FindAsync(subAccountId);
        if (sub is null) return (Guid.Empty, string.Empty);

        var account = await _accountRepository.FindAsync(sub.AccountId);
        if (account is null) return (Guid.Empty, string.Empty);

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var code = await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == account.BalanceCurrencyUnitId)
                    .Select(u => u.Code));
            return (account.BalanceCurrencyUnitId, code ?? string.Empty);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static VoucherLineDto MapLine(VoucherLine l) => new()
    {
        Id               = l.Id,
        VoucherId        = l.VoucherId,
        Type             = l.Type,
        Direction        = l.Direction,
        PaymentType      = l.PaymentType,
        CommodityId      = l.CommodityId,
        CommodityCode    = l.CommodityCode,
        Quantity         = l.Quantity,
        Amount           = l.Amount,
        Factor           = l.Factor,
        Total            = l.Total,
        MainUnitId       = l.MainUnitId,
        PayCommodityId   = l.PayCommodityId,
        PayCommodityCode = l.PayCommodityCode,
        PayUnitId        = l.PayUnitId,
        PayFactor        = l.PayFactor,
        MarketPrice      = l.MarketPrice,
        PayTotal         = l.PayTotal,
        PayUnitRate      = l.PayUnitRate,
        Profit           = l.Profit,
        DueDate          = l.DueDate,
        Description      = l.Description,
        CreationTime     = l.CreationTime,
        CreatorId        = l.CreatorId,
    };

    private async Task ResolveCreatorNamesAsync(List<VoucherLineDto> dtos)
    {
        var creatorIds = dtos.Where(x => x.CreatorId.HasValue).Select(x => x.CreatorId!.Value).Distinct().ToList();
        if (!creatorIds.Any()) return;

        var userRepo = LazyServiceProvider.LazyGetService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>();
        if (userRepo == null) return;

        var users = await AsyncExecuter.ToListAsync(
            (await userRepo.GetQueryableAsync()).Where(u => creatorIds.Contains(u.Id))
        );
        var userDict = users.ToDictionary(u => u.Id, u => u.UserName);

        foreach (var dto in dtos)
        {
            if (dto.CreatorId.HasValue && userDict.TryGetValue(dto.CreatorId.Value, out var name))
            {
                dto.CreatorName = name;
            }
        }
    }

    /// <summary>Görünür birimleri (host‖own) gösterim sırasıyla döndürür: her zaman gösterilecekler
    /// (AlwaysShowInBalance) + <paramref name="includeIds"/> (hareketi olanlar).</summary>
    private async Task<List<(Guid Id, string Code)>> OrderedVisibleUnitsAsync(IEnumerable<Guid> includeIds)
    {
        var ids = includeIds.ToList();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var units = await _unitRepository.GetQueryableAsync();
            var ordered = await AsyncExecuter.ToListAsync(
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

    private async Task<long> NextNumberAsync(Guid companyId)
    {
        var query = await _repository.GetQueryableAsync();
        var maxNumber = await AsyncExecuter.MaxAsync(
            query.Where(v => v.CompanyId == companyId).Select(v => (long?)v.VoucherNumber)) ?? 0L;
        return maxNumber + 1;
    }

    private static VoucherLineInput ToLineInput(VoucherLineDto i) => new(
        Type:             i.Type,
        Direction:        i.Direction,
        PaymentType:      i.PaymentType,
        CommodityId:      i.CommodityId,
        CommodityCode:    i.CommodityCode,
        Quantity:         i.Quantity,
        Amount:           i.Amount,
        Factor:           i.Factor,
        Total:            i.Total,
        MainUnitId:       i.MainUnitId,
        PayFactor:        i.PayFactor,
        MarketPrice:      i.MarketPrice,
        PayTotal:         i.PayTotal,
        Profit:           i.Profit,
        PayCommodityId:   i.PayCommodityId,
        PayCommodityCode: i.PayCommodityCode,
        PayUnitId:        i.PayUnitId,
        PayUnitRate:      i.PayUnitRate,
        DueDate:          i.DueDate,
        Description:      i.Description,
        BullionType:            i.BullionType,
        AssayOfficeId:          i.AssayOfficeId,
        ReportNo:               i.ReportNo,
        IsReport:               i.IsReport,
        IsExtra:                i.IsExtra,
        AssayAmount:            i.AssayAmount,
        SilverFactor:           i.SilverFactor,
        PlatinumFactor:         i.PlatinumFactor,
        PalladiumFactor:        i.PalladiumFactor,
        SilverMode:             i.SilverMode,
        PlatinumMode:           i.PlatinumMode,
        PalladiumMode:          i.PalladiumMode,
        LaborMode:              i.LaborMode,
        SilverLaborRate:        i.SilverLaborRate,
        PlatinumLaborRate:      i.PlatinumLaborRate,
        PalladiumLaborRate:     i.PalladiumLaborRate,
        GoldLaborUnitId:        i.GoldLaborUnitId,
        SilverLaborUnitId:      i.SilverLaborUnitId,
        PlatinumLaborUnitId:    i.PlatinumLaborUnitId,
        PalladiumLaborUnitId:   i.PalladiumLaborUnitId,
        SilverUnitId:           i.SilverUnitId,
        PlatinumUnitId:         i.PlatinumUnitId,
        PalladiumUnitId:        i.PalladiumUnitId,
        GoldRate:               i.GoldRate,
        SilverRate:             i.SilverRate,
        PlatinumRate:           i.PlatinumRate,
        PalladiumRate:          i.PalladiumRate,
        GoldLaborUnitRate:      i.GoldLaborUnitRate,
        SilverLaborUnitRate:    i.SilverLaborUnitRate,
        PlatinumLaborUnitRate:  i.PlatinumLaborUnitRate,
        PalladiumLaborUnitRate: i.PalladiumLaborUnitRate);

    public async Task DeleteAsync(Guid id)
    {
        await _ledgerSynchronizer.DeleteVoucherAsync(id);
        await _repository.DeleteAsync(id, autoSave: true);
    }
}
