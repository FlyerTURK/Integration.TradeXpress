using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy kanal-ürün — dükkân bölümü picker beslemesi dilimi (salt-okuma). Listeleme dükkân-bölümü seçimi için mağazanın
/// bölümlerini <c>getShopSections</c> ile listeler; Etsy'ye SIFIR yazma. Kimlik/token çözümü kargo profili dilimiyle
/// BİREBİR (<see cref="GetShippingProfilesAsync"/>): <see cref="EtsyCredentials"/> kurulur, token client içindeki
/// <c>IEtsyTokenProvider</c> ile şeffaf çözülür. Gevşek referans: yerelde yalnız <c>ShopSectionId</c> saklanır.
/// </summary>
public partial class SalesChannelEtsyProductAppService
{
    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<List<EtsyShopSectionDto>> GetShopSectionsAsync(Guid salesChannelId)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var sections = await _etsyProductClient.GetShopSectionsAsync(credentials);

        return sections
            .Select(ToShopSectionDto)
            .ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<EtsyShopSectionDto> CreateShopSectionAsync(Guid salesChannelId, EtsyShopSectionInputDto input)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var created = await _etsyProductClient.CreateShopSectionAsync(credentials, input.Title.Trim());
        return ToShopSectionDto(created);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<EtsyShopSectionDto> UpdateShopSectionAsync(Guid salesChannelId, long shopSectionId, EtsyShopSectionInputDto input)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var updated = await _etsyProductClient.UpdateShopSectionAsync(credentials, shopSectionId, input.Title.Trim());
        return ToShopSectionDto(updated);
    }

    private static EtsyShopSectionDto ToShopSectionDto(EtsyShopSectionSummary section)
    {
        return new EtsyShopSectionDto { Id = section.Id, Title = section.Title };
    }
}
