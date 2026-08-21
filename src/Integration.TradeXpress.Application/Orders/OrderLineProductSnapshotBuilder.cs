using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Bir <see cref="EntityVariant"/>'ın (Product uzantısı; EntityName="Product") "eşleşme anı" görünümünü (isim + görsel)
/// donduran PAYLAŞILAN yardımcı — hem otomatik eşleştirme (<c>OrderLineProductMatcher</c>, sync sırasında) hem manuel
/// eşleştirme (<c>OrderAppService.SaveOrderLineEditAsync</c>) kullanır (DRY).
///
/// <para><b>Kaynak merkezi DAM</b> (legacy <c>ProductImage</c> 2026-07-31'de emekli): görsel VARYANT-FARKINDA geri
/// düşüşle seçilir — önce varyantın kendi seti ("ProductVariant" bağlamı), yoksa ürünün kayıt geneli seti ("Product").
/// Gerekçe: satır belirli bir varyanta eşleşti; görselin ürün-düzeyi default'tan gelmesi yanlış SKU'yu gösterir
/// (ör. Mavi yüzük satırına Kırmızı thumbnail). cover-önce (IsDefault) sırası <c>GetPushMediaAsync</c>'te çözülür; snapshot
/// poster URL'ini dondurur (medya Id-scoped endpoint — blob adı sızmaz).</para>
/// </summary>
public class OrderLineProductSnapshotBuilder : ITransientDependency
{
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaAppService _media;

    public OrderLineProductSnapshotBuilder(IEntityMediaAppService entityMedia, IMediaAppService media)
    {
        _entityMedia = entityMedia;
        _media = media;
    }

    /// <summary>Varyantın o ANDAKİ isim + görselini döner (isim her zaman dolu; görsel yoksa null). Jenerik
    /// <see cref="EntityVariant"/> — sahip ürün Id'si <see cref="EntityVariant.EntityId"/>'de (EntityName="Product").</summary>
    public async Task<(string Name, string? ImageUrl)> BuildAsync(EntityVariant variant)
    {
        var imageUrl = await ResolvePreferredPosterUrlAsync(variant);
        return (variant.Name, imageUrl);
    }

    // Varyantın kendi medyası → ürünün kayıt geneli medyası → null. Push seçim kuralları (pasif elenir,
    // cover önce) burada da geçerli — sipariş satırı thumbnail'i pazaryerindeki vitrinle aynı görsel olmalı.
    private async Task<string?> ResolvePreferredPosterUrlAsync(EntityVariant variant)
    {
        var set = await _entityMedia.GetPushMediaAsync(MediaEntityNames.ProductVariant, variant.Id, MediaType.Image);
        if (set.Count == 0)
        {
            set = await _entityMedia.GetPushMediaAsync(MediaEntityNames.Product, variant.EntityId, MediaType.Image);
        }

        var first = set.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        var media = (await _media.GetByIdsAsync(new[] { first.MediaId }.ToList())).FirstOrDefault();
        return media?.PosterUrl ?? media?.ContentUrl;
    }
}
