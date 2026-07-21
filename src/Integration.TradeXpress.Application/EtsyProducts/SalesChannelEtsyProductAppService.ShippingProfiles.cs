using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy kanal-ürün — kargo profili picker beslemesi dilimi (salt-okuma). Push kargo-profili seçimi için mağazanın
/// kargo profillerini <c>getShopShippingProfiles</c> ile listeler; Etsy'ye SIFIR yazma. Kimlik/token çözümü import
/// dilimiyle BİREBİR: <see cref="EtsyCredentials"/> (kanal id + <c>x-api-key</c> = <c>{keystring}:{secret}</c> +
/// shopId) kurulur, geçerli access token'ı client içindeki <c>IEtsyTokenProvider</c> şeffaf çözer (rotasyon/yenileme
/// bilinmez). Gevşek referans: yerelde yalnız <c>ShippingProfileId</c> saklanır, profiller Etsy'de tanımlıdır.
/// </summary>
public partial class SalesChannelEtsyProductAppService
{
    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<List<EtsyShippingProfileDto>> GetShippingProfilesAsync(Guid salesChannelId)
    {
        // Kimlik demeti import ile AYNI: token client içinde IEtsyTokenProvider ile (gerekirse yenilenerek) çözülür.
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var profiles = await _etsyProductClient.GetShopShippingProfilesAsync(credentials);

        return profiles
            .Select(p => new EtsyShippingProfileDto { Id = p.Id, Title = p.Title })
            .ToList();
    }
}
