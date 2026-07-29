using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Blazor.Controllers;

/// <summary>
/// Medya İÇERİĞİNİ + POSTER'ını akıtan Id-scoped endpoint (ham blob adı client'a sızmaz → BOLA daraltma). Media Id'siyle
/// tenant/company data-filtreli repo lookup yapar (yabancı tenant/şirket → null → NotFound), sonra blob'u akıtır. Video
/// range-request destekli (&lt;video&gt; seek/scrub). [Authorize] + aynı-origin cookie ile giriş yapmış kullanıcı erişir.
/// </summary>
[Authorize]
[Route("api/media")]
public class MediaContentController : AbpController
{
    private readonly IRepository<Media, Guid> _repository;
    private readonly IBlobContainer<MediaContainer> _container;
    private readonly IMediaPublicLinkProvider _publicLink;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;

    public MediaContentController(
        IRepository<Media, Guid> repository,
        IBlobContainer<MediaContainer> container,
        IMediaPublicLinkProvider publicLink,
        ICurrentTenant currentTenant,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _container = container;
        _publicLink = publicLink;
        _currentTenant = currentTenant;
        _currentCompany = currentCompany;
    }

    [HttpGet("{id}/content")]
    public async Task<IActionResult> GetContentAsync(Guid id)
    {
        var media = await _repository.FindAsync(id);   // tenant/company data-filter → yabancıya null
        if (media is null)
        {
            return NotFound();
        }

        var stream = await _container.GetOrNullAsync(media.BlobName);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, media.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id}/poster")]
    public async Task<IActionResult> GetPosterAsync(Guid id)
    {
        var media = await _repository.FindAsync(id);
        if (media?.PosterBlobName is not { Length: > 0 } poster)
        {
            return NotFound();
        }

        var stream = await _container.GetOrNullAsync(poster);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "image/jpeg", enableRangeProcessing: true);
    }

    /// <summary>
    /// İMZALI JETONLA medya içeriği — pazaryeri sunucusu (N11/Trendyol) görseli kendi tarafına çekerken kullanır.
    ///
    /// <para><b>Neden oturum aranmıyor:</b> uzak pazaryeri oturum çerezi taşıyamaz. Erişim korumasız DEĞİL,
    /// kimlik yerine İMZA ile doğrulanır: jeton tek bir medyaya açılır, süresi vardır, tahmin edilemez ve
    /// listelenemez. Kapsam yalnız İÇERİK OKUMA — yazma/silme/listeleme yok. Bu, 2026-07-07 "dışarıya uç
    /// açma" kararının bilinçli ve dar istisnasıdır (2026-07-28 onaylı).</para>
    ///
    /// <para><b>Tenant izolasyonu KORUNUR:</b> veri filtresi devre dışı BIRAKILMAZ; jetondaki tenant kimliğiyle
    /// doğru bağlam açılır ve sorgu yine filtreli koşar. Jeton imzalı olduğu için tenant kurcalanamaz.</para>
    ///
    /// <para>Geçersiz imza, süresi dolmuş jeton ve var olmayan medya AYNI cevabı (404) döner — hangisinin
    /// olduğunu söylemek, jeton deneyen birine bilgi verirdi.</para>
    /// </summary>
    [AllowAnonymous]
    [HttpGet("link/{token}")]
    public async Task<IActionResult> GetByLinkAsync(string token)
    {
        if (_publicLink.TryResolveToken(token) is not { } target)
        {
            return NotFound();
        }

        // ŞİRKET bağlamı da açılmalı: oturumsuz istekte ICurrentCompany "hiç şirket yetkisi yok" SENTINEL'i
        // (Guid.Empty) döner ve company filtresinin tüm kolları false olur → şirkete ait medya BULUNAMAZ (404).
        // null = konsolide (şirket kısıtı yok); tenant filtresi AÇIK kalır, erişim yine jetondaki tek medyayla
        // sınırlıdır. Veri filtresi devre dışı BIRAKILMAZ — yalnız bağlam doğru kurulur.
        using (_currentTenant.Change(target.TenantId))
        using (_currentCompany.Change(null))
        {
            var media = await _repository.FindAsync(target.MediaId);
            if (media is null)
            {
                return NotFound();
            }

            var stream = await _container.GetOrNullAsync(media.BlobName);
            if (stream is null)
            {
                return NotFound();
            }

            return File(stream, media.ContentType, enableRangeProcessing: true);
        }
    }
}
