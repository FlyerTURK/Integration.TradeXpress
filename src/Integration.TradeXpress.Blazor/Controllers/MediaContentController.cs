using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

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

    public MediaContentController(IRepository<Media, Guid> repository, IBlobContainer<MediaContainer> container)
    {
        _repository = repository;
        _container = container;
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
}
