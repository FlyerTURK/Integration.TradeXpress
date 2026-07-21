using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy kanal-ürün — iade politikası picker beslemesi dilimi (salt-okuma). Push iade-politikası seçimi için mağazanın
/// iade politikalarını <c>getShopReturnPolicies</c> ile listeler; Etsy'ye SIFIR yazma. Kimlik/token çözümü kargo profili
/// dilimiyle BİREBİR (<see cref="GetShippingProfilesAsync"/>): <see cref="EtsyCredentials"/> kurulur, token client içindeki
/// <c>IEtsyTokenProvider</c> ile şeffaf çözülür. Etsy iade politikasının BAŞLIĞI YOKTUR → görüntü etiketi iade/değişim +
/// süre alanlarından burada (lokalize) türetilir. Gevşek referans: yerelde yalnız <c>ReturnPolicyId</c> saklanır.
/// </summary>
public partial class SalesChannelEtsyProductAppService
{
    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<List<EtsyReturnPolicyDto>> GetReturnPoliciesAsync(Guid salesChannelId)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var policies = await _etsyProductClient.GetShopReturnPoliciesAsync(credentials);

        return policies
            .Select(ToReturnPolicyDto)
            .ToList();
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<EtsyReturnPolicyDto> CreateReturnPolicyAsync(Guid salesChannelId, EtsyReturnPolicyInputDto input)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var created = await _etsyProductClient.CreateReturnPolicyAsync(
            credentials, input.AcceptsReturns, input.AcceptsExchanges, input.ReturnDeadlineDays);
        return ToReturnPolicyDto(created);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Default)]
    public virtual async Task<EtsyReturnPolicyDto> UpdateReturnPolicyAsync(Guid salesChannelId, long returnPolicyId, EtsyReturnPolicyInputDto input)
    {
        var credentials = await ResolveEtsyCredentialsAsync(salesChannelId);
        var updated = await _etsyProductClient.UpdateReturnPolicyAsync(
            credentials, returnPolicyId, input.AcceptsReturns, input.AcceptsExchanges, input.ReturnDeadlineDays);
        return ToReturnPolicyDto(updated);
    }

    // Ham client özetini picker DTO'suna çevirir — Etsy politikasının başlığı olmadığından etiket burada (lokalize)
    // türetilir; ham alanlar (iade/değişim/süre) düzenle popup'ının ön-doldurması için de taşınır.
    private EtsyReturnPolicyDto ToReturnPolicyDto(EtsyReturnPolicySummary policy)
    {
        return new EtsyReturnPolicyDto
        {
            Id = policy.Id,
            Label = BuildReturnPolicyLabel(policy),
            AcceptsReturns = policy.AcceptsReturns,
            AcceptsExchanges = policy.AcceptsExchanges,
            ReturnDeadlineDays = policy.ReturnDeadlineDays,
        };
    }

    /// <summary>Etsy iade politikasının BAŞLIĞI OLMADIĞINDAN okunur bir görüntü etiketi türetir (lokalize) — kimlik +
    /// iade/değişim işaretleri + varsa iade süresi (gün). Ör. "#123 · iade + değişim · 30 gün". İade/değişim ikisi de
    /// kapalıysa "iade/değişim yok" etiketi.</summary>
    private string BuildReturnPolicyLabel(EtsyReturnPolicySummary policy)
    {
        var kinds = new List<string>();
        if (policy.AcceptsReturns)
        {
            kinds.Add(L["EtsyProduct:ReturnPolicyAcceptsReturns"]);
        }

        if (policy.AcceptsExchanges)
        {
            kinds.Add(L["EtsyProduct:ReturnPolicyAcceptsExchanges"]);
        }

        var parts = new List<string>
        {
            $"#{policy.Id}",
            kinds.Count > 0 ? string.Join(" + ", kinds) : L["EtsyProduct:ReturnPolicyNoReturns"],
        };
        if (policy.ReturnDeadlineDays is { } days)
        {
            parts.Add(L["EtsyProduct:ReturnPolicyDeadlineDays", days]);
        }

        return string.Join(" · ", parts);
    }
}
