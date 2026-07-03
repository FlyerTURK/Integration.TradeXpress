using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
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
    private readonly IRepository<AssayOffice, Guid> _assayOfficeRepository;
    private readonly VoucherBalanceCalculator _balanceCalculator;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;

    public VoucherAppService(
        IRepository<Voucher, Guid> repository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<AssayOffice, Guid> assayOfficeRepository,
        VoucherBalanceCalculator balanceCalculator,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany)
    {
        _repository            = repository;
        _branchRepository      = branchRepository;
        _vaultRepository       = vaultRepository;
        _unitRepository        = unitRepository;
        _subAccountRepository  = subAccountRepository;
        _accountRepository     = accountRepository;
        _assayOfficeRepository = assayOfficeRepository;
        _balanceCalculator     = balanceCalculator;
        _ledgerSynchronizer    = ledgerSynchronizer;
        _dataFilter            = dataFilter;
        _currentCompany        = currentCompany;
    }

    /// <summary>Sızıntı önleme (BalanceSheet ile aynı desen): CompanyId DAİMA working-context'ten
    /// (<see cref="ICurrentCompany"/>) zorlanır — client'tan gelen CompanyId'ye ASLA güvenilmez.
    /// Sahte CompanyId ile başka şirkete fiş/ledger yazılmasını (ve bilançosuna sızmasını) engeller.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Voucher:CompanyContextRequired");
        }

        return companyId;
    }

    /// <summary>Şube working şirkete, kasa (varsa) o şubeye ait olmalı — aitlik doğrulaması
    /// (client'ın başka şirketin şube/kasasını göndermesini engeller).</summary>
    private async Task EnsureOrgScopeAsync(Guid companyId, Guid branchId, Guid? vaultId)
    {
        if (!await _branchRepository.AnyAsync(b => b.Id == branchId && b.CompanyId == companyId))
        {
            throw new BusinessException("TradeXpress:Voucher:BranchNotInCompany");
        }

        if (vaultId is { } vid && !await _vaultRepository.AnyAsync(v => v.Id == vid && v.BranchId == branchId))
        {
            throw new BusinessException("TradeXpress:Voucher:VaultNotInBranch");
        }
    }

    /// <summary>Fişi yükler + working şirkete aitliğini doğrular (yabancı şirket fişi = yokmuş gibi davran).</summary>
    private async Task<Voucher> GetOwnedVoucherAsync(Guid voucherId)
    {
        var voucher = await _repository.GetAsync(voucherId);
        if (voucher.CompanyId != EnsureCurrentCompanyId())
        {
            throw new EntityNotFoundException(typeof(Voucher), voucherId);
        }

        return voucher;
    }

    // NOT (eşzamanlılık doğrulaması): PARALEL istek koruması ABP'de ZATEN var — repo UpdateAsync root'u
    // Modified işaretler, ABP stamp'i döndürür (expected-original = property'deki değer) → ikinci paralel
    // istek AbpDbConcurrencyException alır; ledger drift'i bu yolda imkânsız. Stamp'i ELLE set etmek bu
    // mekanizmayı BOZAR (set edilen değer expected-original sanılır → 0 satır → hata) — yapma.
    // Kalan tek gerçek boşluk BAYAT İSTEMCİ idi (form eski veriyle açık) → aşağıdaki kontrol.

    /// <summary>İstemcinin okuduğu andaki fiş stamp'i mevcutla eşleşmiyorsa (arada başka kullanıcı değiştirdi)
    /// kaydı reddeder — sessiz last-writer-wins yerine açık, lokalize uyarı.</summary>
    private static void EnsureVoucherNotStale(Voucher voucher, string? clientStamp)
    {
        if (clientStamp != null && clientStamp != voucher.ConcurrencyStamp)
        {
            throw new BusinessException("TradeXpress:Voucher:ConcurrencyConflict");
        }
    }

    /// <summary>VoucherNumber unique index (TenantId,CompanyId,VoucherNumber) ihlali mi? MAX+1 yarışında
    /// (iki kullanıcı aynı anda ilk satır) ikinci insert bu ihlale düşer — sert DbUpdateException yerine
    /// lokalize "tekrar deneyin" mesajına çevrilir (panel verisi ekranda kalır, kullanıcı yeniden kaydeder).</summary>
    private static bool IsVoucherNumberConflict(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.Message.Contains("VoucherNumber", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<VoucherGetDto> CreateAsync(VoucherCreateDto input)
    {
        // Client CompanyId'sine güvenilmez — ambient working-context zorlanır (sızıntı önleme).
        var companyId = EnsureCurrentCompanyId();
        await EnsureOrgScopeAsync(companyId, input.BranchId, input.VaultId);

        var maxNumber = await NextNumberAsync(companyId);

        var entity = new Voucher(
            companyId,
            input.BranchId,
            input.VaultId,
            input.AccountId,
            input.SubAccountId,
            maxNumber,
            input.VoucherDate,
            input.Description);

        try
        {
            await _repository.InsertAsync(entity, autoSave: true);
        }
        catch (Exception ex) when (IsVoucherNumberConflict(ex))
        {
            // MAX+1 yarışı: unique index bütünlüğü koruyor; sert hata yerine lokalize "tekrar deneyin".
            throw new BusinessException("TradeXpress:Voucher:NumberConflict");
        }

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
        // Server-side per-tip yetki kontrolü (UI gate TEK BAŞINA yetmez — bkz. ProcessTypePermissionMap).
        await EnsureTransactionPermissionAsync(input.Type);

        // Tip-bazlı Miktar kuralı (legacy Islem.Save paritesi): Çeşni'de Miktar ZORUNLU (>0);
        // Dekont/Virman gibi parasal tipler muaf (Miktar=0 serbest — ek doğrulama yok).
        if (input.Type == ProcessType.Assay && input.Amount <= 0m)
        {
            throw new BusinessException("TradeXpress:Voucher:AmountRequired");
        }

        // Takoz ÇIKIŞ: metal verisi (miktar/milyem/rapor/ayar evi/birimler) SERVER-AUTHORITATIVE —
        // seçilen giriş külçesinden kopyalanır; panel yalnız işçilik + dağıtım durumlarını gönderir.
        if (input.Type == ProcessType.Bullion && !IsInflow(input.Direction))
            await PrepareBullionExitLineAsync(input);

        // WYSIWYG: ekranda görünen değerler AYNEN kaydedilir (sunucu recompute yok).
        var lineInput = ToLineInput(input);

        Voucher voucher;
        Guid lineId;

        if (input.VoucherId is { } voucherId)
        {
            // Aitlik: yabancı şirketin fişine satır eklenemez/güncellenemez.
            voucher = await GetOwnedVoucherAsync(voucherId);
            await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

            // Bayat istemci kontrolü: okuma anındaki stamp değişmişse (başkası düzenledi) reddet.
            EnsureVoucherNotStale(voucher, input.VoucherConcurrencyStamp);

            if (input.Id != Guid.Empty)
            {
                voucher.UpdateLine(input.Id, lineInput);
                lineId = input.Id;
            }
            else
            {
                lineId = voucher.AddLine(GuidGenerator.Create(), lineInput).Id;
            }

            await _repository.UpdateAsync(voucher, autoSave: true);   // ABP stamp döngüsü paralel isteği zaten yakalar
        }
        else
        {
            // Fiş lazy oluşturulur + numara atanır. CompanyId ambient'ten zorlanır (client'a güvenilmez),
            // şube/kasa aitliği doğrulanır (sızıntı önleme — BalanceSheet ile aynı ilke).
            var companyId = EnsureCurrentCompanyId();
            await EnsureOrgScopeAsync(companyId, input.BranchId, input.VaultId);

            voucher = new Voucher(
                companyId,
                input.BranchId,
                input.VaultId,
                input.AccountId,
                input.SubAccountId,
                await NextNumberAsync(companyId),
                input.VoucherDate,
                input.VoucherDescription);

            lineId = voucher.AddLine(GuidGenerator.Create(), lineInput).Id;

            try
            {
                await _repository.InsertAsync(voucher, autoSave: true);
            }
            catch (Exception ex) when (IsVoucherNumberConflict(ex))
            {
                // MAX+1 yarışı (eşzamanlı ilk satır): unique index veri bütünlüğünü zaten koruyor;
                // sert DbUpdateException yerine lokalize mesaj — panel verisi ekranda, kullanıcı yeniden kaydeder.
                throw new BusinessException("TradeXpress:Voucher:NumberConflict");
            }
        }

        // Ledger senkronu (poster çıktısı → kalıcı): voucher kaydedildikten sonra, aynı UoW içinde.
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        input.Id            = lineId;
        input.VoucherId     = voucher.Id;
        input.VoucherNumber = voucher.VoucherNumber;
        input.VoucherConcurrencyStamp = voucher.ConcurrencyStamp;   // ardışık düzenleme taze stamp'le sürsün
        return input;
    }

    public async Task<PagedResultDto<VoucherListDto>> GetListAsync(VoucherListRequestDto input)
    {
        // Company scope: yalnız working şirketin fişleri (sızıntı önleme).
        var companyId = EnsureCurrentCompanyId();
        var voucherQ = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.SubAccountId == input.SubAccountId)
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
        var voucher = await GetOwnedVoucherAsync(voucherId);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

        // Görüntülenen satırlar — kronolojik (CreationTime, eşitte Id) sıra.
        var displayed = voucher.Lines
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.CreationTime).ThenBy(l => l.Id)
            .ToList();

        var dtos = displayed.Select(MapLine).ToList();
        foreach (var d in dtos)
        {
            d.VoucherDate = voucher.VoucherDate;
            d.VoucherNumber = voucher.VoucherNumber;
            d.VoucherConcurrencyStamp = voucher.ConcurrencyStamp;   // düzenlemede bayat-istemci tespiti için
        }
        await ResolveUnitCodesAsync(dtos);
        await ResolveCreatorNamesAsync(dtos);

        // Yürüyen bakiye: devreden (ilk satırdan ÖNCEKİ tüm satırlar) + satır-satır birikim.
        if (displayed.Count > 0 && voucher.SubAccountId is { } subId)
        {
            var boundary = displayed[0].CreationTime;
            var carryLines = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(v => v.CompanyId == voucher.CompanyId && v.SubAccountId == subId)
                    .SelectMany(v => v.Lines)
                    .Where(l => !l.IsDeleted && l.CreationTime < boundary));

            await AssignRunningBalancesAsync(displayed, dtos, carryLines);
        }

        return dtos;
    }

    /// <summary>Liste modu: cari'nin [start, endExclusive) tarih aralığındaki tüm satırları (fiş-bağımsız),
    /// kronolojik (VoucherDate → CreationTime), yürüyen bakiyeyle (devreden = start'tan ÖNCESİ).
    /// Sözleşme korunur — ekstre metoduna delege eder (devreden/kapanış yutulur).</summary>
    public async Task<List<VoucherLineDto>> GetLinesByDateRangeAsync(Guid subAccountId, DateTime start, DateTime endExclusive)
    {
        var statement = await GetAccountStatementAsync(subAccountId, start, endExclusive);
        return statement.Lines;
    }

    /// <summary>Hesap ekstresi (legacy <c>Cari.HesapExtresiEx</c> paritesi): dönem satırları + devreden + kapanış.
    /// <paramref name="types"/> doluysa hem dönem satırları hem devreden AYNI tip filtresiyle hesaplanır —
    /// filtreli ekstrenin yürüyen bakiyesi kendi içinde tutarlı kalır (filtresiz çağrıda dip-toplam = Bakiye sekmesi).</summary>
    public async Task<AccountStatementDto> GetAccountStatementAsync(
        Guid subAccountId, DateTime start, DateTime endExclusive, List<ProcessType>? types = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        var typeFilter = types is { Count: > 0 } ? types : null;   // boş liste = filtre yok

        var q = await _repository.GetQueryableAsync();
        var rangeQuery =
            from v in q
            where v.CompanyId == companyId && v.SubAccountId == subAccountId && v.VoucherDate >= start && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted
            select new { Line = l, v.VoucherDate, v.VoucherNumber };
        if (typeFilter != null)
        {
            rangeQuery = rangeQuery.Where(r => typeFilter.Contains(r.Line.Type));
        }
        var rows = await AsyncExecuter.ToListAsync(rangeQuery);

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

        // Devreden: start'tan önceki satırların (aynı tip filtresiyle) birim-bazlı neti.
        // Dönemde satır olmasa da hesaplanır — boş dönemde bile devreden görünür kalmalı.
        var carryQuery = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.SubAccountId == subAccountId && v.VoucherDate < start)
            .SelectMany(v => v.Lines)
            .Where(l => !l.IsDeleted);
        if (typeFilter != null)
        {
            carryQuery = carryQuery.Where(l => typeFilter.Contains(l.Type));
        }
        var carryLines = await AsyncExecuter.ToListAsync(carryQuery);

        if (displayed.Count > 0)
        {
            await AssignRunningBalancesAsync(displayed, dtos, carryLines);
        }

        var opening = await ToBalanceRowsAsync(_balanceCalculator.Aggregate(carryLines));
        var closing = dtos.Count > 0 ? dtos[^1].RunningBalances : opening;

        return new AccountStatementDto
        {
            OpeningBalances = opening,
            Lines           = dtos,
            ClosingBalances = closing,
        };
    }

    public async Task<VoucherLineDto> GetLineForEditAsync(Guid lineId)
    {
        var line = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .SelectMany(v => v.Lines)
                .Where(l => l.Id == lineId && !l.IsDeleted))
            ?? throw new EntityNotFoundException(typeof(VoucherLine), lineId);

        var dto = MapLine(line);
        // Fişin stamp'ini de taşı — kaydetmede bayat-istemci tespiti (ConcurrencyConflict) için.
        dto.VoucherConcurrencyStamp = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.Id == line.VoucherId)
                .Select(v => v.ConcurrencyStamp));
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

    /// <summary>Birim → net sözlüğünü görünür-birim sırasıyla bakiye satırlarına çevirir (ekstre devreden/kapanış + Bakiye sekmesi ortak yolu).</summary>
    private async Task<List<VoucherBalanceLineDto>> ToBalanceRowsAsync(IReadOnlyDictionary<Guid, decimal> net)
    {
        var ordered = await OrderedVisibleUnitsAsync(net.Keys);
        return ordered
            .Select(u => new VoucherBalanceLineDto { UnitId = u.Id, UnitCode = u.Code, Net = net.GetValueOrDefault(u.Id) })
            .ToList();
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
        var voucher = await GetOwnedVoucherAsync(voucherId);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

        // Silme de Save ile AYNI per-tip yetkiye tabidir — Metal işlemi yapamayan, Metal satırı da silemez
        // (UI gate'i bypass eden doğrudan API çağrılarına karşı; entegrasyon analizi E-2).
        var line = voucher.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new EntityNotFoundException(typeof(VoucherLine), lineId);
        await EnsureTransactionPermissionAsync(line.Type);

        voucher.RemoveLine(lineId);
        await _repository.UpdateAsync(voucher, autoSave: true);   // ABP stamp döngüsü paralel isteği zaten yakalar
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        // VoucherLineLog gelene kadar nedeni log'a yaz (kalıcı geçmiş ertelendi).
        Logger.LogInformation("VoucherLine {LineId} silindi. Neden: {Reason}", lineId, reason);
    }

    public async Task<AccountBalanceDto> GetBalancesAsync(Guid subAccountId, DateTime? upTo = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        var q = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.SubAccountId == subAccountId);
        if (upTo.HasValue)
            q = q.Where(v => v.VoucherDate <= upTo.Value);

        var lines = await AsyncExecuter.ToListAsync(
            q.SelectMany(v => v.Lines).Where(l => !l.IsDeleted));

        var net  = _balanceCalculator.Aggregate(lines);   // UnitId → işaretli net
        var rows = await ToBalanceRowsAsync(net);

        // Hesabın bakiye para birimi (konsolide hedefi): SubAccount → Account → BalanceCurrencyUnit.
        var (baseUnitId, baseCode) = await ResolveBalanceUnitAsync(subAccountId);

        return new AccountBalanceDto
        {
            BalanceUnitId = baseUnitId,
            BalanceCode   = baseCode,
            Lines         = rows,
        };
    }

    public async Task<List<BullionStockItemDto>> GetBullionStockAsync(bool? inStock = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        var q = await _repository.GetQueryableAsync();

        // Külçeler = aktif GİRİŞ satırları (fiş başlığındaki SubAccountId ile — VoucherLine'da yok).
        var entries = await AsyncExecuter.ToListAsync(
            from v in q
            from l in v.Lines
            where v.CompanyId == companyId
               && l.Type == ProcessType.Bullion
               && l.Direction == ProcessDirectionType.Inbound
               && !l.IsDeleted
            select new BullionStockItemDto
            {
                EntryLineId     = l.Id,
                Code            = l.CommodityCode,
                BullionType     = l.BullionType,
                IsReport        = l.IsReport ?? false,
                IsExtra         = l.IsExtra ?? false,
                Amount          = l.Amount,
                AssayAmount     = l.AssayAmount ?? 0m,
                GoldFactor      = l.Factor,
                SilverFactor    = l.SilverFactor ?? 0m,
                PlatinumFactor  = l.PlatinumFactor ?? 0m,
                PalladiumFactor = l.PalladiumFactor ?? 0m,
                ReportNo        = l.ReportNo,
                AssayOfficeId   = l.AssayOfficeId,
                EntryDate       = l.CreationTime,
                SubAccountId    = v.SubAccountId,
            });

        if (entries.Count == 0)
            return entries;

        // Aktif çıkışlar → külçe başına son çıkış zamanı (stok = çıkışı olmayan giriş).
        var exits = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.CompanyId == companyId)
                .SelectMany(v => v.Lines)
                .Where(l => l.Type == ProcessType.Bullion
                         && l.Direction == ProcessDirectionType.Outbound
                         && !l.IsDeleted
                         && l.CommodityId != null)
                .Select(l => new { l.CommodityId, l.CreationTime }));
        var exitByEntry = exits
            .GroupBy(x => x.CommodityId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreationTime));

        foreach (var e in entries)
        {
            e.InStock  = !exitByEntry.ContainsKey(e.EntryLineId);
            e.ExitDate = exitByEntry.TryGetValue(e.EntryLineId, out var d) ? d : null;
        }

        if (inStock is { } stockFilter)
            entries = entries.Where(e => e.InStock == stockFilter).ToList();

        await ResolveBullionStockDisplayAsync(entries);

        return entries.OrderByDescending(e => e.EntryDate).ToList();
    }

    /// <summary>Çeşni stoğu özeti — SQL-side toplama (satır çekmeden): takoz GİRİŞ satırlarının AssayAmount
    /// havuzu (raporsuzda da cari alacağına dahil — BullionLegCalculator giriş kuralı) MİNUS çeşni ÇIKIŞ
    /// satırlarının Amount toplamı. Milyemler ağırlıklı ortalama (Has/Miktar — legacy Cesni paritesi).
    /// Not: külçenin takoz-çıkışı numuneyi düşürmez (numune dükkânda kalır — legacy kural).</summary>
    public async Task<AssayStockDto> GetAssayStockAsync()
    {
        var companyId = EnsureCurrentCompanyId();   // company scope ZORUNLU (sızıntı önleme)
        var q = await _repository.GetQueryableAsync();

        // Giriş havuzu: takoz GİRİŞ satırlarının numunesi (miktar + metal içerikleri).
        var entry = await AsyncExecuter.FirstOrDefaultAsync(
            (from v in q
             where v.CompanyId == companyId
             from l in v.Lines
             where l.Type == ProcessType.Bullion
                && l.Direction == ProcessDirectionType.Inbound
                && !l.IsDeleted
             select l)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Amount = g.Sum(l => l.AssayAmount ?? 0m),
                Has    = g.Sum(l => (l.AssayAmount ?? 0m) * l.Factor),
                Gum    = g.Sum(l => (l.AssayAmount ?? 0m) * (l.SilverFactor ?? 0m)),
            }));

        // Çıkışlar: çeşni satırları (yön daima ÇIKIŞ) havuzdan düşer.
        var exit = await AsyncExecuter.FirstOrDefaultAsync(
            (from v in q
             where v.CompanyId == companyId
             from l in v.Lines
             where l.Type == ProcessType.Assay
                && l.Direction == ProcessDirectionType.Outbound
                && !l.IsDeleted
             select l)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Amount = g.Sum(l => l.Amount),
                Has    = g.Sum(l => l.Amount * l.Factor),
                Gum    = g.Sum(l => l.Amount * (l.SilverFactor ?? 0m)),
            }));

        var amount = (entry?.Amount ?? 0m) - (exit?.Amount ?? 0m);
        var has    = (entry?.Has ?? 0m) - (exit?.Has ?? 0m);
        var gum    = (entry?.Gum ?? 0m) - (exit?.Gum ?? 0m);

        return new AssayStockDto
        {
            Amount   = amount,
            Has      = has,
            Gum      = gum,
            AuMilyem = amount == 0m ? 0m : has / amount,
            AgMilyem = amount == 0m ? 0m : gum / amount,
        };
    }

    /// <summary>Takoz stoğu satırlarının denormalize gösterim alanlarını (ayar evi adı + getiren cari) doldurur.</summary>
    private async Task ResolveBullionStockDisplayAsync(List<BullionStockItemDto> entries)
    {
        var assayIds = entries.Where(e => e.AssayOfficeId.HasValue)
                              .Select(e => e.AssayOfficeId!.Value).Distinct().ToList();
        if (assayIds.Count > 0)
        {
            var names = (await AsyncExecuter.ToListAsync(
                    (await _assayOfficeRepository.GetQueryableAsync())
                        .Where(a => assayIds.Contains(a.Id))
                        .Select(a => new { a.Id, a.Name })))
                .ToDictionary(x => x.Id, x => x.Name);
            foreach (var e in entries)
                if (e.AssayOfficeId is { } aid && names.TryGetValue(aid, out var n))
                    e.AssayOfficeName = n;
        }

        var subIds = entries.Where(e => e.SubAccountId.HasValue)
                            .Select(e => e.SubAccountId!.Value).Distinct().ToList();
        if (subIds.Count > 0)
        {
            var subs = (await AsyncExecuter.ToListAsync(
                    (await _subAccountRepository.GetQueryableAsync())
                        .Where(s => subIds.Contains(s.Id))
                        .Select(s => new { s.Id, s.Code, s.Name })))
                .ToDictionary(x => x.Id, x => $"{x.Code} — {x.Name}");
            foreach (var e in entries)
                if (e.SubAccountId is { } sid && subs.TryGetValue(sid, out var disp))
                    e.SubAccountDisplay = disp;
        }
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

    /// <summary>Satırın <see cref="ProcessType"/>'ına göre gerekli yetkiyi kontrol eder — UI gate'i (buton
    /// gizleme) bypass eden doğrudan API çağrılarına karşı SON savunma hattı (ProcessTypePermissionMap tek kaynak).</summary>
    private async Task EnsureTransactionPermissionAsync(ProcessType type)
    {
        var permission = ProcessTypePermissionMap.PermissionFor(type);
        await AuthorizationService.CheckAsync(permission);
    }

    /// <summary>Yön "giriş" mi (bakiyeye + yönde): Inbound/Credit/Buy → çift enum değeri.</summary>
    private static bool IsInflow(ProcessDirectionType direction) => ((int)direction % 2) == 0;

    /// <summary>Takoz ÇIKIŞ satırının metal verisini (miktar/milyem/rapor/ayar evi/yan-birimler) seçilen GİRİŞ
    /// külçesinden KOPYALAR — client bu alanlara güvenilmez (yalnız işçilik + dağıtım durumlarını gönderir).
    /// Kısmi çıkış YOK: külçe bütünüyle çıkar (Amount girişten aynen). CommodityId = giriş satırı Id'si.</summary>
    private async Task PrepareBullionExitLineAsync(VoucherLineDto input)
    {
        if (input.CommodityId is not { } entryLineId || entryLineId == Guid.Empty)
            throw new BusinessException("TradeXpress:Bullion:ExitEntryRequired");

        var entry = await FindBullionEntryLineAsync(entryLineId)
            ?? throw new BusinessException("TradeXpress:Bullion:ExitEntryNotFound");

        // Külçe kimliği + metal ölçüleri (giriş otoritedir).
        input.CommodityCode = entry.CommodityCode;
        input.BullionType   = entry.BullionType;
        input.AssayOfficeId = entry.AssayOfficeId;
        input.ReportNo      = entry.ReportNo;
        input.IsReport      = entry.IsReport;
        input.IsExtra       = entry.IsExtra;
        input.Amount        = entry.Amount;
        input.AssayAmount   = entry.AssayAmount;
        input.Factor          = entry.Factor;          // altın milyemi
        input.SilverFactor    = entry.SilverFactor;
        input.PlatinumFactor  = entry.PlatinumFactor;
        input.PalladiumFactor = entry.PalladiumFactor;

        // Ana + yan metal bacak birimleri (poster bunlara postlar) girişten kopyalanır.
        input.MainUnitId     = entry.MainUnitId;
        input.SilverUnitId   = entry.SilverUnitId;
        input.PlatinumUnitId = entry.PlatinumUnitId;
        input.PalladiumUnitId = entry.PalladiumUnitId;
    }

    /// <summary>Bir takoz GİRİŞ satırını (külçeyi) Id ile bulur (silinmemiş, Bullion+Inbound).</summary>
    private async Task<VoucherLine?> FindBullionEntryLineAsync(Guid entryLineId)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .SelectMany(v => v.Lines)
                .Where(l => l.Id == entryLineId
                         && l.Type == ProcessType.Bullion
                         && l.Direction == ProcessDirectionType.Inbound
                         && !l.IsDeleted));
    }

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

        // ── TAKOZ (Bullion) alanları — DÜZELT akışı bunlarsız paneli default'larla açıyordu
        //    (raporsuz/Gold/milyemler 0): kaydetme yönü (ToLineInput) tamdı, okuma yönü eksikti. ──
        BullionType            = l.BullionType,
        AssayOfficeId          = l.AssayOfficeId,
        ReportNo               = l.ReportNo,
        IsReport               = l.IsReport,
        IsExtra                = l.IsExtra,
        AssayAmount            = l.AssayAmount,
        SilverFactor           = l.SilverFactor,
        PlatinumFactor         = l.PlatinumFactor,
        PalladiumFactor        = l.PalladiumFactor,
        SilverMode             = l.SilverMode,
        PlatinumMode           = l.PlatinumMode,
        PalladiumMode          = l.PalladiumMode,
        LaborMode              = l.LaborMode,
        SilverLaborRate        = l.SilverLaborRate,
        PlatinumLaborRate      = l.PlatinumLaborRate,
        PalladiumLaborRate     = l.PalladiumLaborRate,
        GoldLaborUnitId        = l.GoldLaborUnitId,
        SilverLaborUnitId      = l.SilverLaborUnitId,
        PlatinumLaborUnitId    = l.PlatinumLaborUnitId,
        PalladiumLaborUnitId   = l.PalladiumLaborUnitId,
        SilverUnitId           = l.SilverUnitId,
        PlatinumUnitId         = l.PlatinumUnitId,
        PalladiumUnitId        = l.PalladiumUnitId,
        GoldRate               = l.GoldRate,
        SilverRate             = l.SilverRate,
        PlatinumRate           = l.PlatinumRate,
        PalladiumRate          = l.PalladiumRate,
        GoldLaborUnitRate      = l.GoldLaborUnitRate,
        SilverLaborUnitRate    = l.SilverLaborUnitRate,
        PlatinumLaborUnitRate  = l.PlatinumLaborUnitRate,
        PalladiumLaborUnitRate = l.PalladiumLaborUnitRate,

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
        // Aitlik + per-tip yetki: fişteki HER farklı işlem tipi için ayrı yetki gerekir
        // (tek tipte bile yetkisizse fişin tamamı silinemez; entegrasyon analizi E-2).
        var voucher = await GetOwnedVoucherAsync(id);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);
        foreach (var type in voucher.Lines.Where(l => !l.IsDeleted).Select(l => l.Type).Distinct())
        {
            await EnsureTransactionPermissionAsync(type);
        }

        await _ledgerSynchronizer.DeleteVoucherAsync(id);
        await _repository.DeleteAsync(id, autoSave: true);
    }
}
