using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Satış kanalı tanımının <b>soyut TPT tabanı</b> (Table-Per-Type): ortak kimlik + sahiplik alanları burada,
/// pazaryeri-özel yapılandırma (API kimlik bilgileri vb.) somut alt-tiplerde (ör. <see cref="SalesChannelTrN11"/>).
/// Base tablo <c>AppSalesChannels</c>; her alt-tip kendi tablosunu ekler (paylaşılan PK/FK).
///
/// <para><b>Company-owned</b> güvenlik sınırı (<see cref="ICompanyOwned"/>, non-nullable <see cref="CompanyId"/>)
/// + per-tenant (<see cref="IMultiTenant"/>): her şirketin kendi kanalları; "tüm şirketlere açık" hâli YOKTUR.
/// Kapsam DAİMA çalışılan şirket (sunucu <c>ICurrentCompany</c> ile zorlar; client CompanyId göndermez).</para>
/// </summary>
public abstract class SalesChannelBase : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelBase()
    {
    }

    protected SalesChannelBase(
        Guid companyId,
        string code,
        string name,
        bool isActive = true)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Oluşturmadan sonra değişmez (set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    /// <summary>Kanalın yan-maliyet (gider) ayarları — owned JSON (<c>SideCosts</c> kolonu). Null = hiç
    /// yapılandırılmamış. Kanal-agnostik TEK tip: hangi kanal hangi alanı kullanıyorsa doldurur.
    /// <c>SideCostRecipeComposer</c> buradan kanal varyant reçetesine otomatik satırlar üretir.</summary>
    public virtual SideCostSettings? SideCosts { get; protected set; }

    /// <summary>
    /// Bu kanalda gönderilen paketin VARSAYILAN desisi — kargo tarifesinin girdisi. Varyantta
    /// <c>ProductVariantDetail.PackageDesi</c> doluysa O kazanır; buradaki değer "çoğu paket bu boyutta"
    /// demektir ve ürün başına veri girişini gereksiz kılar.
    /// <para>Varsayılan 1: kuyumda gönderiler küçük ve tarifenin en alt basamağında (desi 0-2) toplanıyor.</para>
    /// </summary>
    public virtual int DefaultPackageDesi { get; protected set; } = 1;

    // ── Muhasebe hedefi (2026-07-28 Hakan): "bu kanalın muhasebesi HANGİ cariye yazılır" ──
    // Kanal bir pazaryeriyle olan hesabımızdır (komisyon borcu, hakediş alacağı). Hedef bugüne kadar sistemde
    // HİÇBİR yerde tanımlı değildi; gider satırlarındaki cari alanları vardı ama hiçbir akış onları okumuyordu.
    // Bugün bu bir TANIM'dır: sipariş→fiş köprüsü olmadığı için kendiliğinden kayıt üretmez.

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>; id-only, nav YOK). Kullanıcının KENDİ
    /// cari planından seçilir — sistem cari üretmez. <c>null</c> = henüz tanımlanmamış.
    ///
    /// <para><b>Cari hesap AYRICA tutulmaz:</b> alt hesap zaten bir cariye bağlıdır (<c>SubAccount.AccountId</c>),
    /// ikisini birden saklamak aynı bilgiyi iki yerde tutup çelişme riski açardı (kanalda A carisi, alt hesapta
    /// B carisi yazan bir kayıt). Cari gerektiğinde alt hesaptan okunur.</para></summary>
    public virtual Guid? SubAccountId { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            SalesChannelConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            SalesChannelConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Yan-maliyet ayarlarını ATOMİK atar (yarım güncelleme yok — VO ctor'u guard'ları çalıştırmış
    /// gelir: negatif tutar/oran, GrossUp [0,100) sınırı). Null = ayarları temizle.</summary>
    public virtual void SetSideCosts(SideCostSettings? sideCosts)
    {
        SideCosts = sideCosts;
    }

    /// <summary>Kanalın muhasebe cari alt hesabını bağlar/çözer (boş Guid → null = tanımsız). Alt hesabın
    /// ŞİRKETE ait ve var olduğu doğrulaması AppService'te yapılır (burada repository yok).</summary>
    public virtual void SetSubAccount(Guid? subAccountId)
    {
        SubAccountId = subAccountId == Guid.Empty ? null : subAccountId;
    }

    /// <summary>Kanalın varsayılan paket desisini set eder. Negatif desi yoktur (fail-fast); 0 geçerlidir
    /// (pazaryerinin "Dosya" basamağı).</summary>
    public virtual void SetDefaultPackageDesi(int defaultPackageDesi)
    {
        if (defaultPackageDesi < 0)
        {
            throw new BusinessException("TradeXpress:SalesChannel:DefaultPackageDesiNegative");
        }

        DefaultPackageDesi = defaultPackageDesi;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId+CompanyId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            SalesChannelConsts.CodeMaxLength);
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
