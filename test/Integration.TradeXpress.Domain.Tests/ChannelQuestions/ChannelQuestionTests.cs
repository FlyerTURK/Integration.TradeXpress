using System;
using Integration.Framework;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// <see cref="ChannelQuestion"/> davranış testleri (saf birim — DB/DI YOK).
///
/// <para>Kilitlenen değişmezler: sahiplik/kanal/uzak-kimlik fail-fast · SLA çapası <c>FirstSeenAt</c> SET-ONCE ·
/// cevap yaşam döngüsü (temizle → taslak → sıraya al → gönderildi/başarısız) · gönderilmiş cevabın KİLİTLİ olması ·
/// uzak metnin kırpılması (reddedilmemesi) · sentinel <see cref="Guid.Empty"/> ile eşleşme temizleme.</para>
///
/// <para>Ctor'da id/tenantId bulunmaması ve <c>ToString</c> override'ı <c>EntityConventionTests</c> tarafından
/// zaten mekanik olarak denetleniyor — burada TEKRAR edilmez.</para>
/// </summary>
public class ChannelQuestionTests
{
    private static readonly Guid CompanyId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid ChannelId = SimpleGuidGenerator.Instance.Create();

    private static readonly DateTime FirstFetch = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondFetch = new(2026, 8, 2, 17, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime AnsweredAt = new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PushedAt = new(2026, 8, 2, 18, 5, 0, DateTimeKind.Utc);

    // ── 1) Kuruluş: zorunlu alanlar + başlangıç durumu ──────────────────────────────────────────────

    [Fact]
    public void Question_requires_an_owner_company()
    {
        // Sahipsiz soru = şirket sınırı olmayan satır; ICompanyOwned güvenlik sınırı kuruluşta kapanmalı
        // (CompanyOwnershipGuard fail-closed davranışının entity tarafındaki karşılığı).
        Should.Throw<RequiredPropertyException>(() =>
            new ChannelQuestion(Guid.Empty, ChannelId, SalesChannelType.TrN11, "Q-1"));
    }

    [Fact]
    public void Question_requires_a_sales_channel()
    {
        // Kanal yalnız discriminator değil, idempotency anahtarının YARISIDIR — boş kanal iki farklı
        // pazaryerinin aynı numaralı sorusunu çakıştırırdı.
        Should.Throw<RequiredPropertyException>(() =>
            new ChannelQuestion(CompanyId, Guid.Empty, SalesChannelType.TrN11, "Q-1"));
    }

    [Fact]
    public void Question_requires_a_remote_question_id()
    {
        // Uzak kimlik idempotency anahtarının diğer yarısı: boşsa ikinci çekim GÜNCELLEME yerine
        // dublike üretir. Whitespace de boş sayılır (Clip önce Trim'ler).
        Should.Throw<RequiredPropertyException>(() =>
            new ChannelQuestion(CompanyId, ChannelId, SalesChannelType.TrN11, null!));
        Should.Throw<RequiredPropertyException>(() =>
            new ChannelQuestion(CompanyId, ChannelId, SalesChannelType.TrN11, string.Empty));
        Should.Throw<RequiredPropertyException>(() =>
            new ChannelQuestion(CompanyId, ChannelId, SalesChannelType.TrN11, "   "));
    }

    [Fact]
    public void New_question_starts_unknown_and_unanswered()
    {
        // Başlangıç durumu açıkça kurulur: durum eşlemesi HENÜZ yapılmadığı için Unknown, cevap yolu ise
        // hiç başlamadığı için None. (Enum varsayılanı sıfır olsa da niyet testle sabitlenir.)
        var question = BuildQuestion();

        question.CompanyId.ShouldBe(CompanyId);
        question.SalesChannelId.ShouldBe(ChannelId);
        question.ChannelType.ShouldBe(SalesChannelType.TrN11);
        question.RemoteQuestionId.ShouldBe("Q-1");
        question.NeutralStatus.ShouldBe(ChannelQuestionStatus.Unknown);
        question.AnswerState.ShouldBe(ChannelAnswerState.None);
        question.AnswerText.ShouldBeNull();
        question.AnsweredAt.ShouldBeNull();
        question.AnswerPushedAt.ShouldBeNull();
        question.IsRead.ShouldBeFalse();
    }

    // ── 2) SLA çapası: FirstSeenAt SET-ONCE ────────────────────────────────────────────────────────

    [Fact]
    public void Second_fetch_keeps_the_first_seen_stamp()
    {
        // BU TESTİN VARLIK SEBEBİ: pazaryeri 24 saatlik cevap süresini satıcı puanına işler ve geri sayım
        // FirstSeenAt üzerinden hesaplanır. Tazeleme çekimi bu timestamp'i yenileseydi SLA sayacı her senkronda
        // sıfırlanır, gecikmiş soru "yeni gelmiş" gibi görünür, ceza fark edilmeden birikirdi.
        var question = BuildQuestion();

        ApplyRemote(question, FirstFetch);
        ApplyRemote(question, SecondFetch, questionText: "Metin sonradan düzeltildi");

        question.FirstSeenAt.ShouldBe(FirstFetch);   // İLK değerde kalır
        question.FetchedAt.ShouldBe(SecondFetch);    // tazelik göstergesi ilerler
        question.QuestionText.ShouldBe("Metin sonradan düzeltildi");
    }

    // ── 3) Cevap yazma: temizleme / taslak / gönderim sırası ────────────────────────────────────────

    [Fact]
    public void Blank_answer_clears_the_draft()
    {
        // Temizleme yolu: kullanıcı yazdığı taslağı silebilmeli. Boş metin "boş cevap" olarak SAKLANMAZ —
        // aksi hâlde satır gönderim sırasında boş cevapla kalır ve bekleyen sayacını kirletirdi.
        var question = BuildQuestion();
        question.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);

        question.WriteAnswer("   ", readyToSend: true, AnsweredAt);

        question.AnswerText.ShouldBeNull();
        question.AnswerState.ShouldBe(ChannelAnswerState.None);
        question.AnsweredAt.ShouldBeNull();
    }

    [Fact]
    public void Answer_stays_draft_until_marked_ready()
    {
        // Taslak ≠ gönderilecek: kullanıcı üzerinde çalışırken satır gönderim sırasına GİRMEZ.
        var question = BuildQuestion();

        question.WriteAnswer("Yarın kargoya verilecek.", readyToSend: false, AnsweredAt);

        question.AnswerText.ShouldBe("Yarın kargoya verilecek.");
        question.AnswerState.ShouldBe(ChannelAnswerState.Draft);
        question.AnsweredAt.ShouldBe(AnsweredAt);
        question.AnswerPushedAt.ShouldBeNull();   // yazma ≠ gönderme (push kapalı)
    }

    [Fact]
    public void Answer_marked_ready_enters_the_send_queue()
    {
        // ReadyToSend push açıldığında drenajın çekeceği durumdur; yazma anı yine de AnsweredAt'e yazılır
        // (gönderim anı AYRI alandır ve push kapalıyken boş kalır).
        var question = BuildQuestion();

        question.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);

        question.AnswerState.ShouldBe(ChannelAnswerState.ReadyToSend);
        question.AnsweredAt.ShouldBe(AnsweredAt);
        question.AnswerPushedAt.ShouldBeNull();
    }

    // ── 4) Gönderilmiş cevap KİLİTLİ ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sent_answer_cannot_be_rewritten()
    {
        // Pazaryerinde cevap DÜZENLEME operasyonu yok (N11 WSDL'inde Update/DeleteProductAnswer tanımlı değil).
        // Yerelde düzenlemeye izin vermek kullanıcıya gerçekte olmayan bir yetenek vaat eder: metin bizde
        // değişir, müşterinin gördüğü cevap eski kalırdı.
        var question = BuildQuestion();
        question.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);
        question.MarkAnswerSent(PushedAt);

        Should.Throw<BusinessException>(() =>
                question.WriteAnswer("Pardon, 18 ayar.", readyToSend: true, AnsweredAt))
            .Code.ShouldBe("TradeXpress:ChannelQuestion:AnswerAlreadySent");

        question.AnswerText.ShouldBe("Evet, 14 ayar.");   // gönderilen metin olduğu gibi durur
    }

    // ── 5) Teslim timestamp'leri: Sent / Failed ────────────────────────────────────────────────────

    [Fact]
    public void MarkAnswerSent_requires_an_answer()
    {
        // Cevapsız satırı "gönderildi" işaretlemek en zararlı hâldir: kullanıcı müşteriye cevap gittiğini
        // sanar, SLA sayacı susar, gerçekte hiçbir şey gönderilmemiştir → fail-fast.
        var question = BuildQuestion();

        Should.Throw<BusinessException>(() => question.MarkAnswerSent(PushedAt))
            .Code.ShouldBe("TradeXpress:ChannelQuestion:AnswerRequired");

        question.AnswerState.ShouldBe(ChannelAnswerState.None);
        question.AnswerPushedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkAnswerSent_stamps_the_delivery()
    {
        // "Gönderildi" yalnız GERÇEK gönderim anıyla birlikte anlamlıdır — durum + timestamp aynı anda kurulur.
        var question = BuildQuestion();
        question.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);

        question.MarkAnswerSent(PushedAt);

        question.AnswerState.ShouldBe(ChannelAnswerState.Sent);
        question.AnswerPushedAt.ShouldBe(PushedAt);
        question.AnswerPushError.ShouldBeNull();
    }

    [Fact]
    public void MarkAnswerFailed_keeps_the_error_for_retry()
    {
        // Başarısız gönderim sessizce yutulmaz: hata teşhis için saklanır, satır yeniden denenebilir kalır
        // (Sent DEĞİL → kilitlenmez, cevap düzenlenebilir).
        var question = BuildQuestion();
        question.WriteAnswer("Evet, 14 ayar.", readyToSend: true, AnsweredAt);

        question.MarkAnswerFailed("N11 servisi 500 döndü");

        question.AnswerState.ShouldBe(ChannelAnswerState.Failed);
        question.AnswerPushError.ShouldBe("N11 servisi 500 döndü");
        question.AnswerPushedAt.ShouldBeNull();
    }

    // ── 6) Uzak metin: kırpma (reddetme DEĞİL) + boş → null ─────────────────────────────────────────

    [Fact]
    public void Overlong_remote_text_is_clipped_not_rejected()
    {
        // Uzak veri bizim kontrolümüzde değil: pazaryeri sınırını büyütürse çekim PATLAMAMALI, satır görünür
        // kalmalı. Bu yüzden taşan metin exception yerine KIRPILIR (Order.Clip ile aynı gerekçe).
        var question = BuildQuestion();
        var overlong = new string('s', ChannelQuestionConsts.QuestionTextMaxLength + 250);

        ApplyRemote(question, FirstFetch, questionText: overlong);

        question.QuestionText!.Length.ShouldBe(ChannelQuestionConsts.QuestionTextMaxLength);
        question.FirstSeenAt.ShouldBe(FirstFetch);   // satır kaydedilebilir hâlde kaldı
    }

    [Fact]
    public void Blank_remote_fields_become_null()
    {
        // Boş/whitespace uzak alanlar "" olarak saklanmaz — filtre ve görüntü tarafında "değeri var ama boş"
        // ile "değeri yok" ayrımı tek temsile (null) indirgenir.
        var question = BuildQuestion();

        ApplyRemote(
            question,
            FirstFetch,
            questionText: "   ",
            subject: string.Empty,
            productTitle: null,
            remoteProductId: "  P-1  ",
            remoteStatus: "   ");

        question.QuestionText.ShouldBeNull();
        question.Subject.ShouldBeNull();
        question.ProductTitle.ShouldBeNull();
        question.RemoteStatus.ShouldBeNull();
        question.RemoteProductId.ShouldBe("P-1");   // dolu alanlar trim'lenerek korunur
    }

    // ── 7) Yerel ürün eşleşmesi ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetProduct_with_empty_guid_clears_the_match()
    {
        // Guid.Empty sentinel'i "eşleşme yok" demektir; ham hâliyle saklansaydı var olmayan bir ürüne
        // referans veren satır oluşur, sorgular boş-Guid'i geçerli id sanardı.
        var question = BuildQuestion();
        question.SetProduct(SimpleGuidGenerator.Instance.Create());

        question.SetProduct(Guid.Empty);

        question.ProductId.ShouldBeNull();
    }

    // ── Kurulum yardımcıları ───────────────────────────────────────────────────────────────────────

    private static ChannelQuestion BuildQuestion(string remoteQuestionId = "Q-1")
    {
        return new ChannelQuestion(CompanyId, ChannelId, SalesChannelType.TrN11, remoteQuestionId);
    }

    /// <summary>Çekim çağrısını okunur kılan yardımcı metot — testler yalnız ilgilendikleri alanı geçer.</summary>
    private static void ApplyRemote(
        ChannelQuestion question,
        DateTime fetchedAt,
        string? questionText = "Bu ürün 14 ayar mı?",
        string? subject = "Ayar sorusu",
        string? productTitle = "Altın Kolye",
        string? remoteProductId = "P-1",
        string? customerName = "Ayşe Yılmaz",
        string? customerEmail = "ayse@example.com",
        string? remoteStatus = "OPEN",
        ChannelQuestionStatus neutralStatus = ChannelQuestionStatus.Pending)
    {
        question.ApplyRemote(
            remoteProductId,
            productTitle,
            subject,
            questionText,
            customerName,
            customerEmail,
            remoteQuestionDate: null,
            neutralStatus,
            remoteStatus,
            isPublic: null,
            fetchedAt);
    }
}
