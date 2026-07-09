using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>Etsy satış kanalı CRUD (tipe-özel) — generic <c>ICrudAppService</c>; company-owned. Liste tür-bağımsız
/// <see cref="ISalesChannelAppService"/>'te; burada Etsy'ye özel get/create/update (Keystring/SharedSecret) +
/// OAuth 2.0 PKCE bağlantı başlatma (token'lar sunucuda yaşar, DTO'ya HİÇ çıkmaz).</summary>
public interface ISalesChannelEtsyAppService : ICrudAppService<
    SalesChannelEtsyGetDto,
    SalesChannelListDto,
    Guid,
    SalesChannelListRequestDto,
    SalesChannelEtsyCreateDto,
    SalesChannelEtsyUpdateDto>
{
    /// <summary>OAuth 2.0 Authorization Code + PKCE akışını başlatır: state + code_verifier üretip geçici saklar
    /// (10 dk) ve Etsy onay sayfasının URL'ini döner. UI kullanıcıyı bu URL'e yönlendirir; Etsy geri dönüşü
    /// <c>/etsy/oauth-callback</c> endpoint'inde karşılanır (state doğrula → token değişimi → kanala yaz).</summary>
    Task<string> StartOAuthAsync(Guid id);
}
