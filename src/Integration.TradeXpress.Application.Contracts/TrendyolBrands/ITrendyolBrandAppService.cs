using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// Trendyol marka arama — type-ahead. Markayı ADA GÖRE aratıp <c>BrandId</c> çözer (kanal-üründe brandId zorunlu).
/// Marka verisi UÇUCU (milyonlarca marka → tam sync YOK, entity/DB yok); arama endpoint'i SSOT. Kimlik, çalışılan
/// şirketin Trendyol kanalından çözülür. <see cref="TrendyolCategories.ITrendyolCategoryAppService"/> ile hizalı
/// (ayrı izin yok — IApplicationService varsayılanı = kimlik doğrulaması yeter).
/// </summary>
public interface ITrendyolBrandAppService : IApplicationService
{
    /// <summary>Markayı ada göre aratır (type-ahead). En az 2 harf; aksi halde boş (istemciye liste dökülmez).
    /// Uçucu arama — sonuç saklanmaz.</summary>
    Task<List<TrendyolBrandDto>> SearchAsync(string term);
}
