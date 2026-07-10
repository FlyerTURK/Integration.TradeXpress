using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Metals;

/// <summary>Yüklenecek maden görsel dosyası — içerik byte[] (in-process; ABP JSON'da base64 serialize eder).</summary>
public class MetalImageUploadDto
{
    [Required]
    [StringLength(MetalConsts.ImageFileNameMaxLength)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>Yükleme sonucu — blob referansı + anında gösterim için önizleme.</summary>
public class MetalImageUploadResultDto
{
    public string BlobName { get; set; } = string.Empty;

    /// <summary>data:{mime};base64,... — grid/form önizlemesi.</summary>
    public string PreviewDataUrl { get; set; } = string.Empty;
}

/// <summary>
/// Maden görseli dosya servisi (Product deseni) — dosyayı blob storage'a (Database provider) yazar; maden kaydı
/// yalnız <c>BlobName</c> referansını taşır. İçerik yalnız authorized çağrıyla okunur (anonim endpoint YOK —
/// 2026-07-07 kullanıcı kararıyla hizalı).
/// </summary>
public interface IMetalImageAppService : IApplicationService
{
    /// <summary>Dosyayı blob'a yükler (boyut/uzantı guard'lı) — blob adı + önizleme döner.</summary>
    Task<MetalImageUploadResultDto> UploadAsync(MetalImageUploadDto input);
}
