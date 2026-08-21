using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Satış kanalı BİRLEŞİK (polymorphic) servis arayüzü — TÜM TPT alt-tiplerini tek listede sunar + tür-bağımsız silme.
/// Tipe-özel oluşturma/güncelleme <see cref="ISalesChannelTrN11AppService"/> / <see cref="ISalesChannelTrTrendyolAppService"/>
/// üzerinden (her biri generic <c>ICrudAppService</c>; kendi formu). Company-owned (sunucu <c>ICurrentCompany</c> zorlar).
/// </summary>
public interface ISalesChannelAppService : IApplicationService
{
    /// <summary>Tüm kanal alt-tipleri (base sorgusu) — liste satırı <see cref="SalesChannelListDto.ChannelType"/> taşır.</summary>
    Task<PagedResultDto<SalesChannelListDto>> GetListAsync(SalesChannelListRequestDto input);

    /// <summary>Tür-bağımsız silme (base id) — TPT cascade alt-tip satırını da düşürür.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Çalışılan şirkette HÂLİHAZIRDA bulunan kanal türleri (IsActive'e bakılmaz). Kural: her türden en
    /// fazla bir kanal → UI "Yeni ▾" bu türleri devre dışı bırakır (server de Create'te zorlar).</summary>
    Task<List<SalesChannelType>> GetExistingChannelTypesAsync();
}
