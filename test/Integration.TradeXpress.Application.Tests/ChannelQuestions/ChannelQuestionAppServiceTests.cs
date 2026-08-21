using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// <see cref="IChannelQuestionAppService"/> uçtan uca (DB'li) davranış ağı — gelen kutusunun SÖZLEŞMESİ.
///
/// <para>Kilitlenenler: SLA sıralaması (en eski bekleyen üstte) · "yalnız cevap bekleyenler" süzgeci ·
/// kanal adının SUNUCUDA çözülmesi · cevap yazma yaşam döngüsü (taslak → gönderim sırası → temizleme) ·
/// okundu bayrağı · ŞİRKET İZOLASYONU.</para>
///
/// <para><b>Bu dilimde push YOK</b> (2026-08-01 kararı): cevap yerelde yazılır, pazaryerine GİTMEZ. Testler bu
/// yüzden her cevap adımında <c>AnswerPushedAt</c>'in boş kaldığını da doğrular — ekranda "Gönderildi" ibaresi
/// yalnız <see cref="ChannelAnswerState.Sent"/> satırında görünebilir ve bugün hiçbir satır oraya AppService
/// üzerinden geçemez.</para>
///
/// <para><b>Şirket izolasyonu neden TENANT kapsamında kurulur:</b> <c>ICompanyOwned</c> global filtresi host
/// kaydını (<c>TenantId = null</c>) company kısıtından MUAF tutar. Testler varsayılan olarak host bağlamında
/// koştuğu için izolasyon senaryosu tenant AÇMADAN yazılsaydı yeşil görünür ama hiçbir şey kanıtlamazdı —
/// yabancı satır zaten muafiyetten sızardı (emsal: <c>CompanyOwnedFilterTests</c>).</para>
/// </summary>
public abstract class ChannelQuestionAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly DateTime OldestSeenAt = new(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MiddleSeenAt = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewestSeenAt = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AnsweredAt = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PushedAt = new(2026, 8, 2, 10, 5, 0, DateTimeKind.Utc);

    private readonly IChannelQuestionAppService _appService;
    private readonly IRepository<ChannelQuestion, Guid> _questionRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentTenant _currentTenant;

    protected ChannelQuestionAppServiceTests()
    {
        _appService = GetRequiredService<IChannelQuestionAppService>();
        _questionRepository = GetRequiredService<IRepository<ChannelQuestion, Guid>>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    // ── 1) Varsayılan sıra: en eski bekleyen ÜSTTE ─────────────────────────────────────────────────

    [Fact]
    public async Task GetList_puts_the_oldest_question_on_top()
    {
        // BU TESTİN VARLIK SEBEBİ: pazaryeri cevap süresini satıcı puanına işler ve geri sayım FirstSeenAt
        // üzerinden akar. Varsayılan sıra "en yeni üstte" olsaydı (ABP/grid alışkanlığı) SÜRESİ DOLMAK ÜZERE
        // olan soru listenin dibine düşer, kullanıcı en acil işi en son görürdü. Ekleme sırası KASITEN karışık.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "SLA");
            await SeedQuestionAsync(companyId, channel.Id, "Q-MID", MiddleSeenAt);
            await SeedQuestionAsync(companyId, channel.Id, "Q-NEW", NewestSeenAt);
            await SeedQuestionAsync(companyId, channel.Id, "Q-OLD", OldestSeenAt);

            var list = await _appService.GetListAsync(new ChannelQuestionListRequestDto { MaxResultCount = 50 });

            list.TotalCount.ShouldBe(3);
            list.Items.Select(i => i.RemoteQuestionId).ShouldBe(new[] { "Q-OLD", "Q-MID", "Q-NEW" });
        }
    }

    // ── 2) "Yalnız cevap bekleyenler" süzgeci ──────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyPending_keeps_just_the_rows_that_still_need_an_answer()
    {
        // Gelen kutusunun asıl iş görünümü budur: kapanmış/cevaplanmış soru bekleyen kuyruğunu şişirirse
        // kullanıcı gerçekten acil olanı seçemez. İki eksen AYRI: sorunun KANAL durumu (NeutralStatus) ile
        // cevabın TESLİM durumu (AnswerState) — bekleyen = Pending VE henüz gönderilmemiş.
        // Taslak yazılmış satır HÂLÂ bekliyordur (müşteriye bir şey gitmedi) → listede KALMALI.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "PEND");
            await SeedQuestionAsync(companyId, channel.Id, "Q-PENDING", OldestSeenAt);
            await SeedQuestionAsync(
                companyId, channel.Id, "Q-DRAFT", MiddleSeenAt,
                finalize: q => q.WriteAnswer("Yarın kargoya verilecek.", readyToSend: false, AnsweredAt));

            // Cevabı GÖNDERİLMİŞ satır: bugün AppService bu duruma geçiremez (push kapalı), bu yüzden durum
            // seed'de ELDE kurulur — süzgecin push açıldığı gün de doğru davranacağı ŞİMDİDEN kilitlensin.
            await SeedQuestionAsync(
                companyId, channel.Id, "Q-SENT", MiddleSeenAt,
                finalize: q =>
                {
                    q.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);
                    q.MarkAnswerSent(PushedAt);
                });

            await SeedQuestionAsync(
                companyId, channel.Id, "Q-ANSWERED", NewestSeenAt, ChannelQuestionStatus.Answered);

            var pending = await _appService.GetListAsync(
                new ChannelQuestionListRequestDto { OnlyPending = true, MaxResultCount = 50 });

            var pendingIds = pending.Items.Select(i => i.RemoteQuestionId).ToList();
            pendingIds.ShouldContain("Q-PENDING");
            pendingIds.ShouldContain("Q-DRAFT");     // taslak ≠ gönderilmiş → hâlâ bekliyor
            pendingIds.ShouldNotContain("Q-SENT");   // cevap gitti → kuyruktan düşer
            pendingIds.ShouldNotContain("Q-ANSWERED");
            pending.TotalCount.ShouldBe(2);

            // Süzgeç OPT-IN: kapalıyken hiçbir satır gizlenmez (arşiv/denetim görünümü).
            var all = await _appService.GetListAsync(
                new ChannelQuestionListRequestDto { OnlyPending = false, MaxResultCount = 50 });
            all.TotalCount.ShouldBe(4);
        }
    }

    // ── 3) Kanal adı SUNUCUDA çözülür ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetList_resolves_the_channel_name_of_every_row()
    {
        // ChannelQuestion kanala id-only bağlıdır (aggregate'ler arası nav YOK) → ad grid'de ancak sunucu
        // çözerse görünür. Client tarafında satır başına kanal çekmek N+1 demektir; bu yüzden çözüm TOPLU
        // yapılır ve testte İKİ kanal kurulur: toplu sözlük kurulurken satırların adları KARIŞMAMALI
        // (tek kanallı kurulumda yanlış eşleme fark edilmezdi).
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var first = await SeedChannelAsync(companyId, "NAME1");
            var second = await SeedChannelAsync(companyId, "NAME2");
            await SeedQuestionAsync(companyId, first.Id, "Q-A1", OldestSeenAt);
            await SeedQuestionAsync(companyId, first.Id, "Q-A2", MiddleSeenAt);
            await SeedQuestionAsync(companyId, second.Id, "Q-B1", NewestSeenAt);

            var list = await _appService.GetListAsync(new ChannelQuestionListRequestDto { MaxResultCount = 50 });

            list.Items.ShouldAllBe(i => i.SalesChannelName != null);
            list.Items.Single(i => i.RemoteQuestionId == "Q-A1").SalesChannelName.ShouldBe(first.Name);
            list.Items.Single(i => i.RemoteQuestionId == "Q-A2").SalesChannelName.ShouldBe(first.Name);
            list.Items.Single(i => i.RemoteQuestionId == "Q-B1").SalesChannelName.ShouldBe(second.Name);
            list.Items.ShouldAllBe(i => i.ChannelType == SalesChannelType.TrN11);
        }
    }

    // ── 4) Cevap yazma: taslak → gönderim sırası (ama GÖNDERİM YOK) ────────────────────────────────

    [Fact]
    public async Task WriteAnswer_saves_a_draft_then_queues_it_without_sending()
    {
        // Kullanıcı cevabını iki adımda yazar: üzerinde çalışırken TASLAK (kuyruğa girmez), bitince
        // gönderime HAZIR. Kritik olan üçüncü durumun OLMAMASI: push kapalı olduğu için hiçbir adım
        // AnswerPushedAt'i doldurmaz. Doldursaydı ekran "Gönderildi" derdi, müşteriye ise hiçbir şey
        // gitmemiş olurdu — SLA sessizce tükenirdi (bu ailenin en pahalı hatası).
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "ANS");
            var questionId = await SeedQuestionAsync(companyId, channel.Id, "Q-ANS", OldestSeenAt);

            var draft = await _appService.WriteAnswerAsync(
                questionId,
                new ChannelQuestionAnswerInput { AnswerText = "Yarın kargoya verilecek.", ReadyToSend = false });

            draft.AnswerText.ShouldBe("Yarın kargoya verilecek.");
            draft.AnswerState.ShouldBe(ChannelAnswerState.Draft);
            draft.AnsweredAt.ShouldNotBeNull();      // YAZMA anı AnsweredAt'e yazılır
            draft.AnswerPushedAt.ShouldBeNull();     // GÖNDERME anı boş kalır

            var queued = await _appService.WriteAnswerAsync(
                questionId,
                new ChannelQuestionAnswerInput { AnswerText = "Yarın kargoya verilecek.", ReadyToSend = true });

            queued.AnswerState.ShouldBe(ChannelAnswerState.ReadyToSend);
            queued.AnswerPushedAt.ShouldBeNull();

            // Dönen DTO ekrandaki tek gerçek kaynak: kalıcı hâl ile birebir olmalı.
            var reloaded = await _appService.GetAsync(questionId);
            reloaded.AnswerText.ShouldBe("Yarın kargoya verilecek.");
            reloaded.AnswerState.ShouldBe(ChannelAnswerState.ReadyToSend);
            reloaded.AnsweredAt.ShouldBe(queued.AnsweredAt);
        }
    }

    [Fact]
    public async Task WriteAnswer_with_empty_text_clears_the_answer()
    {
        // Temizleme yolu: kullanıcı yazdığı taslağı silebilmeli. Boş metin "boş cevap" olarak saklanırsa satır
        // gönderim sırasında boş cevapla bekler ve bekleyen sayacını kirletir → durum None'a DÖNER.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "CLR");
            var questionId = await SeedQuestionAsync(companyId, channel.Id, "Q-CLR", OldestSeenAt);
            await _appService.WriteAnswerAsync(
                questionId,
                new ChannelQuestionAnswerInput { AnswerText = "Silinecek taslak.", ReadyToSend = true });

            var cleared = await _appService.WriteAnswerAsync(
                questionId,
                new ChannelQuestionAnswerInput { AnswerText = string.Empty, ReadyToSend = true });

            cleared.AnswerText.ShouldBeNull();
            cleared.AnswerState.ShouldBe(ChannelAnswerState.None);
            cleared.AnsweredAt.ShouldBeNull();
        }
    }

    // ── 5) Okundu bayrağı ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetRead_toggles_the_inbox_flag_both_ways()
    {
        // Okunmamış sayacı gelen kutusunun tek uyarı göstergesi; işaret TEK YÖNLÜ olsaydı yanlışlıkla okundu
        // yapılan soru bir daha dikkat çekmezdi → geri alınabilir olmalı.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "READ");
            var questionId = await SeedQuestionAsync(companyId, channel.Id, "Q-READ", OldestSeenAt);

            (await _appService.SetReadAsync(questionId, true)).IsRead.ShouldBeTrue();
            (await _appService.GetAsync(questionId)).IsRead.ShouldBeTrue();   // kalıcı

            (await _appService.SetReadAsync(questionId, false)).IsRead.ShouldBeFalse();
            (await _appService.GetAsync(questionId)).IsRead.ShouldBeFalse();
        }
    }

    // ── 6) Şirket izolasyonu (güvenlik sınırı) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Another_companys_question_never_appears_in_the_list()
    {
        // ChannelQuestion ICompanyOwned'dır: müşteri sorusu ad + e-posta + sipariş bağlamı taşır; yabancı
        // şirketin satırını göstermek KVKK sızıntısıdır, "yanlış grid" değil. Filtre YAPISAL (global query
        // filter) olmalı — unutulan bir Where'de sızmasın. Tenant kapsamı ZORUNLU: host satırı (TenantId=null)
        // company kısıtından muaftır, tenant açılmasaydı bu test hiçbir şey kanıtlamazdı (bkz. sınıf özeti).
        var tenantId = NewId();
        var mineCompanyId = NewId();
        var foreignCompanyId = NewId();

        using (_currentTenant.Change(tenantId))
        {
            Guid mineQuestionId;
            using (_currentCompany.Change(mineCompanyId))
            {
                var channel = await SeedChannelAsync(mineCompanyId, "MINE");
                mineQuestionId = await SeedQuestionAsync(mineCompanyId, channel.Id, "Q-MINE", OldestSeenAt);
            }

            Guid foreignQuestionId;
            using (_currentCompany.Change(foreignCompanyId))
            {
                var channel = await SeedChannelAsync(foreignCompanyId, "OTHER");
                foreignQuestionId = await SeedQuestionAsync(
                    foreignCompanyId, channel.Id, "Q-FOREIGN", MiddleSeenAt);
            }

            using (_currentCompany.Change(mineCompanyId))
            {
                var list = await _appService.GetListAsync(new ChannelQuestionListRequestDto { MaxResultCount = 50 });

                var ids = list.Items.Select(i => i.Id).ToList();
                ids.ShouldContain(mineQuestionId);
                ids.ShouldNotContain(foreignQuestionId);
                list.TotalCount.ShouldBe(1);
            }
        }
    }

    // ── Kurulum yardımcıları ───────────────────────────────────────────────────────────────────────

    private async Task<SalesChannelTrN11> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{suffix}", $"N11 Kanal {suffix}", "app-key", "app-secret"),
                autoSave: true));
    }

    /// <summary>Çekimden gelmiş gibi tam bir soru satırı kurar (uzak alanlar + <c>FirstSeenAt</c> timestamp'i).
    /// <paramref name="finalize"/> yalnız AppService'in AÇMADIĞI durumları (gönderilmiş cevap gibi) elde
    /// kurmak içindir — normal akış her zaman servis üzerinden test edilir.</summary>
    private async Task<Guid> SeedQuestionAsync(
        Guid companyId,
        Guid salesChannelId,
        string remoteQuestionId,
        DateTime firstSeenAt,
        ChannelQuestionStatus neutralStatus = ChannelQuestionStatus.Pending,
        Action<ChannelQuestion>? finalize = null)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var question = BuildQuestion(companyId, salesChannelId, remoteQuestionId, firstSeenAt, neutralStatus);
            finalize?.Invoke(question);
            var inserted = await _questionRepository.InsertAsync(question, autoSave: true);
            return inserted.Id;
        });
    }

    private static ChannelQuestion BuildQuestion(
        Guid companyId,
        Guid salesChannelId,
        string remoteQuestionId,
        DateTime firstSeenAt,
        ChannelQuestionStatus neutralStatus)
    {
        var question = new ChannelQuestion(companyId, salesChannelId, SalesChannelType.TrN11, remoteQuestionId);
        question.ApplyRemote(
            remoteProductId: $"P-{remoteQuestionId}",
            productTitle: "Altın Kolye",
            subject: "Ayar sorusu",
            questionText: "Bu ürün 14 ayar mı?",
            customerName: "Ayşe Yılmaz",
            customerEmail: "ayse@example.com",
            remoteQuestionDate: null,
            neutralStatus: neutralStatus,
            remoteStatus: neutralStatus == ChannelQuestionStatus.Pending ? "OPEN" : "CLOSED",
            isPublic: null,
            fetchedAt: firstSeenAt);
        return question;
    }

    private static Guid NewId()
    {
        return SimpleGuidGenerator.Instance.Create();
    }
}
