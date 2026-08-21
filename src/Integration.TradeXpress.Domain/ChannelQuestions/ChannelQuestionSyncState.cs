using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Kanal başına SORU SENKRON İLERLEMESİ — çekimin "nerede kaldım" defteri. Kanal başına TEK satır
/// (<see cref="SalesChannelId"/> tekil).
///
/// <para><b>Neden ayrı entity (2026-08-01 kararı, alternatifler elenerek):</b>
/// (a) <c>SalesChannelTrN11</c>'de <c>LastSyncedAt</c> benzeri bir alan YOK — kanal entity'si pazaryeri
/// KİMLİK/AYAR taşır, makine ilerlemesi değil; oraya kolon eklemek her tur kanal satırını (ve
/// <c>ConcurrencyStamp</c>'ini) yazmak demekti → kullanıcı kanalı düzenlerken worker'la çakışırdı.
/// (b) <c>ChannelQuestion</c> satırlarından türetmek İMKÂNSIZ: hiç sorusu olmayan (ya da o ayı boş olan) bir
/// kanalın nerede kaldığı veriden okunamaz — boş ay da ilerlemedir.
/// (c) ABP <c>SettingManager</c> elenmiştir: ayar TANIMLARI statiktir, kanal-başına dinamik ad üretilemez.</para>
///
/// <para><b>Neden KALICI:</b> geçmiş seedi dakikada bir adım ilerler ve 60 aya kadar sürebilir. Bellekte tutulsa
/// her uygulama yeniden başlatmasında seed BAŞA dönerdi — kota tek havuz olduğu için bu, tazelemeyi de süresiz
/// aç bırakırdı.</para>
///
/// <para><b>İki bağımsız ilerleme ekseni:</b> ARTIMLI tazeleme (kanalda hâlâ AÇIK sorular; tarihsiz sorgu) ve
/// GEÇMİŞ seedi (kapalı sorular; ay ay geriye). Aynı kotayı paylaşırlar ama birbirlerinin imlecini bozmazlar.</para>
///
/// <para><b>Soft-delete YOK</b> (<c>AuditedAggregateRoot</c>): bu kullanıcı verisi değil makine defteridir;
/// silinen bir satırın "IsDeleted" hâliyle uzak anahtarı işgal etmesi (ve kanalın bir daha asla senkron
/// olamaması) gerçek risktir.</para>
/// </summary>
public class ChannelQuestionSyncState : AuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ChannelQuestionSyncState()
    {
    }

    public ChannelQuestionSyncState(
        Guid companyId,
        Guid salesChannelId,
        SalesChannelType channelType)
    {
        SetCompanyId(companyId);
        SetSalesChannel(salesChannelId, channelType);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Defterin ait olduğu satış kanalı — id-only referans (aggregate'ler arası nav YOK). Set-once.</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Kanal türü (discriminator) — log/teşhis + ileride kanal-özel eşik ayarı için. Set-once.</summary>
    public virtual SalesChannelType ChannelType { get; protected set; }

    /// <summary>AÇIK soruların en son BAŞARIYLA tazelendiği an (UTC). <c>null</c> = hiç tazelenmedi.
    /// Rutin tazeleme adaylığı buradan hesaplanır (<see cref="IsRefreshDue"/>).</summary>
    public virtual DateTime? LastRefreshedAt { get; protected set; }

    /// <summary>Artımlı tazelemede YARIM KALAN sayfa imleci (0 = sayfalama yok/bitti). Açık soru sayısı 100'ü
    /// aşarsa tazeleme birden çok tura yayılır; bu imleç turlar arasında yerini korur.</summary>
    public virtual int RefreshPageIndex { get; protected set; }

    /// <summary>Geçmiş seedi bitti mi (boş-ay kuralı ya da güvenlik tavanı). Bittiyse kanal artık yalnız
    /// rutin tazeleme kotası tüketir.</summary>
    public virtual bool SeedCompleted { get; protected set; }

    /// <summary>Seed'in ŞU AN üzerinde çalıştığı ayın ilk günü (iş tarihi — gün semantiği, saat/timezone YOK).
    /// <c>null</c> = seed hiç başlamadı.</summary>
    public virtual DateTime? SeedMonthStart { get; protected set; }

    /// <summary>Geçerli ay içindeki sayfa imleci (0-tabanlı — N11 <c>currentPage</c> ile hizalı).</summary>
    public virtual int SeedPageIndex { get; protected set; }

    /// <summary>Seed'in bugüne kadar BİTİRDİĞİ ay sayısı — güvenlik tavanının sayacı.</summary>
    public virtual int SeedMonthsProcessed { get; protected set; }

    /// <summary>ÜST ÜSTE boş çıkan ay sayısı — dolu bir ay görülünce SIFIRLANIR (bkz.
    /// <see cref="ChannelQuestionSyncConsts.EmptyMonthsBeforeStop"/>).</summary>
    public virtual int ConsecutiveEmptyMonths { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Rutin tazeleme adayı mı: yarım kalmış sayfalama varsa DAİMA (kalan sayfalar bitmeli), hiç
    /// tazelenmediyse evet, aksi hâlde eşik dolduysa. Eşik worker periyodundan BAĞIMSIZDIR (bkz.
    /// <see cref="ChannelQuestionSyncConsts.RoutineRefreshMinutes"/>).</summary>
    public virtual bool IsRefreshDue(DateTime nowUtc)
    {
        if (RefreshPageIndex > 0)
        {
            return true;
        }

        if (LastRefreshedAt is not { } lastRefreshedAt)
        {
            return true;
        }

        return nowUtc - lastRefreshedAt >= TimeSpan.FromMinutes(ChannelQuestionSyncConsts.RoutineRefreshMinutes);
    }

    /// <summary>Seed'in üzerinde çalışacağı ayı döndürür; hiç başlamadıysa <paramref name="fallbackMonthStart"/>
    /// ile başlatır (çağıran içinde bulunulan ayı verir → seed BUGÜNDEN geriye yürür).</summary>
    public virtual DateTime EnsureSeedMonth(DateTime fallbackMonthStart)
    {
        if (SeedMonthStart is not { } seedMonthStart)
        {
            SeedMonthStart = fallbackMonthStart;
            SeedPageIndex = 0;
            return fallbackMonthStart;
        }

        return seedMonthStart;
    }

    /// <summary>Geçerli ayda BİR sonraki sayfaya geçer (ay henüz bitmedi).</summary>
    public virtual void AdvanceSeedPage()
    {
        SeedPageIndex++;
    }

    /// <summary>Bir ayı KAPATIR ve bir öncekine geçer; durma kuralları burada uygulanır (Tell-Don't-Ask: politika
    /// entity'de, çağıranda değil).
    /// <para><paramref name="monthWasEmpty"/> = o ayda HİÇ soru yoktu (totalCount 0). Üst üste
    /// <see cref="ChannelQuestionSyncConsts.EmptyMonthsBeforeStop"/> boş ay ya da
    /// <see cref="ChannelQuestionSyncConsts.MaxSeedMonths"/> tavanı → seed biter.</para></summary>
    public virtual void CompleteSeedMonth(DateTime completedMonthStart, bool monthWasEmpty)
    {
        SeedMonthsProcessed++;
        ConsecutiveEmptyMonths = monthWasEmpty ? ConsecutiveEmptyMonths + 1 : 0;
        SeedPageIndex = 0;
        SeedMonthStart = completedMonthStart;

        if (ConsecutiveEmptyMonths >= ChannelQuestionSyncConsts.EmptyMonthsBeforeStop
            || SeedMonthsProcessed >= ChannelQuestionSyncConsts.MaxSeedMonths)
        {
            SeedCompleted = true;
            return;
        }

        SeedMonthStart = completedMonthStart.AddMonths(-1);
    }

    /// <summary>Artımlı (açık soru) sayfasının sonucunu işler: daha sayfa varsa imleci ilerletir, son sayfaysa
    /// <c>LastRefreshedAt</c>'i yazar (eşik BURADAN itibaren işler).</summary>
    public virtual void ApplyRefreshPage(int pageCount, DateTime nowUtc)
    {
        if (RefreshPageIndex + 1 < pageCount)
        {
            RefreshPageIndex++;
            return;
        }

        RefreshPageIndex = 0;
        LastRefreshedAt = nowUtc;
    }

    public override string ToString()
    {
        return $"{ChannelType}:{SalesChannelId}";
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

    #endregion
}
