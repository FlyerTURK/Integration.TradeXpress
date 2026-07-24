using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// Trendyol marka arama — type-ahead + HYBRID cache (K3, 2026-07-23). Markayı ADA GÖRE aratıp <c>BrandId</c> çözer
/// (kanal-üründe brandId zorunlu). Marka evreni milyonlarca kayıt → TAM SYNC YOK; canlı arama endpoint'i SSOT kalır.
/// Yalnız kullanıcının SEÇİP kanal-ürüne kaydettiği markalar host-global <c>TrendyolBrand</c> cache'ine write-through
/// düşer; picker açılışta o cache'ten beslenir (<see cref="GetCachedListAsync"/> — form açılışında canlı API çağrısı
/// YOK). Kimlik, çalışılan şirketin Trendyol kanalından çözülür.
/// <see cref="TrendyolCategories.ITrendyolCategoryAppService"/> ile hizalı (ayrı izin yok — IApplicationService
/// varsayılanı = kimlik doğrulaması yeter).
/// </summary>
public interface ITrendyolBrandAppService : IApplicationService
{
    /// <summary>Markayı ada göre aratır (type-ahead, CANLI API). En az 2 harf; aksi halde boş (istemciye liste
    /// dökülmez). Arama sonucu saklanmaz — yalnız SEÇİLEN marka kanal-ürün kaydında cache'e düşer.</summary>
    Task<List<TrendyolBrandDto>> SearchAsync(string term);

    /// <summary>Write-through cache'lenmiş markaları döner (picker açılış beslemesi; ada göre sıralı).
    /// Host-global okuma — canlı API'ye ÇIKMAZ.</summary>
    Task<List<TrendyolBrandDto>> GetCachedListAsync();
}
