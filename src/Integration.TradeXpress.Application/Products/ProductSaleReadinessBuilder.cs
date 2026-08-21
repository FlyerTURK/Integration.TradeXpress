using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Variants;
using Microsoft.Extensions.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SATIŞA HAZIRLIK PANELİ VERİSİNİN KURUCUSU (2026-08-19) — snapshot'ı (<see cref="ProductSaleSnapshot"/>) yükler,
/// <see cref="ProductSaleValidator"/>'a yargılatır, <see cref="ProductSaleReadinessDto"/>'yu kurar.
///
/// <para><b>Satılabilirlik guard'dan okunur</b> (<see cref="VariantSaleReadinessResolver.ResolveSellableAsync"/>) —
/// rozetten (<c>SaleStatus</c>) değil. Rozet "Hazır" derken guard kapalı olabilir (reçete onaydan sonra değişti);
/// satışa hazırlık paneli tam bu farkı göstermek için var. Kanal satırının hazırlık kademesi de board ile AYNI
/// sinyallerden (<see cref="ChannelProductBoardBuilder"/> + <see cref="ChannelProductReadinessRule"/>) kurulur —
/// liste ile panel aynı kanal ürününe farklı kademe yazamaz.</para>
///
/// <para><b>Can* bayrakları burada karar verilir</b>, UI türetmez: "Durumu Yenile" yalnız batch id'si olan
/// Trendyol kaydında, "Kuyruk sonucunu sorgula" yalnız bekleyen task'ı olan N11 kaydında anlamlıdır. Kural UI'da
/// yaşasaydı kanal başına ayrı bileşende ayrışır, bir düğme sessizce yanlış kayıtta açık kalırdı.</para>
///
/// <para><b>Sahiplik:</b> ürün <c>GetOwnedAsync</c> ile (yabancı şirketin ürünü "yok" cevabı alır); kanal ürünleri
/// ve defter satırları ayrıca <c>CompanyId</c> ile daraltılır (global filtre şirketsiz bağlamda permissive'dir).</para>
/// </summary>
public class ProductSaleReadinessBuilder : ITransientDependency
{
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _detailRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11Repository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolRepository;
    private readonly IRepository<SalesChannelEtsyProduct, Guid> _etsyRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrN11ProductPushHistory, Guid> _n11HistoryRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _trendyolHistoryRepository;
    private readonly VariantSaleReadinessResolver _saleReadiness;
    private readonly ChannelProductBoardBuilder _boardBuilder;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly MarketplacePushImageResolver _pushImages;
    private readonly ProductSaleValidator _validator;
    private readonly ICurrentCompany _currentCompany;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IStringLocalizer<TradeXpressResource> _localizer;

    public ProductSaleReadinessBuilder(
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> detailRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<SalesChannelTrN11Product, Guid> n11Repository,
        IRepository<SalesChannelTrTrendyolProduct, Guid> trendyolRepository,
        IRepository<SalesChannelEtsyProduct, Guid> etsyRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<SalesChannelTrN11ProductPushHistory, Guid> n11HistoryRepository,
        IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> trendyolHistoryRepository,
        VariantSaleReadinessResolver saleReadiness,
        ChannelProductBoardBuilder boardBuilder,
        IEntityMediaAppService entityMedia,
        MarketplacePushImageResolver pushImages,
        ProductSaleValidator validator,
        ICurrentCompany currentCompany,
        IAsyncQueryableExecuter asyncExecuter,
        IStringLocalizer<TradeXpressResource> localizer)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _detailRepository = detailRepository;
        _recipeLineRepository = recipeLineRepository;
        _n11Repository = n11Repository;
        _trendyolRepository = trendyolRepository;
        _etsyRepository = etsyRepository;
        _channelRepository = channelRepository;
        _n11HistoryRepository = n11HistoryRepository;
        _trendyolHistoryRepository = trendyolHistoryRepository;
        _saleReadiness = saleReadiness;
        _boardBuilder = boardBuilder;
        _entityMedia = entityMedia;
        _pushImages = pushImages;
        _validator = validator;
        _currentCompany = currentCompany;
        _asyncExecuter = asyncExecuter;
        _localizer = localizer;
    }

    /// <summary>Yalnız snapshot + yargı (kanal satırları kurulmadan) — <see cref="ProductSaleVerifier"/> bunu
    /// çağırır: doğrulama kararı için kanal DTO'su gerekmez, ama kanal issue'ları yine üretilir (yargıyı veren
    /// sınıf aynı: <see cref="ProductSaleValidator"/>).</summary>
    public virtual async Task<ProductSaleValidationResult> ValidateAsync(Guid productId)
    {
        var product = await _productRepository.GetOwnedAsync(_currentCompany, productId);
        var imageIds = await _pushImages.ResolveCandidateMediaIdsAsync(product, ProductConsts.MaxImageCount);
        var snapshot = await LoadSnapshotAsync(product, await LoadChannelRowsAsync(product, imageIds.Count), imageIds.Count);
        return _validator.Validate(snapshot);
    }

    /// <summary><see cref="ProductSaleReadinessDto"/>: snapshot + yargı + kanal satırları.</summary>
    public virtual async Task<ProductSaleReadinessDto> BuildAsync(Guid productId)
    {
        var product = await _productRepository.GetOwnedAsync(_currentCompany, productId);
        var imageIds = await _pushImages.ResolveCandidateMediaIdsAsync(product, ProductConsts.MaxImageCount);
        var channelRows = await LoadChannelRowsAsync(product, imageIds.Count);
        var snapshot = await LoadSnapshotAsync(product, channelRows, imageIds.Count);
        var verdict = _validator.Validate(snapshot);

        var dto = new ProductSaleReadinessDto
        {
            ProductId = product.Id,
            ProductCode = product.Code,
            IsActive = product.IsActive,
            StockPolicy = product.StockPolicy,
            VariantMode = product.VariantMode,
            HasCategory = snapshot.HasCategory,
            VatRate = product.VatRate,
            ActiveVariantCount = verdict.ActiveVariantCount,
            PricedVariantCount = verdict.PricedVariantCount,
            RecipeVariantCount = verdict.RecipeVariantCount,
            SellableVariantCount = verdict.SellableVariantCount,
            StaleVerifiedVariantCount = verdict.StaleVerifiedVariantCount,
            DraftVariantCount = verdict.DraftVariantCount,
            SuspendedVariantCount = verdict.SuspendedVariantCount,
            ImageCount = snapshot.ImageCount,
            HasPoster = snapshot.HasPoster,
            CanVerify = verdict.CanVerify,
        };

        dto.Steps.AddRange(verdict.Steps);
        dto.Issues.AddRange(verdict.Issues);
        dto.Channels.AddRange(channelRows.Select(r => r.Row));

        return dto;
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<ProductSaleSnapshot> LoadSnapshotAsync(Product product, List<ChannelRowBundle> channelRows, int imageCount)
    {
        var activeVariants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == MediaEntityNames.Product && v.EntityId == product.Id && v.IsActive)
                .OrderBy(v => v.IsMain ? 0 : 1).ThenBy(v => v.Code));

        var variantIds = activeVariants.Select(v => v.Id).ToList();

        var details = variantIds.Count == 0
            ? new List<ProductVariantDetail>()
            : await _asyncExecuter.ToListAsync(
                (await _detailRepository.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId)));
        var detailByVariant = details
            .GroupBy(d => d.EntityVariantId)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = variantIds.Count == 0
            ? new List<ProductVariantRecipeLine>()
            : await _asyncExecuter.ToListAsync(
                (await _recipeLineRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));
        var linesByVariant = lines
            .GroupBy(l => l.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineOrder).ToList());

        // GUARD: satılabilirlik rozetten değil çözücüden.
        var sellable = await _saleReadiness.ResolveSellableAsync(variantIds);

        // GÖRSEL SAYIMI PUSH'UN ÇÖZÜCÜSÜNDEN OKUNUR (2026-08-21 onarımı): panel eskiden yalnız KAYIT-GENELİ
        // medyayı sayıyordu (GetPushMediaAsync("Product", ...)), oysa push MarketplacePushImageResolver ile
        // varyant → kayıt fallback'li okuyor. Fark sessiz değil GÜRÜLTÜLÜ bir yanlış alarmdı: fotoğraflarını
        // yalnız varyant panelinden ekleyen kullanıcı, gerçekte gönderilebilir bir üründe "Ürünün görseli yok"
        // uyarısını KALICI olarak görüyor ve Görseller adımı "Başlanmadı" kalıyordu.
        //
        // Aynı çözücüyü kullanmanın ikinci getirisi: panel "0 görsel" dediğinde push GERÇEKTEN ImagesRequired
        // ile düşer (aday küme boşsa iki kanal da fail-fast eder). Ters yön Trendyol'da birebir; N11'de
        // İMZALI küme aday kümenin ALT kümesidir (imza anahtarı yapılandırılmamış ortamda daha dar) — yani
        // "panelde var" N11 için "kesin gider" değil "denemeye değer" demektir; güvenli yön korunur.
        var posterMap = await _entityMedia.GetDefaultPosterMapAsync(MediaEntityNames.Product, new[] { product.Id });
        var hasPoster = !string.IsNullOrWhiteSpace(posterMap.GetValueOrDefault(product.Id));

        var variantSnapshots = activeVariants.Select(v =>
        {
            var detail = detailByVariant.GetValueOrDefault(v.Id);
            var variantLines = linesByVariant.GetValueOrDefault(v.Id) ?? new List<ProductVariantRecipeLine>();

            return new ProductSaleVariantSnapshot(
                v.Id,
                v.Code,
                detail?.SalePrice,
                // Push satırının birim kaynağıyla AYNI alan — MixedCurrency panel kuralı bunun üzerinden çalışır.
                detail?.SalePriceCurrencyUnitId,
                // Detay kaydı yoksa guard'ın gözünde "bilinmiyor" = Draft (fail-closed).
                detail?.SaleStatus ?? ProductSaleStatus.Draft,
                variantLines.Select(l => new ProductSaleRecipeLineSnapshot(
                    l.LineOrder, l.ComponentType, l.CommodityProcessType, l.Quantity, l.Amount, l.Description)).ToList());
        }).ToList();

        return new ProductSaleSnapshot(
            product.Id,
            product.Code,
            product.IsActive,
            product.StockPolicy,
            product.VariantMode,
            product.ProductCategoryId is not null,
            product.VatRate,
            product.RecipeTemplateId,
            variantSnapshots,
            sellable,
            imageCount,
            hasPoster,
            channelRows.Select(r => r.Snapshot).ToList());
    }

    // ── Kanal satırları ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanal ürünü + onun satışa hazırlık paneli satırı (DTO) + validator snapshot'ı — üçü aynı kaynaktan,
    /// tek geçişte kurulur ki "satırdaki IsPending" ile "issue'daki IsPending" ayrışamasın.</summary>
    private sealed record ChannelRowBundle(ChannelReadinessRowDto Row, ProductSaleChannelSnapshot Snapshot);

    /// <summary><paramref name="imageCount"/>: push'un GÖNDEREBİLECEĞİ görsel sayısı (varyant → kayıt
    /// fallback'li çözücüden). Satır bayrakları bunu bilmek ZORUNDA: görselsiz üründe hem N11 hem Trendyol
    /// gerçek push'ta ImagesRequired ile düşer, dolayısıyla "Gönder" düğmesini açık bırakmak kullanıcıyı
    /// kaçınılmaz bir hataya davet eder (2026-08-21 ölçümü: panel sessizken push'ta patlayan kurallar).</summary>
    private async Task<List<ChannelRowBundle>> LoadChannelRowsAsync(Product product, int imageCount)
    {
        var companyId = product.CompanyId;
        var bundles = new List<ChannelRowBundle>();

        var n11 = await _asyncExecuter.ToListAsync(
            (await _n11Repository.GetQueryableAsync())
                .Where(p => p.ProductId == product.Id && p.CompanyId == companyId));
        var trendyol = await _asyncExecuter.ToListAsync(
            (await _trendyolRepository.GetQueryableAsync())
                .Where(p => p.ProductId == product.Id && p.CompanyId == companyId));
        var etsy = await _asyncExecuter.ToListAsync(
            (await _etsyRepository.GetQueryableAsync())
                .Where(p => p.ProductId == product.Id && p.CompanyId == companyId));

        if (n11.Count == 0 && trendyol.Count == 0 && etsy.Count == 0)
        {
            return bundles;
        }

        var channelIds = n11.Select(p => p.SalesChannelId)
            .Concat(trendyol.Select(p => p.SalesChannelId))
            .Concat(etsy.Select(p => p.SalesChannelId))
            .Distinct()
            .ToList();
        var channels = (await _asyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => channelIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Code, c.Name })))
            .ToDictionary(c => c.Id);

        // Hazırlık kademesi: board sinyalleri (reçete var mı · guard'dan geçen varyant) — liste ile aynı kural.
        var board = await _boardBuilder.BuildAsync(new[] { product.Id });
        var readiness = board.TryGetValue(product.Id, out var signals)
            ? ChannelProductReadinessRule.Resolve(signals.HasRecipe, signals.ReadyVariantCount)
            : ChannelProductReadiness.NoRecipe;

        var n11LastPush = await LoadN11LastSuccessfulPushAsync(n11.Select(p => p.Id).ToList(), companyId);
        var trendyolLastPush = await LoadTrendyolLastSuccessfulPushAsync(trendyol.Select(p => p.Id).ToList(), companyId);

        foreach (var p in n11)
        {
            var channel = channels.GetValueOrDefault(p.SalesChannelId);
            var isPending = p.PendingPushTaskId != null;
            var isListed = p.Skus.Count > 0 || p.N11ProductId != null;

            var row = new ChannelReadinessRowDto
            {
                ChannelType = SalesChannelType.TrN11,
                ChannelProductId = p.Id,
                SalesChannelId = p.SalesChannelId,
                SalesChannelCode = channel?.Code ?? string.Empty,
                SalesChannelName = channel?.Name ?? string.Empty,
                IsActive = p.IsActive,
                IsListed = isListed,
                IsPending = isPending,
                StatusText = JoinStatus(p.SaleStatus, p.ApprovalStatus),
                PendingTaskId = p.PendingPushTaskId,
                BatchRequestId = null,
                LastError = p.LastError,
                LastSyncedAt = p.LastSyncedAt,
                LastPushedAt = n11LastPush.GetValueOrDefault(p.Id),
                Readiness = readiness,
                Obstacle = null,
                // Görselsiz üründe push GERÇEKTEN düşer (N11: TradeXpress:N11:Product:ImagesRequired) — düğmeyi
                // açık bırakmak kullanıcıyı kaçınılmaz hataya davet ederdi.
                CanPush = imageCount > 0,

                // PASİF kanal kaydında senkron YOK — Trendyol kolu bunu zaten yapıyordu, N11 kolu yapmıyordu.
                // Asimetrinin bedeli: kullanıcı "kaldırdım" sandığı üründe stok/fiyat yazımının sürdüğünü ancak
                // sipariş gelince fark ediyordu (2026-08-21 ölçümü).
                CanSyncStockPrice = p.Skus.Count > 0 && p.IsActive,
                CanRefreshStatus = false,
                CanResolveQueue = p.PendingPushTaskId != null,
                CanToggleArchive = false,
            };

            bundles.Add(new ChannelRowBundle(row, new ProductSaleChannelSnapshot(
                p.Id, SalesChannelType.TrN11, Label(row), p.IsActive, isListed, isPending,
                IsStale: false, p.LastError, Obstacle: null,
                MissingRequiredFields: string.IsNullOrWhiteSpace(p.CategoryExternalId))));
        }

        foreach (var p in trendyol)
        {
            var channel = channels.GetValueOrDefault(p.SalesChannelId);
            var isPending = p.BatchRequestId != null && p.Status == TrendyolProductConsts.ProcessingBatchStatus;
            var isListed = p.Skus.Count > 0;
            var isStale = p.Status == TrendyolProductConsts.StaleBatchStatus;
            var obstacle = ResolveTrendyolObstacle(p);

            var row = new ChannelReadinessRowDto
            {
                ChannelType = SalesChannelType.TrTrendyol,
                ChannelProductId = p.Id,
                SalesChannelId = p.SalesChannelId,
                SalesChannelCode = channel?.Code ?? string.Empty,
                SalesChannelName = channel?.Name ?? string.Empty,
                IsActive = p.IsActive,
                IsListed = isListed,
                IsPending = isPending,
                StatusText = p.Status,
                PendingTaskId = null,
                BatchRequestId = p.BatchRequestId,
                LastError = p.LastError,
                LastSyncedAt = p.LastSyncedAt,
                LastPushedAt = trendyolLastPush.GetValueOrDefault(p.Id),
                Readiness = readiness,
                Obstacle = obstacle,
                // Görselsiz üründe push GERÇEKTEN düşer (Trendyol: ImagesRequired) — N11 kolundaki gerekçeyle aynı.
                CanPush = imageCount > 0,
                CanSyncStockPrice = p.Skus.Count > 0 && !isPending && p.IsActive,
                CanRefreshStatus = !string.IsNullOrWhiteSpace(p.BatchRequestId),
                CanResolveQueue = false,
                CanToggleArchive = isListed,
            };

            bundles.Add(new ChannelRowBundle(row, new ProductSaleChannelSnapshot(
                p.Id, SalesChannelType.TrTrendyol, Label(row), p.IsActive, isListed, isPending,
                isStale, p.LastError, obstacle,
                MissingRequiredFields: string.IsNullOrWhiteSpace(p.CategoryId) || string.IsNullOrWhiteSpace(p.BrandId))));
        }

        foreach (var p in etsy)
        {
            var channel = channels.GetValueOrDefault(p.SalesChannelId);
            var isListed = p.EtsyListingId != null;

            var row = new ChannelReadinessRowDto
            {
                ChannelType = SalesChannelType.Etsy,
                ChannelProductId = p.Id,
                SalesChannelId = p.SalesChannelId,
                SalesChannelCode = channel?.Code ?? string.Empty,
                SalesChannelName = channel?.Name ?? string.Empty,
                IsActive = p.IsActive,
                IsListed = isListed,
                // Etsy senkron yazar (batch/kuyruk yok) → ara durum üretmez.
                IsPending = false,
                StatusText = p.ListingState,
                PendingTaskId = null,
                BatchRequestId = null,
                LastError = p.LastError,
                LastSyncedAt = p.LastSyncedAt,
                // Etsy'de delil defteri YOK (açık madde) — uydurulmaz.
                LastPushedAt = null,
                Readiness = readiness,
                Obstacle = null,
                // Etsy'nin push/senkron ucu yok; düğme açılmaz ki tıklanınca "uç yok" ile karşılaşılmasın.
                CanPush = false,
                CanSyncStockPrice = false,
                CanRefreshStatus = false,
                CanResolveQueue = false,
                CanToggleArchive = false,
            };

            bundles.Add(new ChannelRowBundle(row, new ProductSaleChannelSnapshot(
                p.Id, SalesChannelType.Etsy, Label(row), p.IsActive, isListed, IsPending: false,
                IsStale: false, p.LastError, Obstacle: null,
                // Etsy zorunlu-alan kuralı bu dilimde atlanır (kanal kendi doğrulayıcısını taşıyor).
                MissingRequiredFields: false)));
        }

        return bundles;
    }

    /// <summary>Kanal satırının okunur etiketi: "N11 · KANALKODU" — issue listesinde "hangi kanal" ilk bakışta görünsün.</summary>
    private string Label(ChannelReadinessRowDto row)
    {
        var typeName = _localizer[$"Enum:SalesChannelType:{row.ChannelType}"].Value;
        return string.IsNullOrWhiteSpace(row.SalesChannelCode) ? typeName : $"{typeName} · {row.SalesChannelCode}";
    }

    /// <summary>N11 iki ayrı durum taşır (satış + onay) ve biri diğerini anlatmaz — ikisi de gösterilir
    /// (birleşik kanal listesiyle aynı biçim).</summary>
    private static string? JoinStatus(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(secondary))
        {
            return string.IsNullOrWhiteSpace(primary) ? null : primary;
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            return secondary;
        }

        return $"{primary} / {secondary}";
    }

    /// <summary>Pazaryeri engeli — kalemler arasındaki EN AĞIR olanı; ağırlık sırası Domain çözücüsünden
    /// (<see cref="TrendyolListingObstacleResolver"/>). Metin: kanalın kendi gerekçesi, yoksa engelin lokalize adı.</summary>
    private string? ResolveTrendyolObstacle(SalesChannelTrTrendyolProduct p)
    {
        var obstacle = TrendyolListingObstacleResolver.Resolve(
            p.Skus.Any(s => s.RemoteBlacklisted == true),
            p.Skus.Any(s => s.RemoteRejected == true),
            p.Skus.Any(s => s.RemoteLocked == true),
            p.Skus.Any(s => s.RemoteArchived == true));

        if (obstacle == ChannelListingObstacle.None)
        {
            return null;
        }

        var reason = TrendyolListingObstacleResolver.ResolveReason(
            obstacle,
            p.Skus.Where(s => s.RemoteBlacklisted == true).Select(s => s.RemoteBlacklistReason).FirstOrDefault(r => r != null),
            p.Skus.Where(s => s.RemoteRejected == true).Select(s => s.RemoteRejectReason).FirstOrDefault(r => r != null),
            p.Skus.Where(s => s.RemoteLocked == true).Select(s => s.RemoteLockReason).FirstOrDefault(r => r != null));

        return string.IsNullOrWhiteSpace(reason)
            ? _localizer[$"Enum:ChannelListingObstacle:{obstacle}"].Value
            : reason;
    }

    // ── Delil defteri: son BAŞARILI gönderim ──────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, DateTime?>> LoadN11LastSuccessfulPushAsync(List<Guid> channelProductIds, Guid companyId)
    {
        if (channelProductIds.Count == 0)
        {
            return new Dictionary<Guid, DateTime?>();
        }

        var rows = await _asyncExecuter.ToListAsync(
            (await _n11HistoryRepository.GetQueryableAsync())
                .Where(h => channelProductIds.Contains(h.SalesChannelTrN11ProductId)
                            && h.CompanyId == companyId
                            && h.Outcome == ChannelPushOutcome.Succeeded)
                .GroupBy(h => h.SalesChannelTrN11ProductId)
                .Select(g => new { Id = g.Key, Last = g.Max(h => h.PushedAtUtc) }));

        // DateTime? değer: sözlükte OLMAYAN kayıt GetValueOrDefault ile null döner — DateTime olsaydı default(DateTime)
        // (0001-01-01) "son gönderim" diye DTO'ya sızardı (EF testi yakaladı).
        return rows.ToDictionary(r => r.Id, r => (DateTime?)r.Last);
    }

    private async Task<Dictionary<Guid, DateTime?>> LoadTrendyolLastSuccessfulPushAsync(List<Guid> channelProductIds, Guid companyId)
    {
        if (channelProductIds.Count == 0)
        {
            return new Dictionary<Guid, DateTime?>();
        }

        var rows = await _asyncExecuter.ToListAsync(
            (await _trendyolHistoryRepository.GetQueryableAsync())
                .Where(h => channelProductIds.Contains(h.SalesChannelTrTrendyolProductId)
                            && h.CompanyId == companyId
                            && h.Outcome == ChannelPushOutcome.Succeeded)
                .GroupBy(h => h.SalesChannelTrTrendyolProductId)
                .Select(g => new { Id = g.Key, Last = g.Max(h => h.PushedAtUtc) }));

        // DateTime? değer: sözlükte OLMAYAN kayıt GetValueOrDefault ile null döner — DateTime olsaydı default(DateTime)
        // (0001-01-01) "son gönderim" diye DTO'ya sızardı (EF testi yakaladı).
        return rows.ToDictionary(r => r.Id, r => (DateTime?)r.Last);
    }
}
