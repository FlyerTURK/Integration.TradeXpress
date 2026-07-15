using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Virman (Transfer) çift-bacak motoru: birincil satırın doğrulanması/hazırlanması + karşı bacağın
/// (ikiz satır) bul/oluştur/güncelle/sil senkronu. İki bacak AYNI ambient UoW/transaction'da yazılır;
/// etkilenen her fişin ledger'ı ayrıca senkronlanır. Company scope ve fiş-aitlik guard'ları çağıran
/// AppService'te kalır — kaynak alt hesap parametreyle gelir.
/// </summary>
public class VoucherTransferService : ITransientDependency
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly VoucherNumberAllocator _numberAllocator;
    private readonly VoucherCounterpartyResolver _counterpartyResolver;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly VoucherLineHistoryRecorder _historyRecorder;

    public VoucherTransferService(
        IRepository<Voucher, Guid> repository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        VoucherNumberAllocator numberAllocator,
        VoucherCounterpartyResolver counterpartyResolver,
        IGuidGenerator guidGenerator,
        IAsyncQueryableExecuter asyncExecuter,
        VoucherLineHistoryRecorder historyRecorder)
    {
        _repository           = repository;
        _subAccountRepository = subAccountRepository;
        _accountRepository    = accountRepository;
        _ledgerSynchronizer   = ledgerSynchronizer;
        _numberAllocator      = numberAllocator;
        _counterpartyResolver = counterpartyResolver;
        _guidGenerator        = guidGenerator;
        _asyncExecuter        = asyncExecuter;
        _historyRecorder      = historyRecorder;
    }

    /// <summary>Virman hazırlığının taşıyıcısı: kaynak/karşı alt hesap kimlikleri + karşı hesabın üst
    /// hesabı (ikiz fiş başlığı için) + açıklama bileşenleri (ikizde kodlar ters çevrilir).</summary>
    public sealed record TransferContext(
        Guid SourceSubAccountId,
        Guid CounterSubAccountId,
        Guid CounterParentAccountId,
        string SourceCode,
        string CounterCode,
        string RawDescription);

    /// <summary>Virman satırını kaydetmeden ÖNCE doğrular ve sunucu-otoriter alanları doldurur:
    /// karşı alt hesap zorunlu + kaynaktan farklı + AYNI şirkette (SubAccount→Account→CompanyId) + aktif;
    /// LinkId güncellemede mevcut satırdan okunur (istemciye güvenilmez), yeni satırda üretilir;
    /// açıklama legacy "{kaynak}/{karşı}:{desc}" formatına çevrilir.</summary>
    public async Task<TransferContext> PrepareTransferLineAsync(VoucherLineDto input, Guid companyId, Guid? sourceSubAccountId)
    {
        if (sourceSubAccountId is not { } sourceId || sourceId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Voucher:TransferSourceRequired");
        }

        if (input.CounterAccountId is not { } counterId || counterId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Voucher:TransferCounterRequired");
        }

        if (counterId == sourceId)
        {
            throw new BusinessException("TradeXpress:Voucher:TransferCounterSameAccount");
        }

        // Karşı alt hesap aitliği: SubAccount → Account → CompanyId zinciri working şirkete çıkmalı;
        // pasif hesaba virman açılmaz (UI datasource'u da filtreler — burası son savunma hattı).
        var counterSub     = await _subAccountRepository.FindAsync(counterId);
        var counterAccount = counterSub is null ? null : await _accountRepository.FindAsync(counterSub.AccountId);
        if (counterSub is null || !counterSub.IsActive || counterAccount is null || counterAccount.CompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Voucher:TransferCounterNotFound");
        }

        var sourceSub = await _subAccountRepository.GetAsync(sourceId);

        // LinkId (legacy RefNo): güncellemede mevcut satırın kimliği korunur, yeni satırda üretilir.
        Guid linkId;
        if (input.Id != Guid.Empty)
        {
            var existing = await _asyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .SelectMany(v => v.Lines)
                    .Where(l => l.Id == input.Id && !l.IsDeleted));
            linkId = existing?.LinkId ?? _guidGenerator.Create();
        }
        else
        {
            linkId = _guidGenerator.Create();
        }
        input.LinkId = linkId;

        // Açıklama — legacy formatı "{kaynak}/{karşı}:{desc}". Düzenlemede eski önek soyulur (çift önek olmaz).
        var raw = StripTransferPrefix(input.Description);
        input.Description = ComposeTransferDescription(sourceSub.Code, counterSub.Code, raw);

        return new TransferContext(sourceId, counterId, counterSub.AccountId, sourceSub.Code, counterSub.Code, raw);
    }

    /// <summary>Birincil satır kaydedildikten sonra karşı bacağı senkronlar: ikiz yoksa karşı hesabın YENİ
    /// fişinde açılır; varsa yerinde güncellenir; karşı hesap DEĞİŞTİYSE eski ikiz düşer, yenisi açılır.
    /// Etkilenen her fişin ledger'ı ayrıca senkronlanır (hepsi aynı UoW/transaction).</summary>
    public async Task SyncTransferTwinAsync(
        Voucher primaryVoucher, Guid primaryLineId, VoucherLineInput lineInput, TransferContext ctx)
    {
        var twinInput = BuildTransferTwinInput(lineInput, ctx);
        var linkId    = lineInput.LinkId!.Value;

        var twin = await _asyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .SelectMany(v => v.Lines)
                .Where(l => l.LinkId == linkId && l.Id != primaryLineId && !l.IsDeleted));

        if (twin is null)
        {
            await CreateTransferTwinVoucherAsync(primaryVoucher, twinInput, ctx);
            return;
        }

        var twinVoucher = await _repository.GetAsync(twin.VoucherId);
        await _repository.EnsureCollectionLoadedAsync(twinVoucher, v => v.Lines);

        if (twinVoucher.SubAccountId == ctx.CounterSubAccountId)
        {
            twinVoucher.UpdateLine(twin.Id, twinInput);
            await _repository.UpdateAsync(twinVoucher, autoSave: true);
            await _ledgerSynchronizer.SyncVoucherAsync(twinVoucher);

            var updatedTwin = twinVoucher.Lines.First(l => l.Id == twin.Id);
            await _historyRecorder.RecordAsync(twinVoucher, updatedTwin, VoucherLineChangeType.Updated);
            return;
        }

        // Karşı hesap değişti: fiş = tek cari olduğundan ikiz taşınamaz — eski fişten düşer,
        // yeni karşı hesabın fişinde yeniden açılır (LinkId aynı kalır).
        await _historyRecorder.RecordAsync(twinVoucher, twin, VoucherLineChangeType.Deleted);
        twinVoucher.RemoveLine(twin.Id);
        await _repository.UpdateAsync(twinVoucher, autoSave: true);
        await _ledgerSynchronizer.SyncVoucherAsync(twinVoucher);
        await CreateTransferTwinVoucherAsync(primaryVoucher, twinInput, ctx);
    }

    /// <summary>Virman satırını (birincil ya da ikiz) LinkId üzerinden bulup KENDİ fişinden düşürür ve
    /// o fişin ledger'ını senkronlar — satır/fiş silme yollarının ortak ikiz-temizliği.</summary>
    public async Task RemoveTransferTwinAsync(Guid linkId, Guid excludedLineId)
    {
        var twin = await _asyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .SelectMany(v => v.Lines)
                .Where(l => l.LinkId == linkId && l.Id != excludedLineId && !l.IsDeleted));
        if (twin is null)
        {
            return;   // ikiz zaten yok (yarım kalmış eski veri) — sessizce geç, silme akışı sürsün
        }

        var twinVoucher = await _repository.GetAsync(twin.VoucherId);
        await _repository.EnsureCollectionLoadedAsync(twinVoucher, v => v.Lines);

        await _historyRecorder.RecordAsync(twinVoucher, twin, VoucherLineChangeType.Deleted);

        twinVoucher.RemoveLine(twin.Id);
        await _repository.UpdateAsync(twinVoucher, autoSave: true);
        await _ledgerSynchronizer.SyncVoucherAsync(twinVoucher);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Karşı hesabın YENİ fişini (başlık birincil fişten kopya: şube/kasa/tarih/açıklama;
    /// cari = karşı hesap) numaralandırıp ikiz satırla oluşturur ve ledger'ını senkronlar.</summary>
    private async Task CreateTransferTwinVoucherAsync(
        Voucher primaryVoucher, VoucherLineInput twinInput, TransferContext ctx)
    {
        // Virman DAİMA cari↔cari'dir (kasa karşı tarafı Teyit yolundan gelir) → karşı taraf CurrentAccount;
        // kod snapshot'ları sunucu-otoriter çözülür (fişin diğer yazma yollarıyla aynı kapı).
        var counterparty = await _counterpartyResolver.ResolveCurrentAccountAsync(
            primaryVoucher.CompanyId, ctx.CounterParentAccountId, ctx.CounterSubAccountId);

        var counterVoucher = new Voucher(
            primaryVoucher.CompanyId,
            primaryVoucher.BranchId,
            primaryVoucher.VaultId,
            counterparty.AccountType,
            counterparty.AccountId,
            counterparty.AccountCode,
            counterparty.SubAccountId,
            counterparty.SubAccountCode,
            await _numberAllocator.NextNumberAsync(primaryVoucher.CompanyId),
            primaryVoucher.VoucherDate,
            primaryVoucher.Description);

        var twinLine = counterVoucher.AddLine(_guidGenerator.Create(), twinInput);

        await _numberAllocator.InsertNumberedAsync(counterVoucher);

        await _ledgerSynchronizer.SyncVoucherAsync(counterVoucher);

        await _historyRecorder.RecordAsync(counterVoucher, twinLine, VoucherLineChangeType.Created);
    }

    /// <summary>İkiz satır girdisi: birincil satırın kopyası, yön TERS (Giriş↔Çıkış), karşı referans
    /// kaynağa döner, LinkId ve PayTotal/PayUnit AYNI; açıklamada kodlar ters çevrilir.</summary>
    private static VoucherLineInput BuildTransferTwinInput(VoucherLineInput primary, TransferContext ctx)
    {
        return primary with
        {
            Direction = primary.Direction == ProcessDirectionType.Inbound
                ? ProcessDirectionType.Outbound
                : ProcessDirectionType.Inbound,
            CounterAccountId = ctx.SourceSubAccountId,
            Description      = ComposeTransferDescription(ctx.CounterCode, ctx.SourceCode, ctx.RawDescription),
        };
    }

    /// <summary>Legacy açıklama formatı: "{kendi}/{karşı}:{desc}" — taşarsa kolon sınırına kırpılır.</summary>
    private static string ComposeTransferDescription(string ownCode, string otherCode, string raw)
    {
        var composed = $"{ownCode}/{otherCode}:{raw}";
        return composed.Length <= VoucherConsts.DescriptionMaxLength
            ? composed
            : composed[..VoucherConsts.DescriptionMaxLength];
    }

    /// <summary>Var olan "{X}/{Y}:" önekini soyar (düzenlemede çift önek birikmesin). Sezgisel kural:
    /// ilk ':' öncesi tek '/' içeren, boşluksuz bir baş ise önek sayılır — hesap kodları normalize
    /// (boşluksuz/üst-harf) olduğundan güvenli; kullanıcı metni nadiren bu kalıba düşer.</summary>
    private static string StripTransferPrefix(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        var colon = description.IndexOf(':');
        if (colon > 0)
        {
            var head  = description[..colon];
            var slash = head.IndexOf('/');
            if (slash > 0 && head.IndexOf('/', slash + 1) < 0 && !head.Contains(' '))
            {
                return description[(colon + 1)..];
            }
        }

        return description;
    }
}
