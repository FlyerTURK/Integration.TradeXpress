using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// NÖTR müşteri sorusu uygulaması — ORTAK GELEN KUTUSU (tüm kanallar tek grid, kanal yalnız discriminator).
/// <c>OrderAppService</c> emsalinin soru karşılığıdır: <b>company-owned + per-tenant</b>, salt-okuma çekimden
/// beslenen kayıtlar + YEREL cevap katmanı.
///
/// <para><b>Kapsam FAIL-CLOSED (OrderAppService deseni):</b> tenant sınırını ABP global filtresi uygular, ama
/// şirket filtresi <c>CurrentCompanyId</c> null iken PERMISSIVE'dir — working company olmayan bir bağlamda
/// (HTTP API'si/Swagger, arka plan işi) liste tenant'ın TÜM şirketlerinin sorularını döndürürdü. Bu yüzden
/// liste sorgusu şirket bağlamı yoksa BOŞ döner ve ayrıca <c>CompanyId</c> ile açıkça daraltılır. Tekil
/// erişimde repository <c>GetAsync</c>'i kapsam dışı id'yi zaten kayıt yokmuş gibi karşılar.</para>
///
/// <para><b>Cevap pazaryerine GİTMEZ (2026-08-01 Hakan kararı):</b> bu servis hiçbir kanal istemcisi çağırmaz —
/// <c>WriteAnswerAsync</c> yalnız yerel taslağı/kuyruğu yazar. Hiçbir satır <see cref="ChannelAnswerState.Sent"/>
/// durumuna GEÇMEZ; o geçişi yalnız (henüz var olmayan) push katmanı yapabilir.</para>
///
/// <para><b>ÇEKİM de buradan yapılmaz:</b> <see cref="RequestSyncAsync"/> pazaryerine çıkmaz, yalnız
/// <see cref="ChannelQuestionSyncManager"/> kuyruğuna işaret bırakır. Dakikada-1 kotası hesap başına ortak
/// olduğundan N11'e giden TEK merkez arka plan işçisidir (gerekçe: metodun kendi özeti).</para>
/// </summary>
[Authorize(TradeXpressPermissions.ChannelQuestions.Default)]
public class ChannelQuestionAppService : TradeXpressAppService, IChannelQuestionAppService
{
    private readonly IRepository<ChannelQuestion, Guid> _questionRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ChannelQuestionSyncManager _syncManager;

    // Gelen kutusu liste sorgusunda filtre/sıralama/aramaya İZİN VERİLEN alanlar (whitelist — ChannelQuestion
    // entity property adları). CompanyId/TenantId whitelist'te YOK: güvenlik sınırıdır, client daraltamaz/genişletemez.
    // AnswerPushError de YOK — hata metni denetim alanıdır, grid aramasında sızmasın. Id tie-breaker için dahil.
    private static readonly HashSet<string> ChannelQuestionListAllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ChannelQuestion.Id),
        nameof(ChannelQuestion.SalesChannelId),
        nameof(ChannelQuestion.ChannelType),
        nameof(ChannelQuestion.RemoteQuestionId),
        nameof(ChannelQuestion.RemoteProductId),
        nameof(ChannelQuestion.ProductId),
        nameof(ChannelQuestion.ProductTitle),
        nameof(ChannelQuestion.Subject),
        nameof(ChannelQuestion.QuestionText),
        nameof(ChannelQuestion.CustomerName),
        nameof(ChannelQuestion.CustomerEmail),
        nameof(ChannelQuestion.RemoteQuestionDate),
        nameof(ChannelQuestion.FirstSeenAt),
        nameof(ChannelQuestion.FetchedAt),
        nameof(ChannelQuestion.NeutralStatus),
        nameof(ChannelQuestion.RemoteStatus),
        nameof(ChannelQuestion.IsPublic),
        nameof(ChannelQuestion.IsRead),
        nameof(ChannelQuestion.AnswerText),
        nameof(ChannelQuestion.AnswerState),
        nameof(ChannelQuestion.AnsweredAt),
        nameof(ChannelQuestion.AnswerPushedAt),
    };

    public ChannelQuestionAppService(
        IRepository<ChannelQuestion, Guid> questionRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        ICurrentCompany currentCompany,
        ChannelQuestionSyncManager syncManager)
    {
        _questionRepository = questionRepository;
        _channelRepository = channelRepository;
        _currentCompany = currentCompany;
        _syncManager = syncManager;
    }

    // ── Ortak gelen kutusu: birleşik liste (tüm kanallar) ─────────────────────────────────────────────

    public virtual async Task<PagedResultDto<ChannelQuestionListDto>> GetListAsync(ChannelQuestionListRequestDto input)
    {
        // FAIL-CLOSED şirket kapsamı (OrderAppService deseni): global company filtresi CurrentCompanyId null iken
        // PERMISSIVE'dir — working company yokken (HTTP API'si/Swagger, arka plan) tenant'ın TÜM şirketlerinin
        // soruları dönerdi. Şirket bağlamı yoksa boş sayfa: kapsamsız okuma kazara veri sızdırmasın.
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<ChannelQuestionListDto>(0, new List<ChannelQuestionListDto>());
        }

        // Gelen kutusuna ÖZEL tipli eksenler önce (kanal/durum/okundu/bekleyen), sonra MERKEZİ whitelist'li motor
        // (kolon filtresi + global arama + sıralama) — kolon filtresi sessizce düşmez.
        var query = ApplyInboxFilters(
                (await _questionRepository.GetQueryableAsync()).Where(q => q.CompanyId == companyId), input)
            .ApplyListRequest(input, ChannelQuestionListAllowedFields);

        // Client açık sıralama vermediyse alan-özel varsayılanı uygula (ApplyListRequest'in yalnız-Id fallback'ini ezer).
        // VARSAYILAN = FirstSeenAt ARTAN, yani EN ESKİ BEKLEYEN ÜSTTE. Gerekçe SLA: pazaryeri 24 saat içinde cevap
        // bekler ve gecikme satıcı puanına işler → gelen kutusunda en riskli satır (en uzun bekleyen) ilk sırada
        // olmalı. Order'ın "yeni→eski" varsayılanı burada YANLIŞ olurdu: en acil soru listenin sonuna düşerdi.
        // Sıralama FirstSeenAt üzerinden (RemoteQuestionDate DEĞİL): N11 soru tarihi GÜN hassasiyetindedir.
        var hasExplicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
        if (!hasExplicitSort)
        {
            query = query.OrderBy(q => q.FirstSeenAt).ThenBy(q => q.Id);
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var questions = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        var dtos = questions
            .Select(question => ObjectMapper.Map<ChannelQuestion, ChannelQuestionListDto>(question))
            .ToList();
        await EnrichChannelNamesAsync(dtos);

        return new PagedResultDto<ChannelQuestionListDto>(totalCount, dtos);
    }

    public virtual async Task<ChannelQuestionListDto> GetAsync(Guid id)
    {
        var question = await GetOwnedQuestionAsync(id);
        return await BuildRowAsync(question);
    }

    // ── Yerel cevap katmanı (pazaryerine YAZMA YOK) ───────────────────────────────────────────────────

    /// <summary>Cevap taslağını yerel olarak yazar/günceller. Gönderim DEĞİLDİR — bu yüzden ayrı izin
    /// (<c>ChannelQuestions.Answer</c>) ister ama hiçbir kanal istemcisine dokunmaz.</summary>
    [Authorize(TradeXpressPermissions.ChannelQuestions.Answer)]
    public virtual async Task<ChannelQuestionListDto> WriteAnswerAsync(Guid id, ChannelQuestionAnswerInput input)
    {
        var question = await GetOwnedQuestionAsync(id);

        // Timestamp ABP IClock'tan: AbpClockOptions.Kind=Utc olduğundan Clock.Now UTC'dir (kayıt=UTC kuralı).
        // Yerel saate çevirmek UI'nın işi — burada dönüşüm YAPILMAZ.
        question.WriteAnswer(input.AnswerText, input.ReadyToSend, Clock.Now);
        await _questionRepository.UpdateAsync(question);

        return await BuildRowAsync(question);
    }

    public virtual async Task<ChannelQuestionListDto> SetReadAsync(Guid id, bool isRead)
    {
        var question = await GetOwnedQuestionAsync(id);
        question.SetRead(isRead);
        await _questionRepository.UpdateAsync(question);

        return await BuildRowAsync(question);
    }

    // ── Çekim tetikleyicisi (pazaryerine ÇIKMAZ — yalnız kuyruk işareti) ──────────────────────────────

    /// <summary>Çalışılan şirketin AKTİF kanalları için "sıradaki turda öncelikli çek" işareti bırakır ve anında
    /// döner. Bu metot HİÇBİR kanal istemcisi çağırmaz: N11 ürün sorularını hesap başına dakikada bir kez
    /// listelemeye izin verir, kotayı paralellik aşmaz ve kotayı harcayan tek merkez arka plan işçisidir.
    /// <para>Bu yüzden dönüş <c>Task</c>'tır — çekilen satır sayısı diye bir sonuç YOKTUR (bkz. arayüz özeti).</para></summary>
    [Authorize(TradeXpressPermissions.ChannelQuestions.Sync)]
    public virtual async Task RequestSyncAsync()
    {
        // FAIL-CLOSED şirket kapsamı (GetListAsync ile AYNI gerekçe): şirket bağlamı yoksa işaret bırakılacak
        // kanal kümesi de tanımlı değildir — kapsamsız bir tetik tenant'ın TÜM şirketlerinin kanallarını
        // kuyruğa sokar ve dakikada-1 penceresini yabancı şirketlerin kanallarına harcardı.
        if (_currentCompany.Id is not { } companyId)
        {
            return;
        }

        // Yalnız AKTİF kanallar: pasife alınmış kanalı öncelik kuyruğuna sokmak, sırada bekleyen ve gerçekten
        // izlenen kanalların turunu geciktirir (kota dakikada 1 — her tur pahalıdır).
        var channelIds = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(channel => channel.CompanyId == companyId && channel.IsActive)
                .Select(channel => channel.Id));

        foreach (var channelId in channelIds)
        {
            // Kanal türünün soru desteği OLUP OLMADIĞI burada SORULMAZ: hangi kanalın istemcisi var bilgisi
            // senkron yöneticisinin sorumluluğudur (Tell-Don't-Ask) — desteklenmeyen kanalın işareti orada düşer.
            await _syncManager.RequestPriorityAsync(channelId);
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Gelen kutusuna özel tipli filtreler — merkezi whitelist motorundan AYRI tutulur çünkü bunlar
    /// kolon filtresi değil, ekranın kendi görünüm anahtarlarıdır (menüden gelen "bekleyenler" gibi).</summary>
    private static IQueryable<ChannelQuestion> ApplyInboxFilters(
        IQueryable<ChannelQuestion> query, ChannelQuestionListRequestDto input)
    {
        if (input.SalesChannelId is { } salesChannelId)
        {
            query = query.Where(q => q.SalesChannelId == salesChannelId);
        }

        if (input.NeutralStatus is { } neutralStatus)
        {
            query = query.Where(q => q.NeutralStatus == neutralStatus);
        }

        if (input.AnswerState is { } answerState)
        {
            query = query.Where(q => q.AnswerState == answerState);
        }

        if (input.IsRead is { } isRead)
        {
            query = query.Where(q => q.IsRead == isRead);
        }

        if (input.OnlyPending)
        {
            // "Cevap bekleyen" = kanalda hâlâ AÇIK ve cevabı pazaryerine GİTMEMİŞ. AnswerState != Sent (== None
            // DEĞİL): taslak/kuyruktaki/başarısız satırlar hâlâ iş bekler, listeden düşerlerse operatör onları
            // bir daha görmez. Bugün zaten hiçbir satır Sent olmadığından bu koşul push açılınca anlam kazanır.
            query = query.Where(q =>
                q.NeutralStatus == ChannelQuestionStatus.Pending && q.AnswerState != ChannelAnswerState.Sent);
        }

        return query;
    }

    /// <summary>Sayfadaki satırların kanal ADINI TEK sorguda çözer (id-only referanstan; mapper doldurmaz).
    /// Satır başına sorgu (N+1) YOK.</summary>
    private async Task EnrichChannelNamesAsync(List<ChannelQuestionListDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var channelIds = dtos.Select(d => d.SalesChannelId).Distinct().ToList();
        var names = (await AsyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => channelIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name })))
            .ToDictionary(x => x.Id, x => x.Name);

        foreach (var dto in dtos)
        {
            dto.SalesChannelName = names.TryGetValue(dto.SalesChannelId, out var name) ? name : null;
        }
    }

    /// <summary>Tek satırı DTO'ya çevirip kanal adını çözer (tekil yol — liste yolu batch enrich kullanır).</summary>
    private async Task<ChannelQuestionListDto> BuildRowAsync(ChannelQuestion question)
    {
        var dto = ObjectMapper.Map<ChannelQuestion, ChannelQuestionListDto>(question);
        dto.SalesChannelName = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.Id == question.SalesChannelId)
                .Select(c => c.Name));

        return dto;
    }

    /// <summary>Sahiplik + varlık doğrulaması. Tenant/şirket sınırını global query filter uyguladığı için
    /// kapsam dışı id burada kayıt YOKMUŞ gibi davranır (ABP <c>EntityNotFoundException</c>) — sızıntı yok.</summary>
    private async Task<ChannelQuestion> GetOwnedQuestionAsync(Guid id)
    {
        return await _questionRepository.GetAsync(id);
    }
}
