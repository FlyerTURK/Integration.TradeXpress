using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Attachments;

/// <summary>Kütüphane/okuma DTO'su — bir medya varlığı (görsel/video). Poster + içerik URL'lerini sunucu kurar
/// (Id-scoped stream endpoint'i; ham blob adı client'a sızmaz → BOLA daraltma).</summary>
public class MediaDto
{
    public Guid Id { get; set; }
    public MediaType MediaType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }

    /// <summary>İçinde bulunduğu kütüphane klasörü (null = kök/klasörsüz).</summary>
    public Guid? FolderId { get; set; }

    public bool HasPoster { get; set; }

    /// <summary>Poster (grid thumbnail) endpoint'i — poster yoksa null (UI jenerik + ▶ gösterir).</summary>
    public string? PosterUrl { get; set; }

    /// <summary>İçerik (tam görsel / video oynatma) endpoint'i — range destekli.</summary>
    public string ContentUrl { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public override string ToString()
    {
        return $"{MediaType}:{FileName}";
    }
}

/// <summary>Kütüphane listeleme isteği — company-scoped (sunucu CurrentCompany ile daraltır); arama + tür filtresi + paging.</summary>
public class MediaListRequestDto : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public MediaType? MediaType { get; set; }

    /// <summary>Klasör filtresi — FilterByFolder=false ⇒ tüm klasörler; true ⇒ yalnız FolderId (null = klasörsüz/kök).</summary>
    public Guid? FolderId { get; set; }
    public bool FilterByFolder { get; set; }
}

/// <summary>Dosya yükleme — içerik + ad. Blob'a SELF-CONTAINED yazılır (dedup: içerik-hash).</summary>
public class MediaUploadDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>Hedef klasör (null = kök) — kütüphanede seçili klasöre yükleme.</summary>
    public Guid? FolderId { get; set; }
}

/// <summary>URL'den içe aktar — sunucu içeriği FETCH edip blob'a yazar (URL SAKLANMAZ). SSRF guard'lı (yalnız http/https + boyut/timeout).</summary>
public class MediaImportUrlDto
{
    public string Url { get; set; } = string.Empty;
    public string? FileName { get; set; }

    /// <summary>Hedef klasör (null = kök) — kütüphanede seçili klasöre içe aktarma.</summary>
    public Guid? FolderId { get; set; }
}

/// <summary>Video poster'ını İSTEMCİ-yakalamayla ayarla — &lt;video&gt; karesi canvas→JPEG. Süre/boyut opsiyonel (client'tan).</summary>
public class SetMediaPosterDto
{
    public Guid MediaId { get; set; }
    public byte[] PosterContent { get; set; } = Array.Empty<byte>();
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

/// <summary>Entity→medya link OKUMA DTO'su — link meta (sıra/varsayılan/aktif) + gösterim için medya.</summary>
public class EntityMediaLinkDto
{
    public Guid MediaId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public MediaDto Media { get; set; } = new();
}

/// <summary>Entity→medya link DÜZENLEME düğümü (panel + ReplaceFor girdisi). Yalnız link meta'sı + MediaId taşır.</summary>
public class EntityMediaLinkEditDto
{
    /// <summary>İstemci-tarafı satır anahtarı (@key) — kalıcı değil.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Gösterim için çözülmüş medya (panel doldurur; ReplaceFor yoksayar — MediaId esastır).</summary>
    public MediaDto? Media { get; set; }
}

/// <summary>Kütüphane klasörü — ağaç düğümü (company-scoped, hiyerarşik). ParentId null = kök.</summary>
public class MediaFolderDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

/// <summary>Yeni klasör oluştur — ad + opsiyonel üst klasör.</summary>
public class CreateMediaFolderDto
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}

/// <summary>Klasör güncelle — yeniden adlandır ve/veya taşı (üst klasör). Döngü sunucuda engellenir.</summary>
public class UpdateMediaFolderDto
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}

/// <summary>Medyayı klasöre taşı — bir/çok medya → hedef klasör (null = kök/klasörsüz).</summary>
public class MoveMediaToFolderDto
{
    public List<Guid> MediaIds { get; set; } = new();
    public Guid? FolderId { get; set; }
}
