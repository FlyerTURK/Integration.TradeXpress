using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Integration.Framework.Base.Querying;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş + satır servisi — ORKESTRATÖR. Dış sözleşme (<see cref="IVoucherAppService"/>) burada; iş
/// parçaları ayrı servislerde: guard'lar (<c>VoucherAppService.Guards.cs</c> partial), numara tahsisi
/// (<see cref="VoucherNumberAllocator"/>), ekstre/bakiye (<see cref="VoucherStatementService"/>),
/// takoz/çeşni stok (<see cref="VoucherBullionStockService"/>), virman çift-bacak
/// (<see cref="VoucherTransferService"/>), DTO eşleme (<see cref="VoucherLineDtoFactory"/>),
/// denormalize kod çözümü (<see cref="VoucherCodeResolver"/>).
/// Cash satırları WYSIWYG kaydedilir (istemci aynı motoru çalıştırır; sunucu recompute YOK).
/// Liste/edit formu YOK — Cari İşlemler formu doğrudan bu servisi kullanır.
/// </summary>
[Authorize]
public partial class VoucherAppService : TradeXpressAppService, IVoucherAppService
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly ICurrentCompany _currentCompany;
    private readonly IScopedGrantResolver _scopedGrantResolver;
    private readonly VoucherNumberAllocator _numberAllocator;
    private readonly VoucherCodeResolver _codeResolver;
    private readonly VoucherCounterpartyResolver _counterpartyResolver;
    private readonly VoucherStatementService _statementService;
    private readonly VoucherBullionStockService _bullionStockService;
    private readonly VoucherTransferService _transferService;
    private readonly VoucherLineHistoryRecorder _historyRecorder;

    public VoucherAppService(
        IRepository<Voucher, Guid> repository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        ICurrentCompany currentCompany,
        IScopedGrantResolver scopedGrantResolver,
        VoucherNumberAllocator numberAllocator,
        VoucherCodeResolver codeResolver,
        VoucherCounterpartyResolver counterpartyResolver,
        VoucherStatementService statementService,
        VoucherBullionStockService bullionStockService,
        VoucherTransferService transferService,
        VoucherLineHistoryRecorder historyRecorder)
    {
        _repository          = repository;
        _branchRepository    = branchRepository;
        _vaultRepository     = vaultRepository;
        _ledgerSynchronizer  = ledgerSynchronizer;
        _currentCompany      = currentCompany;
        _scopedGrantResolver = scopedGrantResolver;
        _numberAllocator     = numberAllocator;
        _codeResolver         = codeResolver;
        _counterpartyResolver = counterpartyResolver;
        _statementService    = statementService;
        _bullionStockService = bullionStockService;
        _transferService     = transferService;
        _historyRecorder     = historyRecorder;
    }

    public async Task<VoucherGetDto> CreateAsync(VoucherCreateDto input)
    {
        // Client CompanyId'sine güvenilmez — ambient working-context zorlanır (sızıntı önleme).
        var companyId = EnsureCurrentCompanyId();
        await EnsureOrgScopeAsync(companyId, input.BranchId, input.VaultId);

        // Karşı taraf kodları SUNUCU-OTORİTER çözülür (istemciden gelen koda güvenilmez).
        var counterparty = input.AccountType == AccountType.Vault
            ? await _counterpartyResolver.ResolveVaultAsync(companyId, input.SubAccountId ?? Guid.Empty)
            : await _counterpartyResolver.ResolveCurrentAccountAsync(companyId, input.AccountId, input.SubAccountId);

        var entity = new Voucher(
            companyId,
            input.BranchId,
            input.VaultId,
            counterparty.AccountType,
            counterparty.AccountId,
            counterparty.AccountCode,
            counterparty.SubAccountId,
            counterparty.SubAccountCode,
            await _numberAllocator.NextNumberAsync(companyId),
            input.VoucherDate,
            input.Description);

        await _numberAllocator.InsertNumberedAsync(entity);

        return new VoucherGetDto
        {
            Id             = entity.Id,
            CompanyId      = entity.CompanyId,
            BranchId       = entity.BranchId,
            VaultId        = entity.VaultId,
            AccountType    = entity.AccountType,
            AccountId      = entity.AccountId,
            AccountCode    = entity.AccountCode,
            SubAccountId   = entity.SubAccountId,
            SubAccountCode = entity.SubAccountCode,
            VoucherNumber  = entity.VoucherNumber,
            VoucherDate    = entity.VoucherDate,
            Description    = entity.Description,
        };
    }

    // ÇOK-ADIMLI yazım (fiş kaydı + ledger sil/yaz + virman ikizi) → AÇIK transaction ZORUNLU.
    // Global AddAlwaysDisableUnitOfWorkTransaction yalnız OTOMATİK hesaplanan UoW opsiyonlarını etkiler;
    // attribute'un açık IsTransactional değeri interceptor'da onu bypass eder (UnitOfWorkInterceptor.
    // CreateOptions: attribute.IsTransactional != null → provider'a hiç danışılmaz). Dikkat: ambient UoW
    // varsa Begin child olarak katılır ve bu opsiyon YOK SAYILIR — bu metotlar dış UoW'suz çağrılır
    // (Blazor circuit / HTTP entry), garanti oradan gelir.
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<VoucherLineDto> SaveLineAsync(VoucherLineDto input)
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
        if (input.Type == ProcessType.Bullion && input.Direction.IsOutflow())
        {
            await _bullionStockService.PrepareBullionExitLineAsync(input, EnsureCurrentCompanyId());
        }

        // Virman: karşı hesap doğrulaması + LinkId (sunucu otoritedir) + legacy açıklama formatı.
        // Diğer tiplerde virman alanları temizlenir (istemciden sızan değere güvenilmez).
        VoucherTransferService.TransferContext? transferCtx = null;
        if (input.Type == ProcessType.Transfer)
        {
            // Kaynak alt hesap: güncellemede fiş başlığından (otorite), yeni satırda input'tan.
            var sourceSubId = input.VoucherId is { } transferVoucherId
                ? (await GetOwnedVoucherAsync(transferVoucherId)).SubAccountId
                : input.SubAccountId;
            transferCtx = await _transferService.PrepareTransferLineAsync(input, EnsureCurrentCompanyId(), sourceSubId);
        }
        else
        {
            input.CounterAccountId = null;
            input.LinkId           = null;
        }

        // WYSIWYG: ekranda görünen değerler AYNEN kaydedilir (sunucu recompute yok).
        var lineInput = VoucherLineDtoFactory.ToLineInput(input);

        Voucher voucher;
        Guid lineId;

        if (input.VoucherId is { } voucherId)
        {
            // Aitlik: yabancı şirketin fişine satır eklenemez/güncellenemez.
            voucher = await GetOwnedVoucherAsync(voucherId);
            await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

            // Bayat istemci kontrolü: okuma anındaki stamp değişmişse (başkası düzenledi) reddet.
            EnsureVoucherNotStale(voucher, input.VoucherConcurrencyStamp);

            VoucherLine savedLine;
            VoucherLineChangeType changeType;
            if (input.Id != Guid.Empty)
            {
                voucher.UpdateLine(input.Id, lineInput);
                lineId = input.Id;
                savedLine  = voucher.Lines.First(l => l.Id == lineId);
                changeType = VoucherLineChangeType.Updated;
            }
            else
            {
                savedLine  = voucher.AddLine(GuidGenerator.Create(), lineInput);
                lineId     = savedLine.Id;
                changeType = VoucherLineChangeType.Created;
            }

            await _repository.UpdateAsync(voucher, autoSave: true);   // ABP stamp döngüsü paralel isteği zaten yakalar

            // Gölge günlük — çekirdek posting/bakiyeyi ETKİLEMEZ, AYNI UoW içinde (2026-07-15 kullanıcı isteği).
            await _historyRecorder.RecordAsync(voucher, savedLine, changeType);
        }
        else
        {
            // Fiş lazy oluşturulur + numara atanır. CompanyId ambient'ten zorlanır (client'a güvenilmez),
            // şube/kasa aitliği doğrulanır (sızıntı önleme — BalanceSheet ile aynı ilke).
            var companyId = EnsureCurrentCompanyId();
            await EnsureOrgScopeAsync(companyId, input.BranchId, input.VaultId);

            // Satır kaydı DAİMA dış cari yoludur: iç kip satırı postlamaz, Teyit kurar (materyalizasyon
            // ConfirmationVoucherMaterializer'da, kasa başlıklı fişle). Kodlar sunucu-otoriter çözülür.
            var counterparty = await _counterpartyResolver.ResolveCurrentAccountAsync(
                companyId, input.AccountId, input.SubAccountId);

            voucher = new Voucher(
                companyId,
                input.BranchId,
                input.VaultId,
                counterparty.AccountType,
                counterparty.AccountId,
                counterparty.AccountCode,
                counterparty.SubAccountId,
                counterparty.SubAccountCode,
                await _numberAllocator.NextNumberAsync(companyId),
                input.VoucherDate,
                input.VoucherDescription);

            var newLine = voucher.AddLine(GuidGenerator.Create(), lineInput);
            lineId = newLine.Id;

            await _numberAllocator.InsertNumberedAsync(voucher);

            // Gölge günlük — çekirdek posting/bakiyeyi ETKİLEMEZ, AYNI UoW içinde (2026-07-15 kullanıcı isteği).
            await _historyRecorder.RecordAsync(voucher, newLine, VoucherLineChangeType.Created);
        }

        // Ledger senkronu (poster çıktısı → kalıcı): voucher kaydedildikten sonra, aynı UoW içinde.
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        // Virman: karşı bacak (ikiz satır) AYNI UoW içinde — karşı hesabın kendi fişinde bul/oluştur/güncelle
        // + o fişin ledger senkronu. İki bacak tek transaction'da tutarlı yazılır.
        if (transferCtx is not null)
        {
            await _transferService.SyncTransferTwinAsync(voucher, lineId, lineInput, transferCtx);
        }

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
        // SubAccountId POLİMORFİK anahtardır (cari kipinde alt hesap, kasa kipinde kasa) → kasa fiş listesi
        // de bu sorgudan, imza/filtre değişmeden gelir.
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
        // VoucherListRequestDto ListRequestDto'dan TÜREMEZ (kendi bağımsız sözleşmesi) → ApplyPaging/AllPages
        // semantiği burada geçerli DEĞİL; sayfalama elle kalır.
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

        var dtos = displayed.Select(VoucherLineDtoFactory.MapLine).ToList();
        foreach (var d in dtos)
        {
            d.VoucherDate = voucher.VoucherDate;
            d.VoucherNumber = voucher.VoucherNumber;
            d.VoucherConcurrencyStamp = voucher.ConcurrencyStamp;   // düzenlemede bayat-istemci tespiti için
        }
        await _codeResolver.ResolveUnitCodesAsync(dtos);
        await _codeResolver.ResolveCounterAccountCodesAsync(dtos);
        await _codeResolver.ResolveCreatorNamesAsync(dtos);

        // Yürüyen bakiye: devreden (ilk satırdan ÖNCEKİ tüm satırlar) + satır-satır birikim.
        if (displayed.Count > 0)
        {
            var subId    = voucher.SubAccountId;
            var boundary = displayed[0].CreationTime;
            var carryLines = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(v => v.CompanyId == voucher.CompanyId && v.SubAccountId == subId)
                    .SelectMany(v => v.Lines)
                    .Where(l => !l.IsDeleted && l.CreationTime < boundary));

            await _statementService.AssignRunningBalancesAsync(displayed, dtos, carryLines);
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

    /// <summary>Hesap ekstresi (legacy <c>Cari.HesapExtresiEx</c> paritesi) — <see cref="VoucherStatementService"/>'e delege.</summary>
    public async Task<AccountStatementDto> GetAccountStatementAsync(
        Guid subAccountId, DateTime start, DateTime endExclusive, List<ProcessType>? types = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        return await _statementService.GetAccountStatementAsync(companyId, subAccountId, start, endExclusive, types);
    }

    public async Task<VoucherLineDto> GetLineForEditAsync(Guid lineId)
    {
        // Company scope: yabancı şirketin satırı YOKMUŞ gibi davranılır (sızıntı önleme —
        // GetLinesAsync/DeleteLineAsync ile aynı aitlik ilkesi, sorgu fiş başlığına filtrelenir).
        var companyId = EnsureCurrentCompanyId();
        var line = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.CompanyId == companyId)
                .SelectMany(v => v.Lines)
                .Where(l => l.Id == lineId && !l.IsDeleted))
            ?? throw new EntityNotFoundException(typeof(VoucherLine), lineId);

        var dto = VoucherLineDtoFactory.MapLine(line);
        // Fişin stamp'ini de taşı — kaydetmede bayat-istemci tespiti (ConcurrencyConflict) için.
        dto.VoucherConcurrencyStamp = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.Id == line.VoucherId)
                .Select(v => v.ConcurrencyStamp));
        if (line.MainUnitId != Guid.Empty)
        {
            dto.MainUnitCode = await _codeResolver.ResolveUnitCodeAsync(line.MainUnitId);
        }
        if (line.PayUnitId is { } pid)
        {
            dto.PayUnitCode = await _codeResolver.ResolveUnitCodeAsync(pid);
        }
        return dto;
    }

    // Çok-adımlı: satır düşür + ledger senkronu + virman ikizinin fişi/ledger'ı → tek transaction.
    [UnitOfWork(isTransactional: true)]
    public virtual async Task DeleteLineAsync(Guid voucherId, Guid lineId, string reason)
    {
        var voucher = await GetOwnedVoucherAsync(voucherId);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

        // Silme de Save ile AYNI per-tip yetkiye tabidir — Metal işlemi yapamayan, Metal satırı da silemez
        // (UI gate'i bypass eden doğrudan API çağrılarına karşı; entegrasyon analizi E-2).
        var line = voucher.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new EntityNotFoundException(typeof(VoucherLine), lineId);
        await EnsureTransactionPermissionAsync(line.Type);

        // Silmeden ÖNCE snapshot: soft-delete olduğundan satır hâlâ okunabilir ama anlamlı anlık görüntü
        // (silinmemiş SON hâl) IsDeleted işaretinden ÖNCE alınmalı.
        await _historyRecorder.RecordAsync(voucher, line, VoucherLineChangeType.Deleted);

        voucher.RemoveLine(lineId);
        await _repository.UpdateAsync(voucher, autoSave: true);   // ABP stamp döngüsü paralel isteği zaten yakalar
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        // Virman: ikiz satır BAŞKA fişte yaşar — silmede ikisi birlikte düşer (tutarlılık).
        if (line.Type == ProcessType.Transfer && line.LinkId is { } linkId)
        {
            await _transferService.RemoveTransferTwinAsync(linkId, lineId);
        }

        // VoucherLineLog gelene kadar nedeni log'a yaz (kalıcı geçmiş ertelendi).
        Logger.LogInformation("VoucherLine {LineId} silindi. Neden: {Reason}", lineId, reason);
    }

    public async Task<AccountBalanceDto> GetBalancesAsync(Guid subAccountId, DateTime? upTo = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        return await _statementService.GetBalancesAsync(companyId, subAccountId, upTo);
    }

    public async Task<AccountBalanceDto> GetAccountBalancesAsync(Guid accountId, DateTime? upTo = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        return await _statementService.GetAccountScopedBalancesAsync(companyId, accountId, upTo);
    }

    public async Task<UnitStatementDto> GetUnitStatementAsync(
        Guid scopeId, bool scopeIsAccount, Guid unitId, DateTime start, DateTime endExclusive)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        return await _statementService.GetUnitStatementAsync(companyId, scopeIsAccount, scopeId, unitId, start, endExclusive);
    }


    public async Task<List<BullionStockItemDto>> GetBullionStockAsync(bool? inStock = null)
    {
        var companyId = EnsureCurrentCompanyId();   // company scope (sızıntı önleme)
        return await _bullionStockService.GetBullionStockAsync(companyId, inStock);
    }

    public async Task<AssayStockDto> GetAssayStockAsync()
    {
        var companyId = EnsureCurrentCompanyId();   // company scope ZORUNLU (sızıntı önleme)
        return await _bullionStockService.GetAssayStockAsync(companyId);
    }

    // Çok-adımlı: virman ikizleri (başka fişler) + ledger temizliği + fiş silme → tek transaction.
    [UnitOfWork(isTransactional: true)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Aitlik + per-tip yetki: fişteki HER farklı işlem tipi için ayrı yetki gerekir
        // (tek tipte bile yetkisizse fişin tamamı silinemez; entegrasyon analizi E-2).
        var voucher = await GetOwnedVoucherAsync(id);
        await _repository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);
        foreach (var type in voucher.Lines.Where(l => !l.IsDeleted).Select(l => l.Type).Distinct())
        {
            await EnsureTransactionPermissionAsync(type);
        }

        // Virman satırlarının ikizleri BAŞKA fişlerde yaşar — fiş silinirken ikizler de tutarlı düşer
        // (aksi hâlde karşı hesapta tek bacak kalır; çift bacak değişmezi bozulur).
        var transferLines = voucher.Lines
            .Where(l => !l.IsDeleted && l.Type == ProcessType.Transfer && l.LinkId != null)
            .ToList();
        foreach (var line in transferLines)
        {
            await _transferService.RemoveTransferTwinAsync(line.LinkId!.Value, line.Id);
        }

        await _ledgerSynchronizer.DeleteVoucherAsync(id);
        await _repository.DeleteAsync(id, autoSave: true);
    }
}
