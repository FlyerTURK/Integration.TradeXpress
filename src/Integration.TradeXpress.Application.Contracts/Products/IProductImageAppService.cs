using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Products;

/// <summary>Yüklenecek görsel dosyası — içerik byte[] (in-process; ABP JSON'da base64 serialize eder).</summary>
public class ProductImageUploadDto
{
    [Required]
    [StringLength(ProductConsts.ImageFileNameMaxLength)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>Görselin ait olduğu ürün kodu — blob adının path ön-ekini (Products/{Kod}/…) üretir.</summary>
    [Required]
    [StringLength(ProductConsts.CodeMaxLength)]
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Görseli bir VARYANTA bağlar (blob path'ine …/{VaryantKodu}/ segmenti eklenir).
    /// Boş = ürün-geneli görsel (Products/{Kod}/… altında).</summary>
    [StringLength(EntityVariantConsts.VariantCodeMaxLength)]
    public string? VariantCode { get; set; }
}

/// <summary>Yükleme sonucu — blob referansı + anında gösterim için önizleme.</summary>
public class ProductImageUploadResultDto
{
    public string BlobName { get; set; } = string.Empty;

    /// <summary>data:{mime};base64,... — grid/form önizlemesi.</summary>
    public string PreviewDataUrl { get; set; } = string.Empty;
}

/// <summary>
/// Ürün görseli dosya servisi — dosyayı blob storage'a (Database provider) yazar; ürün kaydı yalnız
/// <c>BlobName</c> referansını taşır. İçerik yalnız authorized çağrıyla okunur (anonim endpoint YOK —
/// 2026-07-07 kullanıcı kararı: marketplace push için dış URL, production'da geçici dosya-hosting ile üretilecek).
/// </summary>
public interface IProductImageAppService : IApplicationService
{
    /// <summary>Dosyayı blob'a yükler (boyut/uzantı guard'lı) — blob adı + önizleme döner.</summary>
    Task<ProductImageUploadResultDto> UploadAsync(ProductImageUploadDto input);
}
