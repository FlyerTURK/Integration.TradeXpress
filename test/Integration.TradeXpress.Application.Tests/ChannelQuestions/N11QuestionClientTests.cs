using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// <see cref="N11QuestionClient"/> birim testleri (istek kurma + yanıt ayrıştırma) — ağ/DI YOK, örnek SOAP XML
/// üzerinden. Örnekler WSDL yapısından üretilmiştir: canlı keşif anında (2026-08-01) hesapta HİÇ soru yoktu, yani
/// gerçek soru gövdesi görülmedi. Bu testler o boşluğun yerini tutar — ilk gerçek soru geldiğinde örnek XML canlı
/// gövdeyle DEĞİŞTİRİLMELİ, silinmemelidir.
///
/// <para><b>Kilitlenen davranışlar:</b> liste/detay asimetrisi (listede müşteri/tarih/durum YOK · detayda id YOK) ·
/// kota duvarının İSTİSNA OLMAMASI · gerçek hatanın dostane istisnaya çevrilmesi · sayfalama alanları ·
/// görsel bağlantılarının çıkarılması · soru tarihinin GÜN hassasiyetini timezone kaydırmadan koruması ·
/// isteğin 0-tabanlı sayfa ve <c>dd/MM/yyyy</c> tarihiyle kurulması.</para>
///
/// <para><b>Neden istek kurma da test ediliyor:</b> N11 bu ucu dakikada 1 çağrıya kısar — biçimi yanlış kurulmuş
/// her istek yalnız hata değil, kaybedilmiş bir TUR demektir. Bu sınıfın işi, o hataları ağa çıkmadan yakalamaktır.</para>
/// </summary>
public class N11QuestionClientTests
{
    // Gerçek N11 GetProductQuestionListResponse yapısı (WSDL ProductQuestion: id/productId/productTitle/
    // questionSubject/question/answer/images). result bloğu canlı doğrulandı; pagingData 0-tabanlı currentPage.
    private const string ListXml = """
        <GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas">
          <result><status>success</status></result>
          <productQuestions>
            <productQuestion>
              <id>9001</id>
              <productId>556677</productId>
              <productTitle>Altın Kolye 14 Ayar</productTitle>
              <questionSubject>Ürün Özellikleri</questionSubject>
              <question>Bu ürün gerçekten 14 ayar mı?</question>
              <answer></answer>
              <images>
                <image>https://n11scdn.akamaized.net/a1/soru/1.jpg</image>
                <image>https://n11scdn.akamaized.net/a1/soru/2.jpg</image>
              </images>
            </productQuestion>
            <productQuestion>
              <id>9002</id>
              <productId>556688</productId>
              <productTitle>Gümüş Yüzük</productTitle>
              <questionSubject>Kargo</questionSubject>
              <question>Bugün kargoya verilir mi?</question>
              <answer>Evet, saat 16:00'a kadar verilen siparişler aynı gün çıkar.</answer>
            </productQuestion>
          </productQuestions>
          <pagingData>
            <currentPage>0</currentPage>
            <pageSize>100</pageSize>
            <totalCount>137</totalCount>
            <pageCount>2</pageCount>
          </pagingData>
        </GetProductQuestionListResponse>
        """;

    // WSDL ProductQuestionDetail: liste alanları + fullName/email/productStatus/status/questionDate/answeredDate/
    // sellerExpose/buyerExpose/images — ama <id> YOK (eşleme istekten taşınır).
    private const string DetailXml = """
        <GetProductQuestionDetailResponse xmlns="http://www.n11.com/ws/schemas">
          <result><status>success</status></result>
          <productQuestion>
            <productId>556677</productId>
            <productTitle>Altın Kolye 14 Ayar</productTitle>
            <questionSubject>Ürün Özellikleri</questionSubject>
            <question>Bu ürün gerçekten 14 ayar mı?</question>
            <answer></answer>
            <fullName>Ayşe Yılmaz</fullName>
            <email>ayse@example.com</email>
            <productStatus>Active</productStatus>
            <status>Satıcı Cevabı Bekleniyor</status>
            <questionDate>28/07/2026</questionDate>
            <answeredDate></answeredDate>
            <sellerExpose>true</sellerExpose>
            <buyerExpose>true</buyerExpose>
            <images>
              <image>https://n11scdn.akamaized.net/a1/soru/1.jpg</image>
            </images>
          </productQuestion>
        </GetProductQuestionDetailResponse>
        """;

    // ── 1) Liste ayrıştırma + sayfalama ────────────────────────────────────────────────────────────

    [Fact]
    public void Parses_list_items_and_paging_counters()
    {
        var page = N11QuestionClient.ParseListResponse(XDocument.Parse(ListXml));

        page.RateLimited.ShouldBeFalse();
        page.TotalCount.ShouldBe(137);
        page.PageCount.ShouldBe(2);          // kuyruk kör döngü kurmaz: kaç tur kaldığını buradan bilir
        page.Items.Count.ShouldBe(2);

        var first = page.Items[0];
        first.RemoteQuestionId.ShouldBe("9001");
        first.RemoteProductId.ShouldBe("556677");
        first.ProductTitle.ShouldBe("Altın Kolye 14 Ayar");
        first.Subject.ShouldBe("Ürün Özellikleri");
        first.QuestionText.ShouldBe("Bu ürün gerçekten 14 ayar mı?");
        first.ExistingAnswer.ShouldBeNull();  // boş <answer> "cevap yok" demektir, boş metin değil
    }

    [Fact]
    public void List_row_leaves_detail_only_fields_unknown()
    {
        // BU TESTİN VARLIK SEBEBİ: N11 liste öğesinde müşteri/tarih/durum YOKTUR. Bu alanlar boş STRING ya da
        // varsayılan tarihle doldurulsaydı, senkron katmanı detaydan gelen gerçek değerleri "güncelleme" diye
        // EZERDİ — müşteri adı silinir, SLA çapraz kontrolü bozulurdu. null = "bilgi yok".
        var first = N11QuestionClient.ParseListResponse(XDocument.Parse(ListXml)).Items[0];

        first.CustomerName.ShouldBeNull();
        first.CustomerEmail.ShouldBeNull();
        first.QuestionDate.ShouldBeNull();
        first.RemoteStatus.ShouldBeNull();
        first.IsPublic.ShouldBeNull();
    }

    [Fact]
    public void Existing_answer_is_visible_without_a_detail_call()
    {
        // Kota altında en değerli bilgi: "bu soru cevaplanmış mı" — answer LİSTEDE de geldiği için detay
        // çağrısı (bir dakikalık kota) harcamadan bilinir.
        var second = N11QuestionClient.ParseListResponse(XDocument.Parse(ListXml)).Items[1];

        second.RemoteQuestionId.ShouldBe("9002");
        second.ExistingAnswer.ShouldBe("Evet, saat 16:00'a kadar verilen siparişler aynı gün çıkar.");
    }

    [Fact]
    public void Extracts_customer_image_links()
    {
        // Müşteri soruya fotoğraf ekleyebiliyor ("bu taş çatlak mı?"). Karar (2026-08-01): yalnız BAĞLANTI
        // saklanır, DAM'a indirilmez → istemcinin işi url listesini kayıpsız çıkarmaktır.
        var items = N11QuestionClient.ParseListResponse(XDocument.Parse(ListXml)).Items;

        items[0].ImageUrls.ShouldBe(new[]
        {
            "https://n11scdn.akamaized.net/a1/soru/1.jpg",
            "https://n11scdn.akamaized.net/a1/soru/2.jpg",
        });
        items[1].ImageUrls.ShouldBeEmpty();   // images bloğu yoksa boş liste (null değil)
    }

    [Fact]
    public void Empty_question_list_yields_no_items()
    {
        var xml = """
            <GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas">
              <result><status>success</status></result>
              <productQuestions/>
              <pagingData><currentPage>0</currentPage><pageSize>100</pageSize><totalCount>0</totalCount><pageCount>0</pageCount></pagingData>
            </GetProductQuestionListResponse>
            """;

        var page = N11QuestionClient.ParseListResponse(XDocument.Parse(xml));

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
        page.RateLimited.ShouldBeFalse();     // "kayıt yok" ile "kota doldu" AYRI hâller
    }

    [Fact]
    public void Row_without_an_id_is_dropped_instead_of_breaking_the_page()
    {
        // id idempotency anahtarının yarısıdır (SalesChannelId + RemoteQuestionId); id'siz satır ne saklanabilir
        // ne tazelenebilir. Kaydedilmeye çalışılsaydı aggregate reddeder ve TÜM sayfa düşerdi.
        var xml = """
            <GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas">
              <result><status>success</status></result>
              <productQuestions>
                <productQuestion><productId>1</productId><question>Kimliksiz</question></productQuestion>
                <productQuestion><id>9003</id><question>Kimlikli</question></productQuestion>
              </productQuestions>
            </GetProductQuestionListResponse>
            """;

        var page = N11QuestionClient.ParseListResponse(XDocument.Parse(xml));

        page.Items.Select(i => i.RemoteQuestionId).ShouldBe(new[] { "9003" });
    }

    // ── 2) Kota duvarı: İSTİSNA DEĞİL ──────────────────────────────────────────────────────────────

    [Fact]
    public void Access_limit_is_a_rate_limited_page_not_an_exception()
    {
        // BU TESTİN VARLIK SEBEBİ: N11 ürün sorularını dakikada BİR kez listeletir; kota duvarı bu sistemin
        // normal işleyişidir, arıza değil. İstisnaya çevrilseydi worker turu "başarısız" sayılır, log gürültüsü
        // üretir ve kuyruğun bir sonraki tura erteleme kararı kaybolurdu.
        var xml = """
            <GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas">
              <result>
                <status>failure</status>
                <errorCode>SELLER_API.getProductQuestionListRequest.accessLimit.reached</errorCode>
                <errorMessage>Ürün soruları 1 dakikada bir kez listelenebilmektedir.</errorMessage>
              </result>
            </GetProductQuestionListResponse>
            """;

        var page = N11QuestionClient.ParseListResponse(XDocument.Parse(xml));

        page.RateLimited.ShouldBeTrue();
        page.Items.ShouldBeEmpty();
        // Sayaçlar 0'dır ve "kanalda 0 kayıt var" ANLAMINA GELMEZ — kuyruk RateLimited bayrağına bakmalıdır.
        page.TotalCount.ShouldBe(0);
        page.PageCount.ShouldBe(0);
    }

    [Fact]
    public void Access_limit_on_the_detail_endpoint_yields_no_record_instead_of_an_exception()
    {
        var xml = """
            <GetProductQuestionDetailResponse xmlns="http://www.n11.com/ws/schemas">
              <result>
                <status>failure</status>
                <errorCode>SELLER_API.getProductQuestionDetailRequest.accessLimit.reached</errorCode>
                <errorMessage>Ürün soruları 1 dakikada bir kez listelenebilmektedir.</errorMessage>
              </result>
            </GetProductQuestionDetailResponse>
            """;

        N11QuestionClient.ParseDetailResponse(XDocument.Parse(xml), "9001").ShouldBeNull();
    }

    // ── 3) Gerçek hatalar dostane istisnaya çevrilir ───────────────────────────────────────────────

    [Fact]
    public void Genuine_failure_becomes_a_friendly_business_exception()
    {
        // Kota DIŞI her hata (kimlik, zorunlu alan, aralık aşımı) gerçek bir arızadır: sessizce boş sayfa
        // dönmek "kanalda soru yok" yanılsaması üretir ve kayıp fark edilmez.
        var xml = """
            <GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas">
              <result>
                <status>failure</status>
                <errorCode>SELLER_API.nullParam</errorCode>
                <errorMessage>startDate alanı boş olamaz</errorMessage>
              </result>
            </GetProductQuestionListResponse>
            """;

        var ex = Should.Throw<BusinessException>(() => N11QuestionClient.ParseListResponse(XDocument.Parse(xml)));

        ex.Code.ShouldBe("TradeXpress:N11:Question:ListRejected");
        ex.Data["message"].ShouldBe("startDate alanı boş olamaz");
    }

    [Fact]
    public void Missing_result_block_is_treated_as_failure_not_as_an_empty_page()
    {
        // Fail-closed: N11 bu serviste her yanıta result ekler. Yokluğu beklenmedik bir gövdedir; "boş liste"
        // saymak veri kaybını GİZLERDİ.
        var xml = """<GetProductQuestionListResponse xmlns="http://www.n11.com/ws/schemas"><productQuestions/></GetProductQuestionListResponse>""";

        Should.Throw<BusinessException>(() => N11QuestionClient.ParseListResponse(XDocument.Parse(xml)))
            .Code.ShouldBe("TradeXpress:N11:Question:ListRejected");
    }

    // ── 4) Detay: kimlik istekten taşınır ──────────────────────────────────────────────────────────

    [Fact]
    public void Detail_carries_the_question_id_from_the_request()
    {
        // BU TESTİN VARLIK SEBEBİ: WSDL ProductQuestionDetail'de <id> YOKTUR. Yanıt tek başına hangi soruya ait
        // olduğunu söylemez — eşleme kimliği İSTEKTEN taşınmazsa idempotent upsert kurulamaz ve her tazeleme
        // yeni satır üretirdi.
        var detail = N11QuestionClient.ParseDetailResponse(XDocument.Parse(DetailXml), "9001");

        detail.ShouldNotBeNull();
        detail!.RemoteQuestionId.ShouldBe("9001");
        detail.CustomerName.ShouldBe("Ayşe Yılmaz");
        detail.CustomerEmail.ShouldBe("ayse@example.com");
        detail.ProductTitle.ShouldBe("Altın Kolye 14 Ayar");
        detail.ImageUrls.ShouldBe(new[] { "https://n11scdn.akamaized.net/a1/soru/1.jpg" });
    }

    [Fact]
    public void Detail_carries_the_raw_status_text_untouched()
    {
        // Arama filtresi yalnız OPEN/CLOSED kabul ederken detaydaki status KISITSIZ metindir. İstemci burada
        // eşleme YAPMAZ ve tanımadığı metinde fail-fast ETMEZ: tek bir bilinmeyen durum yüzünden tüm çekim
        // düşerdi. Nötr eşleme (tanınmayan → Unknown + log) senkron katmanının işidir.
        var detail = N11QuestionClient.ParseDetailResponse(XDocument.Parse(DetailXml), "9001");

        detail!.RemoteStatus.ShouldBe("Satıcı Cevabı Bekleniyor");
    }

    [Fact]
    public void Question_date_keeps_day_precision_without_a_timezone_shift()
    {
        // questionDate WSDL'de xs:date (saat YOK). Sipariş istemcisinin createDate'i gibi GMT+3'ten UTC'ye
        // çekilseydi tarih BİR GÜN geriye kayardı. Alan zaten yalnız çapraz kontrol içindir; SLA geri sayımı
        // ChannelQuestion.FirstSeenAt üzerinden akar.
        var detail = N11QuestionClient.ParseDetailResponse(XDocument.Parse(DetailXml), "9001");

        detail!.QuestionDate.ShouldBe(new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Unrecognized_expose_flag_stays_unknown()
    {
        // sellerExpose/buyerExpose N11 tarafında BELGELENMEMİŞTİR ve canlı görülmedi. Doğrulanmamış bir
        // değerden "herkese açık" etiketi üretmek müşteri mahremiyeti açısından risklidir → üç durumlu.
        var xml = DetailXml.Replace("<buyerExpose>true</buyerExpose>", "<buyerExpose>EVET</buyerExpose>");

        N11QuestionClient.ParseDetailResponse(XDocument.Parse(xml), "9001")!.IsPublic.ShouldBeNull();
        N11QuestionClient.ParseDetailResponse(XDocument.Parse(DetailXml), "9001")!.IsPublic.ShouldBe(true);
    }

    [Fact]
    public void Detail_without_a_record_yields_null()
    {
        var xml = """
            <GetProductQuestionDetailResponse xmlns="http://www.n11.com/ws/schemas">
              <result><status>success</status></result>
            </GetProductQuestionDetailResponse>
            """;

        N11QuestionClient.ParseDetailResponse(XDocument.Parse(xml), "9001").ShouldBeNull();
    }

    // ── 5) İstek kurma: kotayı boşa harcayacak biçim hataları burada yakalanır ─────────────────────

    [Fact]
    public void Open_query_asks_for_open_questions_without_any_date_filter()
    {
        // status=OPEN tarihsiz ÇALIŞIR (canlı doğrulandı). Boş bir <startDate> eklemek filtreyi bozardı.
        var request = N11QuestionClient.BuildListRequest("app-key", "app-secret", OpenQuery(pageIndex: 0));

        // Kanıtlanmış N11 deseni: prefix'li wrapper (şema namespace'inde) + UNQUALIFIED çocuklar.
        request.Name.LocalName.ShouldBe("GetProductQuestionListRequest");
        request.Name.NamespaceName.ShouldBe("http://www.n11.com/ws/schemas");
        request.Elements().ShouldAllBe(e => e.Name.Namespace == XNamespace.None);

        Value(request, "status").ShouldBe("OPEN");
        Find(request, "startDate").ShouldBeNull();
        Find(request, "endDate").ShouldBeNull();
        Value(request, "appKey").ShouldBe("app-key");
        Value(request, "appSecret").ShouldBe("app-secret");
    }

    [Fact]
    public void Paging_is_sent_zero_based()
    {
        // currentPage 0-TABANLI (canlı doğrulandı). 1-tabanlı gönderilseydi ilk sayfa sessizce ATLANIR,
        // en eski sorular hiç çekilmezdi — ve her yanlış deneme bir dakikalık kotaya mal olurdu.
        var request = N11QuestionClient.BuildListRequest("k", "s", OpenQuery(pageIndex: 0));

        Value(request, "currentPage").ShouldBe("0");
        Value(request, "pageSize").ShouldBe("100");
        Value(N11QuestionClient.BuildListRequest("k", "s", OpenQuery(pageIndex: 3)), "currentPage").ShouldBe("3");
    }

    [Fact]
    public void Closed_query_sends_the_date_window_in_n11_format()
    {
        // Tarih biçimi dd/MM/yyyy (canlı doğrulandı: yanlış aralık hatası döndü, format hatası DEĞİL).
        // ISO ya da MM/dd/yyyy gönderilseydi 07/08 gibi tarihler SESSİZCE başka bir ayı tarardı.
        var query = new ChannelQuestionQuery(
            OnlyOpen: false,
            StartDate: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            PageIndex: 0,
            PageSize: 100);

        var request = N11QuestionClient.BuildListRequest("k", "s", query);

        Value(request, "status").ShouldBe("CLOSED");
        Value(request, "startDate").ShouldBe("01/07/2026");
        Value(request, "endDate").ShouldBe("01/08/2026");
    }

    [Fact]
    public void Closed_query_without_a_start_date_fails_before_touching_the_channel()
    {
        // N11 bu isteği SELLER_API.nullParam ile reddeder — ama reddedilen çağrı da dakikalık kotayı YER.
        // Kendi hatamızı yerelde yakalamak, worker'ın o turunu kurtarır.
        var query = new ChannelQuestionQuery(
            OnlyOpen: false, StartDate: null, EndDate: null, PageIndex: 0, PageSize: 100);

        Should.Throw<BusinessException>(() => N11QuestionClient.BuildListRequest("k", "s", query))
            .Code.ShouldBe("TradeXpress:N11:Question:StartDateRequired");
    }

    // ── 6) Test sahtesi kendi sözleşmesine uyuyor mu ───────────────────────────────────────────────

    [Fact]
    public async Task Fake_client_scripts_pages_simulates_the_quota_wall_and_counts_every_call()
    {
        var fake = new FakeN11QuestionClient();
        var channelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var query = new ChannelQuestionQuery(OnlyOpen: true, StartDate: null, EndDate: null, PageIndex: 0, PageSize: 100);
        fake.ScriptPage(BuildQuestion("9001"));

        var page = await fake.FetchPageAsync(channelId, query, default);
        page.Items.Single().RemoteQuestionId.ShouldBe("9001");
        page.RateLimited.ShouldBeFalse();

        // Script tükenince BOŞ sayfa döner — "kayıt yok", kota hatası DEĞİL.
        (await fake.FetchPageAsync(channelId, query, default)).Items.ShouldBeEmpty();

        fake.SimulateRateLimit = true;
        (await fake.FetchPageAsync(channelId, query, default)).RateLimited.ShouldBeTrue();
        (await fake.FetchDetailAsync(channelId, "9001", default)).ShouldBeNull();

        // Kota TÜM hesap için ortak: liste ve detay AYNI havuzu yer → ikisi de sayılır.
        fake.TotalCallCount.ShouldBe(4);
        fake.ListRequests.Count.ShouldBe(3);
        fake.DetailRequests.ShouldBe(new[] { "9001" });
        fake.ChannelType.ShouldBe(SalesChannelType.TrN11);
    }

    private static ChannelQuestionQuery OpenQuery(int pageIndex)
    {
        return new ChannelQuestionQuery(
            OnlyOpen: true, StartDate: null, EndDate: null, PageIndex: pageIndex, PageSize: 100);
    }

    private static XElement? Find(XElement root, string localName)
    {
        return root.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
    }

    private static string? Value(XElement root, string localName)
    {
        return Find(root, localName)?.Value;
    }

    private static RemoteQuestion BuildQuestion(string remoteQuestionId)
    {
        return new RemoteQuestion(
            RemoteQuestionId: remoteQuestionId,
            RemoteProductId: "556677",
            ProductTitle: "Altın Kolye 14 Ayar",
            Subject: "Ürün Özellikleri",
            QuestionText: "Bu ürün gerçekten 14 ayar mı?",
            CustomerName: null,
            CustomerEmail: null,
            QuestionDate: null,
            RemoteStatus: null,
            IsPublic: null,
            ExistingAnswer: null,
            ImageUrls: Array.Empty<string>());
    }
}
