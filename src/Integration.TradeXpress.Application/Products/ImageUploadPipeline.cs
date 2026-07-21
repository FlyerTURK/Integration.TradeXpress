using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Guids;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Görsel yükleme boru hattının ORTAK çekirdeği (Product + Metal ve gelecekteki görselli kataloglar):
/// boyut/uzantı guard'ları, gerçek-görsel doğrulaması, thumbnail üretimi ve önizleme data-URL'i tek yerde.
/// Hata kodları çağıranın entity ön-ekiyle üretilir (ör. "TradeXpress:Product" → ":ImageTooLarge") —
/// lokalize mesajlar entity-başına kalır, mantık kalmaz (DRY).
/// </summary>
public static class ImageUploadPipeline
{
    /// <summary>Önizleme uzun kenarı (px) — grid/form thumbnail'i.</summary>
    public const int ThumbnailMaxEdge = 240;

    /// <summary>Decode edilecek görüntünün piksel üst sınırı (40MP) — decompression-bomb koruması:
    /// küçük dosya (4MB guard'ı geçer) devasa boyut bildirip sunucuda GB'larca buffer ayırtamasın.</summary>
    public const long MaxDecodedPixels = 40_000_000;

    /// <summary>Thumbnail blob adı — ana blob'dan türetilir (silme/okuma tek kuraldan). Path-aware:
    /// son '/'a göre klasör + dosya ayrılır → thumbnail dosya adının başına "thumb-" eklenir, klasör KORUNUR
    /// ("Products/KOD/GORSEL0001.jpg" → "Products/KOD/thumb-GORSEL0001.jpg.jpg"). Slash yoksa klasör boş →
    /// eski flat davranışla ("thumb-" + blobName + ".jpg") BİREBİR aynı (mevcut Guid-adlı bloblar bozulmaz).</summary>
    public static string ThumbnailNameOf(string blobName)
    {
        var slash = blobName.LastIndexOf('/');
        var dir = slash >= 0 ? blobName.Substring(0, slash + 1) : string.Empty;
        var file = slash >= 0 ? blobName.Substring(slash + 1) : blobName;
        return dir + "thumb-" + file + ".jpg";
    }

    /// <summary>Thumbnail JPEG içeriğinden önizleme data-URL'i.</summary>
    public static string BuildPreviewDataUrl(byte[] thumbnailContent)
    {
        return "data:image/jpeg;base64," + Convert.ToBase64String(thumbnailContent);
    }

    /// <summary>Yükleme guard'ları: boş içerik / boyut sınırı / uzantı whitelist'i — dostane hata
    /// (<paramref name="errorCodePrefix"/> + ":ImageEmpty|:ImageTooLarge|:ImageTypeNotSupported").</summary>
    public static void EnsureValidUpload(byte[] content, string fileName, int maxSizeBytes, string errorCodePrefix)
    {
        if (content.Length == 0)
        {
            throw new BusinessException(errorCodePrefix + ":ImageEmpty");
        }

        if (content.Length > maxSizeBytes)
        {
            throw new BusinessException(errorCodePrefix + ":ImageTooLarge")
                .WithData("MaxMb", maxSizeBytes / (1024 * 1024));
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ContentTypes.ContainsKey(extension))
        {
            throw new BusinessException(errorCodePrefix + ":ImageTypeNotSupported");
        }
    }

    /// <summary>Görseli en-boy oranını koruyarak küçültür (uzun kenar <see cref="ThumbnailMaxEdge"/> px) → JPEG.
    /// Bozuk/görsel-olmayan içerik dostane hatayla reddedilir (whitelist'i geçen ama gerçek görsel olmayan dosya).</summary>
    public static byte[] BuildThumbnail(byte[] content, string errorCodePrefix)
    {
        try
        {
            // Decode ETMEDEN header'dan boyut oku — piksel sınırını aşan (bomba) içerik dostane hatayla reddedilir,
            // devasa RGBA buffer hiç ayrılmaz (Blazor Server host'u tüm circuit'lerin yaşadığı süreç).
            var info = Image.Identify(content);
            if ((long)info.Width * info.Height > MaxDecodedPixels)
            {
                throw new BusinessException(errorCodePrefix + ":ImageTypeNotSupported");
            }

            using var image = Image.Load(content);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbnailMaxEdge, ThumbnailMaxEdge),
            }));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = 80 });
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw new BusinessException(errorCodePrefix + ":ImageTypeNotSupported");
        }
    }

    /// <summary>
    /// Upload orkestrasyonunun TEK kaynağı (Product + Metal + gelecekteki görselli kataloglar — DRY):
    /// guard'lar → thumbnail → blob adı (Guid + uzantı) → ana blob + thumbnail kaydı → önizleme data-URL.
    /// Çağıran yalnız kendi result DTO'sunu kurar.
    /// </summary>
    public static async Task<ImageUploadResult> UploadAsync(
        IBlobContainer container,
        IGuidGenerator guidGenerator,
        string fileName,
        byte[] content,
        int maxSizeBytes,
        string errorCodePrefix)
    {
        EnsureValidUpload(content, fileName, maxSizeBytes, errorCodePrefix);
        var thumbnail = BuildThumbnail(content, errorCodePrefix);

        var blobName = guidGenerator.Create().ToString("N")
            + Path.GetExtension(fileName).ToLowerInvariant();
        await container.SaveAsync(blobName, content);
        await container.SaveAsync(ThumbnailNameOf(blobName), thumbnail);

        return new ImageUploadResult(blobName, BuildPreviewDataUrl(thumbnail));
    }

    /// <summary>
    /// PATH ön-ekli upload (Product görselleri — anlamlı blob anahtarı): guard'lar → thumbnail → İLK BOŞ SIRAYI
    /// probe et ("GORSEL0001", "GORSEL0002", …; <paramref name="blobFolder"/> + "/" altında ExistsAsync ile) →
    /// ana blob + thumbnail kaydı → önizleme data-URL. <paramref name="blobFolder"/> trailing slash İSTEMEZ
    /// (ör. "Products/KOD/VARYANTKOD"). Flat Guid-adlı <see cref="UploadAsync"/> ile çekirdeği (guard/thumbnail) paylaşır.
    /// </summary>
    public static async Task<ImageUploadResult> UploadToFolderAsync(
        IBlobContainer container,
        string blobFolder,
        string fileName,
        byte[] content,
        int maxSizeBytes,
        string errorCodePrefix)
    {
        EnsureValidUpload(content, fileName, maxSizeBytes, errorCodePrefix);
        var thumbnail = BuildThumbnail(content, errorCodePrefix);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var blobName = await ProbeNextFreeNameAsync(container, blobFolder, extension);
        await container.SaveAsync(blobName, content);
        await container.SaveAsync(ThumbnailNameOf(blobName), thumbnail);

        return new ImageUploadResult(blobName, BuildPreviewDataUrl(thumbnail));
    }

    /// <summary>İlk boş "GORSEL{n:D4}{ext}" adını bulur — n=1'den başlar, klasördeki ad DOLUYSA artırır
    /// (aynı klasöre tekrar yükleme çakışmasın; blob adı tekilliğin TEK kaynağı olur).</summary>
    private static async Task<string> ProbeNextFreeNameAsync(IBlobContainer container, string blobFolder, string extension)
    {
        var index = 1;
        while (true)
        {
            var candidate = blobFolder + "/GORSEL" + index.ToString("D4") + extension;
            if (!await container.ExistsAsync(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    /// <summary>Upload yetkisi: Create YA DA Update yeterli (yeni kayıt oluştururken de yüklenir;
    /// yalnız-Create'li kullanıcı takılmasın — Product review bulgusuyla gelen ORTAK kural).</summary>
    public static async Task EnsureCanUploadAsync(
        IAuthorizationService authorizationService, string createPolicy, string updatePolicy)
    {
        if (await authorizationService.IsGrantedAsync(createPolicy)
            || await authorizationService.IsGrantedAsync(updatePolicy))
        {
            return;
        }

        throw new AbpAuthorizationException(code: updatePolicy);
    }

    // İzinli görsel türleri (uzantı → mime). Whitelist — başka tür yüklemesi dostane hatayla reddedilir.
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };
}

/// <summary>Ortak upload sonucu — servis-özel result DTO'ları (Product/Metal) bundan kurulur.</summary>
public sealed record ImageUploadResult(string BlobName, string PreviewDataUrl);
