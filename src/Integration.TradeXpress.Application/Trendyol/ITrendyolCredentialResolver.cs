using System;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Trendyol;

/// <summary>
/// Per-kanal Trendyol kimlik çözücüsü (server-side infra) — bir <see cref="SalesChannels.SalesChannelTrTrendyol"/>
/// kaydından SellerId/ApiKey/ApiSecret'ı okur (client sırrı bilmez; AppService kimliği burada çözer). MERKEZİ tek
/// kimlik YOK: her çağrı ilgili company'nin ya da verilen kanalın kendi kaydını kullanır (eleştiri F-kimlik). Kategori
/// endpoint'i public olsa da (auth gerektirmez) zorunlu <c>User-Agent</c> için SellerId lazım → taban bu çözümü ister.
/// </summary>
public interface ITrendyolCredentialResolver
{
    /// <summary>Çalışılan şirketin Trendyol satış kanalının kimliğini çözer. Şirket/kanal yoksa <c>BusinessException</c>.</summary>
    Task<TrendyolCredentials> ResolveForCurrentCompanyAsync(CancellationToken cancellationToken = default);

    /// <summary>Belirli bir Trendyol satış kanalı kaydının kimliğini çözer (ör. ürün push'unda listeleme kanalı). Yoksa <c>BusinessException</c>.</summary>
    Task<TrendyolCredentials> ResolveBySalesChannelIdAsync(Guid salesChannelId, CancellationToken cancellationToken = default);
}
