using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Scraping.N11;

/// <summary>n11 kategori sayfasından ürünleri çeken servis (TEST/demo). Sunucu tarafında Playwright (gerçek
/// Chrome) ile çalışır; görseller istemcide yüklenir. Üretim değil — kazıma demosu.</summary>
public interface IN11Scraper
{
    Task<List<N11Product>> GetCategoryAsync(string categoryUrl, int maxItems = 40, CancellationToken cancellationToken = default);
}
