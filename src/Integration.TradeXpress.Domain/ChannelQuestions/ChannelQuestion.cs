using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// NÖTR müşteri sorusu aggregate'i — TÜM satış kanallarının ürün soruları buraya map olur (kanal yalnız
/// discriminator: <see cref="SalesChannelId"/> + <see cref="ChannelType"/>; kanal başına ayrı tablo YOKTUR).
/// <c>Order</c> emsalinin soru karşılığıdır.
///
/// <para><b>Çekim SALT-OKUMA, cevap ŞİMDİLİK GÖNDERİLMEZ (2026-08-01 Hakan kararı):</b> sorular pazaryerinden
/// çekilir, cevap TradeXpress içinde yazılır ama kanala GİTMEZ — <see cref="AnswerState"/> teslim durumunu
/// taşır ve push açılana kadar hiçbir satır <see cref="ChannelAnswerState.Sent"/> olmaz.</para>
///
/// <para><b>İdempotency anahtarı</b> (<see cref="SalesChannelId"/>, <see cref="RemoteQuestionId"/>): ikinci
/// çekim durumu/metni GÜNCELLER, dublike üretmez. Alanlar SNAPSHOT'tır (<c>Order</c> felsefesi): yerel ürün
/// silinse bile soru neyi sorduğunu bilir.</para>
///
/// <para><b>SLA saati BİZDEN:</b> pazaryeri 24 saat içinde cevap bekler ve gecikme satıcı puanına işler, ama
/// N11 <c>questionDate</c>'i GÜN hassasiyetindedir (WSDL <c>xs:date</c> — saat yok). Geri sayım bu yüzden
/// <see cref="FirstSeenAt"/> (bizim UTC damgamız) üzerinden hesaplanır; <see cref="RemoteQuestionDate"/> yalnız
/// çapraz kontrol için saklanır.</para>
///
/// <para><b>Müşteri adı ve iletişim bilgisi SAKLANIR ve GÖSTERİLİR (2026-08-01 Hakan kararı):</b> müşteri
/// soruyu kendi isteğiyle satıcıya yazıyor ve verisinin işlenmesi için rıza metnini pazaryerinde onaylıyor.
/// Pazaryerinin bu alanları MASKELEMEDEN göndermesi, satıcının görmesinde sakınca olmadığının kendi
/// beyanıdır. Gerekirse tenant'lara ayrıca bir onay metni gösterilir.</para>
///
/// <para><b>Pazaryerinin gerçek kısıtı BAŞKA:</b> müşteriyi kanal dışına davet etmek yasaktır — cevapta kendi
/// web sitemize, başka bir pazaryerine ya da harici iletişim kanalına yönlendirme yapılamaz. Bu kısıt
/// <see cref="AnswerText"/> İÇERİĞİNİ ilgilendirir (kişisel veriyi değil); cevap yazma yüzeyi kullanıcıyı
/// bu konuda uyarmalıdır.</para>
/// </summary>
public class ChannelQuestion : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ChannelQuestion()
    {
    }

    public ChannelQuestion(
        Guid companyId,
        Guid salesChannelId,
        SalesChannelType channelType,
        string remoteQuestionId)
    {
        SetCompanyId(companyId);
        SetSalesChannel(salesChannelId, channelType);
        RemoteQuestionId = ClipRequired(
            remoteQuestionId, nameof(RemoteQuestionId), ChannelQuestionConsts.RemoteQuestionIdMaxLength);
        NeutralStatus = ChannelQuestionStatus.Unknown;
        AnswerState = ChannelAnswerState.None;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sorunun geldiği satış kanalı — id-only referans (aggregate'ler arası nav YOK). Set-once.</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + filtre. Set-once.</summary>
    public virtual SalesChannelType ChannelType { get; protected set; }

    /// <summary>Kanaldaki soru kimliği — idempotency anahtarı (SalesChannelId ile birlikte tekil). Değişmez.</summary>
    public virtual string RemoteQuestionId { get; protected set; } = null!;

    /// <summary>Kanaldaki ürün kimliği (snapshot) — yerel eşleşme kurulamasa da soru hangi ürüne ait bilinir.</summary>
    public virtual string? RemoteProductId { get; protected set; }

    /// <summary>Eşleşen YEREL ürün — id-only, opsiyonel: eşleşme kurulamayabilir ya da ürün sonradan silinebilir.
    /// Soru satırı her iki durumda da sağ kalır (snapshot alanları sayesinde).</summary>
    public virtual Guid? ProductId { get; protected set; }

    /// <summary>Ürün başlığı snapshot'ı (kanaldan).</summary>
    public virtual string? ProductTitle { get; protected set; }

    /// <summary>Soru başlığı (kanal alanı).</summary>
    public virtual string? Subject { get; protected set; }

    /// <summary>Soru gövdesi.</summary>
    public virtual string? QuestionText { get; protected set; }

    /// <summary>Soruyu soran müşterinin adı (kanaldan geldiği gibi) — cevabı kişiselleştirmek ve aynı müşteriyi
    /// tanımak için gösterilir.</summary>
    public virtual string? CustomerName { get; protected set; }

    /// <summary>Müşterinin iletişim adresi (kanaldan geldiği gibi). Cevap kanal üzerinden gider; bu alan
    /// kimlik eşleştirme ve gerektiğinde iletişim içindir (bkz. tip özeti).</summary>
    public virtual string? CustomerEmail { get; protected set; }

    /// <summary>Kanalın bildirdiği soru tarihi. N11'de GÜN hassasiyetindedir (saat yok) → SLA için kullanılamaz,
    /// yalnız çapraz kontrol/görüntü.</summary>
    public virtual DateTime? RemoteQuestionDate { get; protected set; }

    /// <summary>Soruyu İLK gördüğümüz an (UTC) — SLA geri sayımının TEK güvenilir kaynağı (bkz. tip özeti).
    /// İlk çekimde damgalanır, sonraki çekimler DEĞİŞTİRMEZ.</summary>
    public virtual DateTime FirstSeenAt { get; protected set; }

    /// <summary>Bu kaydın en son çekildiği an (UTC) — tazelik göstergesi.</summary>
    public virtual DateTime FetchedAt { get; protected set; }

    /// <summary>Nötr (kanal-agnostik) durum — ortak filtre/görüntü.</summary>
    public virtual ChannelQuestionStatus NeutralStatus { get; protected set; }

    /// <summary>Ham kanal durumu — nötr eşlemenin kaynağı, denetim için saklanır.</summary>
    public virtual string? RemoteStatus { get; protected set; }

    /// <summary>Soru/cevap pazaryerinde herkese açık mı. <c>null</c> = BİLİNMİYOR — N11'in bu alanları
    /// (sellerExpose/buyerExpose) belgelenmemiştir; doğrulanmadan "herkese açık" etiketi göstermek müşteri
    /// mahremiyeti açısından risklidir, bu yüzden üç durumlu.</summary>
    public virtual bool? IsPublic { get; protected set; }

    /// <summary>Kullanıcı bu soruyu gördü mü — gelen kutusu okunmamış sayacı.</summary>
    public virtual bool IsRead { get; protected set; }

    /// <summary>YEREL cevap gövdesi (taslak ya da gönderilmiş).</summary>
    public virtual string? AnswerText { get; protected set; }

    /// <summary>Cevabın TESLİM durumu — sorunun kanal durumundan bağımsız (bkz. <see cref="ChannelAnswerState"/>).</summary>
    public virtual ChannelAnswerState AnswerState { get; protected set; }

    /// <summary>Cevabın yerelde yazıldığı an (UTC). Gönderim anı DEĞİLDİR.</summary>
    public virtual DateTime? AnsweredAt { get; protected set; }

    /// <summary>Cevabın pazaryerine GERÇEKTEN gönderildiği an (UTC). Push açılana kadar daima <c>null</c>.</summary>
    public virtual DateTime? AnswerPushedAt { get; protected set; }

    /// <summary>Son gönderim hatasının özeti — <see cref="ChannelAnswerState.Failed"/> teşhisi için.</summary>
    public virtual string? AnswerPushError { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Uzak kaynaktan gelen alanları uygular (ilk çekim + sonraki tazelemeler ORTAK yol).</summary>
    public virtual void ApplyRemote(
        string? remoteProductId,
        string? productTitle,
        string? subject,
        string? questionText,
        string? customerName,
        string? customerEmail,
        DateTime? remoteQuestionDate,
        ChannelQuestionStatus neutralStatus,
        string? remoteStatus,
        bool? isPublic,
        DateTime fetchedAt)
    {
        RemoteProductId = Clip(remoteProductId, ChannelQuestionConsts.RemoteProductIdMaxLength);
        ProductTitle = Clip(productTitle, ChannelQuestionConsts.ProductTitleMaxLength);
        Subject = Clip(subject, ChannelQuestionConsts.SubjectMaxLength);
        QuestionText = Clip(questionText, ChannelQuestionConsts.QuestionTextMaxLength);
        CustomerName = Clip(customerName, ChannelQuestionConsts.CustomerNameMaxLength);
        CustomerEmail = Clip(customerEmail, ChannelQuestionConsts.CustomerEmailMaxLength);
        RemoteQuestionDate = remoteQuestionDate;
        NeutralStatus = neutralStatus;
        RemoteStatus = Clip(remoteStatus, ChannelQuestionConsts.RemoteStatusMaxLength);
        IsPublic = isPublic;
        FetchedAt = fetchedAt;

        // İlk görülme damgası SET-ONCE: sonraki çekimler SLA geri sayımını sıfırlamamalı.
        if (FirstSeenAt == default)
        {
            FirstSeenAt = fetchedAt;
        }
    }

    /// <summary>Yerel ürün eşleşmesini kurar/temizler (eşleştirici sonradan da çalışabilir).</summary>
    public virtual void SetProduct(Guid? productId)
    {
        ProductId = productId == Guid.Empty ? null : productId;
    }

    public virtual void SetRead(bool value)
    {
        IsRead = value;
    }

    /// <summary>Cevap taslağını yazar/günceller. <paramref name="readyToSend"/> true ise satır gönderim
    /// sırasına alınır (push açıldığında drenaj bunları çeker) — ama BU METOT HİÇBİR ŞEY GÖNDERMEZ.
    /// Gönderilmiş cevap yeniden yazılamaz: pazaryerinde cevap düzenleme operasyonu YOKTUR (N11 WSDL'inde
    /// UpdateProductAnswer/DeleteProductAnswer tanımlı değil), dolayısıyla yerelde değiştirmek kullanıcıya
    /// gerçekte olmayan bir yetenek vaat ederdi.</summary>
    public virtual void WriteAnswer(string? answerText, bool readyToSend, DateTime answeredAt)
    {
        if (AnswerState == ChannelAnswerState.Sent)
        {
            throw new BusinessException("TradeXpress:ChannelQuestion:AnswerAlreadySent");
        }

        var text = Clip(answerText, ChannelQuestionConsts.AnswerTextMaxLength);
        if (string.IsNullOrEmpty(text))
        {
            AnswerText = null;
            AnswerState = ChannelAnswerState.None;
            AnsweredAt = null;
            return;
        }

        AnswerText = text;
        AnswerState = readyToSend ? ChannelAnswerState.ReadyToSend : ChannelAnswerState.Draft;
        AnsweredAt = answeredAt;
        AnswerPushError = null;
    }

    /// <summary>Gönderim BAŞARILI — yalnız push katmanı çağırır (bugün çağıran YOK; push ayrı onayla açılacak).</summary>
    public virtual void MarkAnswerSent(DateTime pushedAt)
    {
        if (string.IsNullOrEmpty(AnswerText))
        {
            throw new BusinessException("TradeXpress:ChannelQuestion:AnswerRequired");
        }

        AnswerState = ChannelAnswerState.Sent;
        AnswerPushedAt = pushedAt;
        AnswerPushError = null;
    }

    /// <summary>Gönderim BAŞARISIZ — hata saklanır, satır yeniden denenebilir durumda kalır.</summary>
    public virtual void MarkAnswerFailed(string? error)
    {
        AnswerState = ChannelAnswerState.Failed;
        AnswerPushError = Clip(error, ChannelQuestionConsts.AnswerPushErrorMaxLength);
    }

    public override string ToString()
    {
        return RemoteQuestionId;
    }

    private void SetCompanyId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = value;
    }

    private void SetSalesChannel(Guid salesChannelId, SalesChannelType channelType)
    {
        if (salesChannelId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelId));
        }

        SalesChannelId = salesChannelId;
        ChannelType = channelType;
    }

    /// <summary>Uzak metin snapshot'ı: boş → null; taşan uzunluk KIRPILIR (Order.Clip ile aynı gerekçe —
    /// uzak veri bizim kontrolümüzde değil, kaydı kaybetmek kırpmaktan kötüdür).</summary>
    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    private static string ClipRequired(string? value, string propertyName, int maxLength)
    {
        var clipped = Clip(value, maxLength);
        if (clipped is null)
        {
            throw new RequiredPropertyException(propertyName);
        }

        return clipped;
    }

    #endregion
}
