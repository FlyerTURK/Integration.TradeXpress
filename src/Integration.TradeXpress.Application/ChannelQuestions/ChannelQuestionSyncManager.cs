using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Müşteri sorusu SENKRON çekirdeği — <b>turda TEK İŞ ADIMI</b> yürütür.
///
/// <para><b>Tasarımın tek belirleyicisi (2026-08-01 canlı keşif): DAKİKADA 1 ÇAĞRI.</b> Kota tüm hesap için
/// ortaktır ve paralellikle aşılamaz (3 eşzamanlı çağrıdan 2'si <c>accessLimit</c>). Bu yüzden:
/// (1) pazaryerine giden TEK yer worker'dır — UI asla doğrudan çağırmaz, yalnız
/// <see cref="RequestPriorityAsync"/> ile sıraya işaret koyar; (2) her tur TEK çağrı harcar; (3) seed, artımlı
/// tazeleme ve elle tetik AYNI havuzu paylaştığı için tek bir sırada yarışırlar.</para>
///
/// <para><b>Tur seçim sırası</b> (<see cref="ChannelQuestionWorkKind"/>): öncelik işareti → hiç tazelenmemiş
/// kanalın İLK açık-soru çekimi → geçmiş seedi (ay ay geriye) → rutin tazeleme (eşiği dolmuş, en eski tazelenen).
/// Aynı sınıftan birden çok aday varsa en uzun süredir tur almayan kazanır (adalet imleci
/// <see cref="ChannelQuestionSyncSignals"/>'da).</para>
///
/// <para><b>Kota hatası İSTİSNA DEĞİLDİR:</b> istemci <c>RateLimited=true</c> döner; bu turda HİÇBİR yazma
/// yapılmaz (ne soru ne de ilerleme defteri) ve iş bir sonraki tura ertelenir. Yarım ilerleme yazmak, kaçırılan
/// bir ayı sessizce atlamak demek olurdu.</para>
///
/// <para><b>Neden AppService değil DomainService:</b> worker'da kullanıcı yoktur; <c>[Authorize]</c>
/// interceptor'ı kimliksiz bağlamda patlardı (<c>OrderSyncManager</c>/<c>N11CategorySyncManager</c> emsali).</para>
/// </summary>
public class ChannelQuestionSyncManager : DomainService
{
    /// <summary>Tek pencerede (ay ya da artımlı sorgu) izin verilen azami sayfa — bozuk <c>pageCount</c>
    /// yüzünden bir ayda sonsuza dek dönmeyelim (100'lük sayfayla 20.000 soru; gerçekte erişilmez).</summary>
    private const int MaxPagesPerWindow = 200;

    private readonly IRepository<ChannelQuestion, Guid> _questionRepository;
    private readonly IRepository<ChannelQuestionSyncState, Guid> _stateRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<Tenant, Guid> _tenantRepository;
    private readonly IEnumerable<IChannelQuestionClient> _clients;
    private readonly ChannelQuestionSyncSignals _signals;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    public ChannelQuestionSyncManager(
        IRepository<ChannelQuestion, Guid> questionRepository,
        IRepository<ChannelQuestionSyncState, Guid> stateRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<Tenant, Guid> tenantRepository,
        IEnumerable<IChannelQuestionClient> clients,
        ChannelQuestionSyncSignals signals,
        IDataFilter dataFilter,
        IUnitOfWorkManager uowManager,
        OrchestrationIdentityScope identityScope,
        ICurrentPrincipalAccessor currentPrincipalAccessor)
    {
        _questionRepository = questionRepository;
        _stateRepository = stateRepository;
        _channelRepository = channelRepository;
        _tenantRepository = tenantRepository;
        _clients = clients;
        _signals = signals;
        _dataFilter = dataFilter;
        _uowManager = uowManager;
        _identityScope = identityScope;
        _currentPrincipalAccessor = currentPrincipalAccessor;
    }

    // ── Dış API ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>UI tetiği — pazaryerine GİTMEZ. "Bu kanalı sıradaki turda öncelikli çek" işareti koyar; kullanıcı
    /// mevcut veriyi görmeye devam eder. Doğrudan çağrı yapılsaydı aynı dakikada sayfayı açan ikinci kullanıcı
    /// kota hatası alır ve worker'ın hakkını yerdi (canlı keşif SINIR 1).</summary>
    public virtual Task RequestPriorityAsync(Guid salesChannelId)
    {
        _signals.RequestPriority(salesChannelId);
        return Task.CompletedTask;
    }

    /// <summary>Worker girişi — dakikada bir çağrılır, TEK iş adımı yürütür. Dönen değer bu turda yazılan
    /// (eklenen + güncellenen) soru sayısıdır; iş yoksa ya da kota penceresine takıldıysa 0.</summary>
    public virtual async Task<int> RunOnePassAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = Clock.Now.ToUniversalTime();

        var candidate = await PlanNextStepAsync(nowUtc);
        if (candidate is null)
        {
            return 0;
        }

        // Adalet imleci turu AYIRIR AYIRMAZ işaretlenir (başarı/başarısızlık fark etmez): kota hatası alan bir
        // kanal defterine yazmadığı için, bellekteki bu timestamp olmasa sıradaki tur yine aynı kanalı seçer ve
        // sistem tek bir kanala kilitlenirdi.
        _signals.RegisterAttempt(candidate.SalesChannelId, nowUtc);

        return await ExecuteStepAsync(candidate, nowUtc, cancellationToken);
    }

    // ── 1) Planlama: TÜM tenant'ları tara, TEK aday seç (pazaryerine hiç gitmez) ───────────────────────

    /// <summary>Tüm tenant'ların uygun kanallarını gezip iş adayı toplar ve en önceliklisini seçer. Bu faz
    /// SALT-OKUMA'dır (defter yazılmaz): kota hatası olasılığı yüzünden, gerçekten çağrı yapılmadan hiçbir
    /// ilerleme kalıcılaşmamalıdır.</summary>
    private async Task<SyncCandidate?> PlanNextStepAsync(DateTime nowUtc)
    {
        var supportedChannelTypes = _clients.Select(client => client.ChannelType).ToHashSet();
        if (supportedChannelTypes.Count == 0)
        {
            return null;
        }

        var candidates = new List<SyncCandidate>();
        foreach (var tenantId in await GetTenantIdsAsync())
        {
            // Tenant DEĞİŞİMİ sonrası TAZE UoW şart (OrderSyncManager emsali): DbContext o tenant'a bağlanmazsa
            // kanallar global filtreyle gizli kalır. Şirket filtresi de kapatılır — worker'ın working company'si
            // yoktur, tenant'ın TÜM şirketlerinin kanalları görünmelidir (tenant izolasyonu Change ile korunur).
            using (CurrentTenant.Change(tenantId))
            using (_dataFilter.Disable<ICompanyScoped>())
            using (var uow = _uowManager.Begin(requiresNew: true))
            {
                candidates.AddRange(await CollectCandidatesAsync(tenantId, supportedChannelTypes, nowUtc));
                await uow.CompleteAsync();
            }
        }

        // Sıra: iş sınıfı (öncelik → ilk çekim → seed → rutin), sonra "en uzun süredir bekleyen", sonra
        // belirlenimci tie-breaker (aynı girdiyle aynı çıktı — test edilebilirlik).
        return candidates
            .OrderBy(c => (int)c.Kind)
            .ThenBy(c => c.OrderKey)
            .ThenBy(c => c.SalesChannelId)
            .FirstOrDefault();
    }

    private async Task<List<SyncCandidate>> CollectCandidatesAsync(
        Guid? tenantId, HashSet<SalesChannelType> supportedChannelTypes, DateTime nowUtc)
    {
        var candidates = new List<SyncCandidate>();

        var channels = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync()).Where(c => c.IsActive));
        if (channels.Count == 0)
        {
            return candidates;
        }

        var channelIds = channels.Select(c => c.Id).ToList();
        var states = (await AsyncExecuter.ToListAsync(
                (await _stateRepository.GetQueryableAsync()).Where(s => channelIds.Contains(s.SalesChannelId))))
            .ToDictionary(s => s.SalesChannelId);

        var currentMonthStart = ToMonthStart(nowUtc);

        foreach (var channel in channels)
        {
            if (ResolveChannelType(channel) is not { } channelType || !supportedChannelTypes.Contains(channelType))
            {
                continue;
            }

            states.TryGetValue(channel.Id, out var state);
            var candidate = BuildCandidate(tenantId, channel, channelType, state, currentMonthStart, nowUtc);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    /// <summary>Bir kanalın SIRADAKİ işini belirler (kanal başına EN FAZLA bir aday — sınıflar birbirini eler).</summary>
    private SyncCandidate? BuildCandidate(
        Guid? tenantId,
        SalesChannelBase channel,
        SalesChannelType channelType,
        ChannelQuestionSyncState? state,
        DateTime currentMonthStart,
        DateTime nowUtc)
    {
        var lastAttempt = _signals.GetLastAttemptUtc(channel.Id);

        if (_signals.HasPriority(channel.Id))
        {
            return NewCandidate(ChannelQuestionWorkKind.Priority, lastAttempt, state?.RefreshPageIndex ?? 0, null);
        }

        // Kanalın AÇIK soruları HİÇ çekilmediyse ilk iş budur — tek çağrı, ve cevap bekleyen soruları görünür
        // kılar. Geçmiş seedi bundan önce gelseydi (seed 60 tura kadar sürebilir, kanallar sırayı paylaşır)
        // yeni kurulan bir kanalda bekleyen sorular saatlerce görünmezdi. Bootstrap KANAL BAŞINA BİR KEREDİR.
        if (state is null || state.LastRefreshedAt is null)
        {
            return NewCandidate(ChannelQuestionWorkKind.InitialRefresh, lastAttempt, state?.RefreshPageIndex ?? 0, null);
        }

        if (!state.SeedCompleted)
        {
            var seedMonthStart = state.SeedMonthStart ?? currentMonthStart;
            return NewCandidate(ChannelQuestionWorkKind.Seed, lastAttempt, state.SeedPageIndex, seedMonthStart);
        }

        if (state.IsRefreshDue(nowUtc))
        {
            // Rutin sırada "en eski tazelenen kanal" önde (eşik zaten dolmuş adaylar arasında adalet).
            return NewCandidate(
                ChannelQuestionWorkKind.RoutineRefresh,
                state.LastRefreshedAt ?? DateTime.MinValue,
                state.RefreshPageIndex,
                null);
        }

        return null;

        SyncCandidate NewCandidate(ChannelQuestionWorkKind kind, DateTime orderKey, int pageIndex, DateTime? seedMonthStart)
        {
            return new SyncCandidate(tenantId, channel.Id, channel.CompanyId, channelType, kind, orderKey, pageIndex, seedMonthStart);
        }
    }

    // ── 2) Yürütme: TEK uzak çağrı + (başarılıysa) yazma ──────────────────────────────────────────────

    private async Task<int> ExecuteStepAsync(SyncCandidate candidate, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var client = _clients.First(c => c.ChannelType == candidate.ChannelType);

        using (CurrentTenant.Change(candidate.TenantId))
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            // KİMLİK (OrderSyncManager deseni): worker principal'sız koşar; zincirin ileride [Authorize] bir
            // servise inmesi hâlinde kimliksiz bağlam AbpAuthorizationException verirdi. Change ÇAĞIRANIN
            // frame'inde kurulur (AsyncLocal kuralı — OrchestrationIdentityScope doc'u).
            // FARK: admin bulunamazsa tenant ATLANMAZ, çekim kimliksiz sürer. Gerekçe: bu zincir bugün hiçbir
            // [Authorize] servise inmiyor (yalnız repository + kanal istemcisi) ve tenant'ı atlamak, admin'i
            // olmayan bir tenant'ın sorularını SÜRESİZ görünmez kılardı. Kimliksiz koşmak yetki GENİŞLETMEZ.
            var principal = await _identityScope.BuildTenantAdminPrincipalAsync();
            if (principal is null)
            {
                Logger.LogWarning(
                    "Soru senkronu: tenant {Tenant} için admin bulunamadı — çekim KİMLİKSİZ sürdürülüyor.",
                    candidate.TenantId);
                return await RunStepAsync(candidate, client, nowUtc, cancellationToken);
            }

            using (_currentPrincipalAccessor.Change(principal))
            {
                return await RunStepAsync(candidate, client, nowUtc, cancellationToken);
            }
        }
    }

    private async Task<int> RunStepAsync(
        SyncCandidate candidate, IChannelQuestionClient client, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var query = BuildQuery(candidate);

        // Uzak çağrı UoW DIŞINDA: SOAP isteği saniyeler sürebilir, DbContext'i o süre boyunca açık tutmak
        // (ve Sqlite/SQL Server bağlantısını meşgul etmek) gereksiz (N11CategorySyncManager emsali).
        RemoteQuestionPage page;
        try
        {
            page = await client.FetchPageAsync(candidate.SalesChannelId, query, cancellationToken);
        }
        catch (Exception ex)
        {
            // Ağ/kimlik/ayrıştırma hatası turu düşürür ama döngüyü ÖLDÜRMEZ; ilerleme yazılmadığı için aynı
            // adım bir sonraki turda yeniden denenir.
            Logger.LogWarning(ex, "Soru çekimi atlandı (kanal {Channel}, adım {Kind}) — kimlik/ağ/ayrıştırma?",
                candidate.SalesChannelId, candidate.Kind);
            return 0;
        }

        if (page.RateLimited)
        {
            // Kota penceresi: İSTİSNA DEĞİL, beklenen bir durum. Hiçbir şey yazılmaz — ne soru ne defter.
            Logger.LogInformation(
                "Soru çekimi kota penceresine takıldı (kanal {Channel}, adım {Kind}) — yazma YOK, sıradaki tura ertelendi.",
                candidate.SalesChannelId, candidate.Kind);
            return 0;
        }

        var result = await ApplyPageAsync(candidate, query, page, nowUtc);

        // Öncelik işareti YALNIZ başarılı çekimden sonra tüketilir (kota hatasında kullanıcının isteği durur).
        if (candidate.Kind == ChannelQuestionWorkKind.Priority)
        {
            _signals.ClearPriority(candidate.SalesChannelId);
        }

        await TryEnrichNewQuestionAsync(candidate, client, result.FirstNewRemoteQuestionId, nowUtc, cancellationToken);

        return result.WrittenCount;
    }

    /// <summary>Adım türüne göre uzak sorguyu kurar.
    /// <para><b>Artımlı/öncelikli</b> = <c>OnlyOpen</c> (tarihsiz çalışır — canlı doğrulandı).
    /// <b>Seed</b> = kapalı sorular, AY AY: canlıda 6,5 yıllık aralık <c>interval.over.max.limit</c> ile
    /// reddedildi, 1 ay çalıştı. Bitiş günü bugünden ileri taşınmaz (gelecek tarihli pencere anlamsız).</para>
    /// <para>Tarihler GÜN semantiktir (iş tarihi) — timezone kaydırması UYGULANMAZ (CLAUDE.md zaman kuralı).</para></summary>
    private static ChannelQuestionQuery BuildQuery(SyncCandidate candidate)
    {
        if (candidate.Kind != ChannelQuestionWorkKind.Seed)
        {
            return new ChannelQuestionQuery(
                OnlyOpen: true,
                StartDate: null,
                EndDate: null,
                PageIndex: candidate.PageIndex,
                PageSize: ChannelQuestionSyncConsts.PageSize);
        }

        var monthStart = candidate.SeedMonthStart ?? DateTime.MinValue;
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        return new ChannelQuestionQuery(
            OnlyOpen: false,
            StartDate: monthStart,
            EndDate: monthEnd,
            PageIndex: candidate.PageIndex,
            PageSize: ChannelQuestionSyncConsts.PageSize);
    }

    // ── 3) Yazma: idempotent upsert + defter ilerlemesi (TEK UoW) ─────────────────────────────────────

    private async Task<PageApplyResult> ApplyPageAsync(
        SyncCandidate candidate, ChannelQuestionQuery query, RemoteQuestionPage page, DateTime nowUtc)
    {
        // Tenant + şirket kapsamı ÇAĞIRANIN frame'inde kuruludur (ExecuteStepAsync) — burada tekrarlanmaz.
        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            var written = 0;
            string? firstNewRemoteQuestionId = null;

            foreach (var remote in page.Items)
            {
                if (string.IsNullOrWhiteSpace(remote.RemoteQuestionId))
                {
                    // Anahtarsız uzak kayıt idempotent upsert EDİLEMEZ (ikinci çekim dublike üretirdi) → atla + raporla.
                    Logger.LogWarning("Soru çekimi: kanal {Channel} için kimliksiz soru atlandı.", candidate.SalesChannelId);
                    continue;
                }

                var isNew = await UpsertQuestionAsync(candidate, query, remote, nowUtc);
                written++;
                if (isNew && firstNewRemoteQuestionId is null)
                {
                    firstNewRemoteQuestionId = remote.RemoteQuestionId;
                }
            }

            await AdvanceStateAsync(candidate, query, page, nowUtc);

            await uow.CompleteAsync();
            return new PageApplyResult(written, firstNewRemoteQuestionId);
        }
    }

    /// <summary>İdempotent upsert — anahtar (SalesChannelId, RemoteQuestionId). Dönen değer: satır YENİ mi.</summary>
    private async Task<bool> UpsertQuestionAsync(
        SyncCandidate candidate, ChannelQuestionQuery query, RemoteQuestion remote, DateTime nowUtc)
    {
        var question = await FindQuestionAsync(candidate, remote.RemoteQuestionId);
        var isNew = question is null;

        question ??= new ChannelQuestion(
            candidate.CompanyId, candidate.SalesChannelId, candidate.ChannelType, remote.RemoteQuestionId);

        question.ApplyRemote(
            remote.RemoteProductId,
            remote.ProductTitle,
            remote.Subject,
            remote.QuestionText,
            remote.CustomerName,
            remote.CustomerEmail,
            remote.QuestionDate,
            ResolveStatus(remote, DefaultStatusFor(query)),
            remote.RemoteStatus,
            remote.IsPublic,
            nowUtc);

        WriteImageUrls(question, remote.ImageUrls);

        if (isNew)
        {
            await _questionRepository.InsertAsync(question, autoSave: true);
        }
        else
        {
            await _questionRepository.UpdateAsync(question, autoSave: true);
        }

        return isNew;
    }

    private async Task<ChannelQuestion?> FindQuestionAsync(SyncCandidate candidate, string remoteQuestionId)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _questionRepository.GetQueryableAsync())
                .Where(q => q.CompanyId == candidate.CompanyId
                            && q.SalesChannelId == candidate.SalesChannelId
                            && q.RemoteQuestionId == remoteQuestionId));
    }

    /// <summary>Defteri ilerletir — sayfa bitmediyse imleç, bittiyse ay kapanışı / tazeleme timestamp'i.</summary>
    private async Task AdvanceStateAsync(
        SyncCandidate candidate, ChannelQuestionQuery query, RemoteQuestionPage page, DateTime nowUtc)
    {
        var state = await GetOrCreateStateAsync(candidate);

        if (candidate.Kind == ChannelQuestionWorkKind.Seed)
        {
            var monthStart = state.EnsureSeedMonth(candidate.SeedMonthStart ?? ToMonthStart(nowUtc));
            var hasMorePages = query.PageIndex + 1 < page.PageCount && query.PageIndex + 1 < MaxPagesPerWindow;
            if (hasMorePages)
            {
                state.AdvanceSeedPage();
            }
            else
            {
                // "Ay boş mu" kararı YALNIZ ilk sayfada anlamlıdır: sonraki sayfalara geçmişsek ay zaten doluydu.
                state.CompleteSeedMonth(monthStart, page.TotalCount == 0);
            }
        }
        else
        {
            var pageCount = Math.Min(page.PageCount, MaxPagesPerWindow);
            state.ApplyRefreshPage(pageCount, nowUtc);
        }

        await _stateRepository.UpdateAsync(state, autoSave: true);
    }

    private async Task<ChannelQuestionSyncState> GetOrCreateStateAsync(SyncCandidate candidate)
    {
        var state = await AsyncExecuter.FirstOrDefaultAsync(
            (await _stateRepository.GetQueryableAsync())
                .Where(s => s.SalesChannelId == candidate.SalesChannelId));
        if (state is not null)
        {
            return state;
        }

        // Defter satırı çekim BAŞARILI olunca doğar: kota hatası alan bir tur hiçbir iz bırakmamalı.
        return await _stateRepository.InsertAsync(
            new ChannelQuestionSyncState(candidate.CompanyId, candidate.SalesChannelId, candidate.ChannelType),
            autoSave: true);
    }

    // ── 4) Detay zenginleştirme (opsiyonel, PAHALI) ───────────────────────────────────────────────────

    /// <summary>Bu turda İLK KEZ görülen bir soru varsa onun DETAYINI çeker (turda EN FAZLA 1 detay).
    /// <para><b>Neden yalnız 1 ve yalnız yeni satır:</b> müşteri adı/e-postası, soru tarihi ve durum YALNIZ
    /// detayda gelir (canlı keşif SINIR 3) ama detay çağrısı da aynı kota havuzunu tüketir. Detay alınamazsa
    /// satır liste verisiyle KALIR — kısmi kayıt, kayıp kayıttan iyidir.</para></summary>
    private async Task TryEnrichNewQuestionAsync(
        SyncCandidate candidate,
        IChannelQuestionClient client,
        string? remoteQuestionId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteQuestionId))
        {
            return;
        }

        RemoteQuestion? detail;
        try
        {
            detail = await client.FetchDetailAsync(candidate.SalesChannelId, remoteQuestionId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Soru detayı atlandı (kanal {Channel}, soru {Question}) — kota/ağ?",
                candidate.SalesChannelId, remoteQuestionId);
            return;
        }

        if (detail is null)
        {
            return;
        }

        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            var question = await FindQuestionAsync(candidate, remoteQuestionId);
            if (question is not null)
            {
                ApplyDetail(question, detail, nowUtc);
                await _questionRepository.UpdateAsync(question, autoSave: true);
            }

            await uow.CompleteAsync();
        }
    }

    /// <summary>Detayı MEVCUT satırın üzerine BİRLEŞTİREREK uygular: detay yanıtında boş gelen alan, listeden
    /// gelen değeri EZMEZ (<c>ApplyRemote</c> tüm alanları yazdığı için birleştirme burada yapılmalı).</summary>
    private void ApplyDetail(ChannelQuestion question, RemoteQuestion detail, DateTime nowUtc)
    {
        question.ApplyRemote(
            detail.RemoteProductId ?? question.RemoteProductId,
            detail.ProductTitle ?? question.ProductTitle,
            detail.Subject ?? question.Subject,
            detail.QuestionText ?? question.QuestionText,
            detail.CustomerName ?? question.CustomerName,
            detail.CustomerEmail ?? question.CustomerEmail,
            detail.QuestionDate ?? question.RemoteQuestionDate,
            ResolveStatus(detail, question.NeutralStatus),
            detail.RemoteStatus ?? question.RemoteStatus,
            detail.IsPublic ?? question.IsPublic,
            nowUtc);

        WriteImageUrls(question, detail.ImageUrls);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>TOLERANT durum eşlemesi (fail-fast YASAK — canlı keşif SINIR 4: detaydaki <c>status</c> kısıtsız
    /// metindir; tanımadığımız bir değer çekimi komple düşürmemeli, satır görünür kalmalı).
    /// <para>Sıra: cevap dolu → <c>Answered</c> · ham OPEN → <c>Pending</c> · ham CLOSED → <c>Answered</c> ·
    /// ham durum BOŞ → sorgunun ima ettiği durum · tanınmayan → <c>Unknown</c> + uyarı.</para>
    /// <para><b>Boş durum neden uyarı ÜRETMEZ:</b> liste yükünde durum alanı hiç YOKTUR (SINIR 3). Her liste
    /// satırı için uyarı basmak log'u kullanılamaz hâle getirirdi — ve bilgi kaybı da değildir: <c>status=OPEN</c>
    /// sorgusundan dönen satır tanım gereği açıktır, kapalı ay penceresinden dönen satır ise cevaplanmıştır.</para></summary>
    private ChannelQuestionStatus ResolveStatus(RemoteQuestion remote, ChannelQuestionStatus fallback)
    {
        if (!string.IsNullOrWhiteSpace(remote.ExistingAnswer))
        {
            return ChannelQuestionStatus.Answered;
        }

        var raw = remote.RemoteStatus?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        if (string.Equals(raw, "OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return ChannelQuestionStatus.Pending;
        }

        if (string.Equals(raw, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return ChannelQuestionStatus.Answered;
        }

        Logger.LogWarning("Tanınmayan kanal soru durumu: '{RemoteStatus}' → Unknown (soru {Question}).",
            raw, remote.RemoteQuestionId);
        return ChannelQuestionStatus.Unknown;
    }

    /// <summary>Ham durum boş geldiğinde sorgunun ima ettiği durum (bkz. <see cref="ResolveStatus"/>).</summary>
    private static ChannelQuestionStatus DefaultStatusFor(ChannelQuestionQuery query)
    {
        return query.OnlyOpen ? ChannelQuestionStatus.Pending : ChannelQuestionStatus.Answered;
    }

    /// <summary>Soru fotoğraflarının BAĞLANTILARINI extra-property'ye yazar (DAM'a indirme YOK — 2026-08-01
    /// Hakan kararı). Boş liste mevcut bağlantıları SİLMEZ: liste ve detay yükleri farklı zenginlikte gelir,
    /// eksik bir yükün elimizdeki bağlantıları düşürmesi bilgi KAYBI olurdu.</summary>
    private static void WriteImageUrls(ChannelQuestion question, IReadOnlyList<string> imageUrls)
    {
        var urls = (imageUrls ?? Array.Empty<string>())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            return;
        }

        question.SetProperty(
            ChannelQuestionSyncConsts.ImageUrlsPropertyName,
            string.Join(ChannelQuestionSyncConsts.ImageUrlsSeparator, urls));
    }

    /// <summary>Tüm tenant kimlikleri + host (<c>null</c>). Kendi UoW'unda okunur (host scope).</summary>
    private async Task<List<Guid?>> GetTenantIdsAsync()
    {
        List<Guid?> tenantIds;
        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            tenantIds = (await AsyncExecuter.ToListAsync(await _tenantRepository.GetQueryableAsync()))
                .Select(t => (Guid?)t.Id)
                .ToList();
            await uow.CompleteAsync();
        }

        tenantIds.Add(null);
        return tenantIds;
    }

    /// <summary>TPT alt-tipinden kanal türü. Tanınmayan tip <c>null</c> döner ve kanal ATLANIR — varsayılan bir
    /// türe düşmek, yanlış pazaryerine soru sorgusu göndermek demek olurdu.</summary>
    private static SalesChannelType? ResolveChannelType(SalesChannelBase channel)
    {
        return channel switch
        {
            SalesChannelTrN11 => SalesChannelType.TrN11,
            SalesChannelTrTrendyol => SalesChannelType.TrTrendyol,
            SalesChannelEtsy => SalesChannelType.Etsy,
            _ => null,
        };
    }

    /// <summary>Verilen anın ait olduğu ayın İLK GÜNÜ (gün semantiği — saat/timezone taşımaz).</summary>
    private static DateTime ToMonthStart(DateTime value)
    {
        return new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
    }

    /// <summary>Tur seçim sınıfları — küçük sayı önce yürür (bkz. sınıf özeti).</summary>
    private enum ChannelQuestionWorkKind
    {
        /// <summary>UI'ın "hemen çek" işareti — kullanıcı ekranın başında bekliyor.</summary>
        Priority = 0,

        /// <summary>Kanalın açık soruları HİÇ çekilmedi — kanal başına tek seferlik bootstrap.</summary>
        InitialRefresh = 1,

        /// <summary>Geçmiş seedi — ay ay geriye, tur başına bir sayfa.</summary>
        Seed = 2,

        /// <summary>Eşiği dolmuş rutin tazeleme.</summary>
        RoutineRefresh = 3,
    }

    /// <summary>Planlama fazının ürettiği İŞ ADAYI (uzak çağrı için gereken her şeyin anlık kopyası — yürütme
    /// fazı defteri yeniden okur, bu kayıt entity TAŞIMAZ: UoW'lar arasında entity taşımak detached tuzağıdır).</summary>
    private sealed record SyncCandidate(
        Guid? TenantId,
        Guid SalesChannelId,
        Guid CompanyId,
        SalesChannelType ChannelType,
        ChannelQuestionWorkKind Kind,
        DateTime OrderKey,
        int PageIndex,
        DateTime? SeedMonthStart);

    private sealed record PageApplyResult(int WrittenCount, string? FirstNewRemoteQuestionId);
}
