using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Company-scoped, SELF-CONTAINED medya varlığı (görsel VEYA video) — merkezi yeniden-kullanılabilir kütüphane (DAM).
/// İçerik DAİMA bizim blob'umuzda (<see cref="BlobName"/>); URL referansı TUTULMAZ — yükleme ya da URL-import içeriği
/// indirip blob'a yazar, kaynak silinse de bizde kalır. İçerik-hash (<see cref="ContentHash"/>) ile dedup (aynı içerik
/// ikinci kez yazılmaz). Entity'ler <see cref="EntityMediaLink"/> ile REFERANS verir (aynı medya çok yerde). Poster:
/// görselde küçültülmüş thumbnail, videoda çıkarılmış/kullanıcı-seçili kare (<see cref="PosterBlobName"/>).
/// </summary>
public class Media : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected Media()
    {
    }

    public Media(
        Guid? companyId,
        MediaType mediaType,
        string blobName,
        string fileName,
        string contentType,
        long size,
        string contentHash)
    {
        CompanyId = companyId;
        MediaType = mediaType;
        BlobName = StringFieldGuard.EnsureRequiredText(blobName, nameof(BlobName), 1, MediaConsts.BlobNameMaxLength);
        SetFileName(fileName);
        ContentType = StringFieldGuard.EnsureRequiredText(contentType, nameof(ContentType), 1, MediaConsts.ContentTypeMaxLength);
        Size = size;
        ContentHash = StringFieldGuard.EnsureRequiredText(contentHash, nameof(ContentHash), 1, MediaConsts.ContentHashMaxLength);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (company-scope; null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    public virtual MediaType MediaType { get; protected set; }

    /// <summary>Bizim blob'umuzun adı (Guid) — içerik burada, DAİMA dolu. URL saklanmaz.</summary>
    public virtual string BlobName { get; protected set; } = null!;

    /// <summary>Poster/thumbnail blob adı — görselde küçültülmüş kare, videoda seçili kare. Yoksa (henüz) null.</summary>
    public virtual string? PosterBlobName { get; protected set; }

    /// <summary>Orijinal/görünen dosya adı (kullanıcıya gösterim).</summary>
    public virtual string FileName { get; protected set; } = null!;

    public virtual string ContentType { get; protected set; } = null!;

    public virtual long Size { get; protected set; }

    /// <summary>İçerik SHA-256 hex'i — company içinde dedup anahtarı (aynı içerik reuse edilir, tekrar yazılmaz).</summary>
    public virtual string ContentHash { get; protected set; } = null!;

    public virtual int? Width { get; protected set; }

    public virtual int? Height { get; protected set; }

    /// <summary>Video süresi (saniye) — yalnız video; poster/metadata üretiminde doldurulur.</summary>
    public virtual double? DurationSeconds { get; protected set; }

    /// <summary>İçinde bulunduğu kütüphane klasörü (null = kök/klasörsüz). Organizasyon amaçlı; içeriği etkilemez.</summary>
    public virtual Guid? FolderId { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetFileName(string fileName)
    {
        FileName = StringFieldGuard.EnsureRequiredText(fileName, nameof(FileName), 1, MediaConsts.FileNameMaxLength);
    }

    /// <summary>Poster blob'unu ayarlar (görsel thumbnail / video kare); temizlemek için null.</summary>
    public virtual void SetPoster(string? posterBlobName)
    {
        PosterBlobName = StringFieldGuard.EnsureOptionalText(posterBlobName, nameof(PosterBlobName), 1, MediaConsts.BlobNameMaxLength);
    }

    public virtual void SetDimensions(int? width, int? height)
    {
        Width = width;
        Height = height;
    }

    public virtual void SetDuration(double? seconds)
    {
        DurationSeconds = seconds;
    }

    /// <summary>Medyayı bir kütüphane klasörüne taşır (null = kök/klasörsüz).</summary>
    public virtual void SetFolder(Guid? folderId)
    {
        FolderId = folderId;
    }

    public override string ToString()
    {
        return FileName;
    }

    #endregion
}
