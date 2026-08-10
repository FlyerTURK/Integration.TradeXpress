using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>Trendyol satış kanalı CRUD (tipe-özel) — generic <c>ICrudAppService</c>; company-owned. Liste tür-bağımsız
/// <see cref="ISalesChannelAppService"/>'te; burada Trendyol'a özel get/create/update (SellerId/ApiKey/ApiSecret).</summary>
public interface ISalesChannelTrTrendyolAppService : ICrudAppService<
    SalesChannelTrTrendyolGetDto,
    SalesChannelListDto,
    Guid,
    SalesChannelListRequestDto,
    SalesChannelTrTrendyolCreateDto,
    SalesChannelTrTrendyolUpdateDto>
{
    /// <summary>Yalnız varsayılan kargo firmasını değiştirir.
    /// <para><b>Neden dar bir uç, neden <c>UpdateAsync</c> değil:</b> tam güncelleme kimlik alanlarını
    /// (SellerId/ApiKey/ApiSecret/cari) da taşır ve çağıranın onları eksiksiz göndermesini şart koşar.
    /// Kurulum sihirbazı mevcut kanal kipinde bu alanları HİÇ yüklemiyor — tam DTO ile güncelleme, dokunmadığı
    /// alanları sessizce boşaltma riski demekti. Tek alanı değiştiren yol o riski taşımaz.</para></summary>
    Task<SalesChannelTrTrendyolGetDto> SetDefaultCargoProviderAsync(Guid id, Guid? cargoProviderId);
}
