using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Kanal-ürünlerinin BİRLEŞİK okuma servisi — üç pazaryerinin (N11 · Trendyol · Etsy) kanal-ürün kayıtları
/// tek listede. <c>ChannelQuestionAppService</c>'in kardeşi: kanal yalnız discriminator, kapsam
/// company-owned, sorgu merkezi <c>ApplyListRequest</c> motoruna bağlı.
///
/// <para><b>Neden ÜÇ SORGU + BELLEKTE BİRLEŞTİRME (ve bunun sınırı):</b> kanal-ürünleri satış kanallarının
/// aksine ortak bir taban entity PAYLAŞMAZ — üçü de ayrı <c>FullAuditedAggregateRoot</c>, ayrı tablo.
/// Dolayısıyla tek bir sorgu kökü yoktur. Her kaynak SQL'de kendi filtreleriyle daraltılır ve HAFİF bir
/// projeksiyonla çekilir (graf/koleksiyon materyalize EDİLMEZ, SKU'lar yalnız sayılır); arama · sıralama ·
/// sayfalama birleşmiş küme üzerinde yine MERKEZİ motorla uygulanır. <b>Bu bilinçli bir sınırdır:</b>
/// birleştirme bellekte olduğu için maliyet, şirketin kanal-ürün kayıt sayısıyla doğru orantılıdır
/// (bugün: ~10² satır). Kayıt sayısı beş haneye çıkarsa doğru çözüm üç projeksiyonun SQL <c>UNION ALL</c>'ı
/// ya da ayrı bir okuma modelidir — o gün gelene kadar SQL'e çevrilemeyen bir soyutlama uydurmak
/// erken karmaşıklık olurdu.</para>
///
/// <para><b>FAIL-CLOSED şirket kapsamı</b> (Order/ChannelQuestion deseni): global company filtresi
/// <c>CurrentCompanyId</c> null iken PERMISSIVE'dir — working company olmayan bir bağlamda (HTTP
/// yüzeyi/Swagger, arka plan işi) liste tenant'ın TÜM şirketlerinin kayıtlarını döndürürdü. Şirket
/// bağlamı yoksa BOŞ sayfa döner ve sorgular ayrıca <c>CompanyId</c> ile açıkça daraltılır.</para>
///
/// <para><b>Yazma UCU YOKTUR</b> — gerekçesi <see cref="ISalesChannelProductAppService"/> özetinde.</para>
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelProductAppService : TradeXpressAppService, ISalesChannelProductAppService
{
    /// <summary>Birleşik satırda filtre/sıralama/aramaya İZİN VERİLEN alanlar (whitelist — DTO property
    /// adları). <c>SalesChannelId</c>/<c>ProductId</c>/<c>ChannelType</c> whitelist'te YOK: bunlar tipli
    /// eksenlerdir (istekte ayrı alan) ve kolon filtresinden gelmeleri gerekmez. <c>LastError</c> de YOK —
    /// hata metni denetim alanıdır, global aramada gürültü yapar ve kısmi sunucu mesajları sızdırır.</summary>
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SalesChannelProductListDto.Id),
        nameof(SalesChannelProductListDto.SalesChannelCode),
        nameof(SalesChannelProductListDto.SalesChannelName),
        nameof(SalesChannelProductListDto.ProductCode),
        nameof(SalesChannelProductListDto.ProductName),
        nameof(SalesChannelProductListDto.ChannelProductCode),
        nameof(SalesChannelProductListDto.CategoryName),
        nameof(SalesChannelProductListDto.RemoteId),
        nameof(SalesChannelProductListDto.RemoteStatus),
        nameof(SalesChannelProductListDto.LastSyncedAt),
        nameof(SalesChannelProductListDto.SkuCount),
        nameof(SalesChannelProductListDto.IsActive),
    };

    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11Repository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolRepository;
    private readonly IRepository<SalesChannelEtsyProduct, Guid> _etsyRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly ChannelProductBoardBuilder _boardBuilder;
    private readonly ChannelCategoryPathResolver _categoryPathResolver;
    private readonly IRepository<SalesChannelTrN11ProductPushHistory, Guid> _n11HistoryRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _trendyolHistoryRepository;
    private readonly ICurrentCompany _currentCompany;

    public SalesChannelProductAppService(
        IRepository<SalesChannelTrN11Product, Guid> n11Repository,
        IRepository<SalesChannelTrTrendyolProduct, Guid> trendyolRepository,
        IRepository<SalesChannelEtsyProduct, Guid> etsyRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        ChannelProductBoardBuilder boardBuilder,
        ChannelCategoryPathResolver categoryPathResolver,
        IRepository<SalesChannelTrN11ProductPushHistory, Guid> n11HistoryRepository,
        IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> trendyolHistoryRepository,
        ICurrentCompany currentCompany)
    {
        _n11Repository = n11Repository;
        _trendyolRepository = trendyolRepository;
        _etsyRepository = etsyRepository;
        _channelRepository = channelRepository;
        _boardBuilder = boardBuilder;
        _categoryPathResolver = categoryPathResolver;
        _n11HistoryRepository = n11HistoryRepository;
        _trendyolHistoryRepository = trendyolHistoryRepository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<SalesChannelProductListDto>> GetListAsync(
        SalesChannelProductListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<SalesChannelProductListDto>(0, new List<SalesChannelProductListDto>());
        }

        var projected = new List<ProjectedRow>();
        projected.AddRange(await QueryN11Async(input, companyId));
        projected.AddRange(await QueryTrendyolAsync(input, companyId));
        projected.AddRange(await QueryEtsyAsync(input, companyId));

        // Kategori YOLU satır kurulmadan ÖNCE çözülür: yol, projeksiyondaki yaprak adının YERİNE geçer.
        // Sonra çözmek, aynı alanı iki kez yazmak (ve hangisinin kazandığını okuyana bulmacaya çevirmek) olurdu.
        await EnrichCategoryPathsAsync(projected);

        var rows = projected.Select(BuildRow).ToList();
        await EnrichChannelsAsync(rows);
        await EnrichProductsAsync(rows);

        // Türetilmiş durum filtresi zorunlu olarak BELLEKTE: SyncState saklanan bir kolon değil, üç kanalın
        // sinyallerinden çıkarılan bir karardır (bkz. ChannelProductSyncState). SQL'e itmek, aynı önceliği
        // üç ayrı WHERE'de tekrar yazmak demekti — kural değişince biri güncellenir, diğeri sessizce eski kalırdı.
        if (input.SyncState is { } syncState)
        {
            rows = rows.Where(r => r.SyncState == syncState).ToList();
        }

        // Arama/sıralama MERKEZİ motorla (elle yazılmış sıralama yok): satırlar bellekte olduğu için
        // IQueryable'a çevrilir — whitelist, tie-breaker ve savunma sınırları aynen geçerlidir.
        var query = rows.AsQueryable().ApplyListRequest(input, AllowedFields);

        var hasExplicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
        if (!hasExplicitSort)
        {
            // VARSAYILAN: KARAR BEKLEYEN ÖNDE (fiyatlandırma tahtasıyla aynı ilke) — reçetesizler başta,
            // satışa çıkamayanlar sonra, hazır olanlar en altta. Ekranın işi "şimdi ne yapmam gerekiyor"u
            // göstermek; alfabetik sıra en acil satırı listenin ortasına gömerdi. Eşitlikte kanal → ürün kodu.
            //
            // Kova hesabı ARTIK BURADA DEĞİL: enum'un sayısal sırası zaten "işi biten"e doğru gidiyor
            // (NoRecipe=0 → NotReady=1 → Ready=2). Aynı kuralı burada bir kez daha yazmak, kolon ile
            // sıralamanın birbirinden habersiz eskimesine açık kapı bırakıyordu.
            query = query
                .OrderBy(r => r.Readiness)
                .ThenBy(r => r.ChannelType)
                .ThenBy(r => r.ProductCode)
                .ThenBy(r => r.Id);
        }

        var ordered = query.ToList();

        return new PagedResultDto<SalesChannelProductListDto>(
            ordered.Count,
            ordered.ApplyPaging(input).ToList());
    }

    // ── Gönderim geçmişi (append-only delil defterinin okunuşu) ───────────────────────────────────────

    public virtual async Task<List<SalesChannelProductPushHistoryDto>> GetPushHistoryAsync(
        Guid channelProductId,
        SalesChannelType channelType)
    {
        // FAIL-CLOSED: şirket bağlamı yoksa boş — liste ucuyla aynı gerekçe (kapsamsız okuma sızdırmasın).
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<SalesChannelProductPushHistoryDto>();
        }

        var rows = channelType switch
        {
            SalesChannelType.TrN11 => await ReadN11HistoryAsync(channelProductId, companyId),
            SalesChannelType.TrTrendyol => await ReadTrendyolHistoryAsync(channelProductId, companyId),

            // Etsy'de defter HENÜZ YOK (açık madde). Boş liste döner — uydurma satır üretmek, olmayan bir
            // delili varmış gibi göstermek olurdu; ekran "kayıt yok" der ve bu DOĞRUDUR.
            _ => new List<SalesChannelProductPushHistoryDto>(),
        };

        // EN YENİ ÜSTTE: "en son ne gönderdik" en sık sorulan sorudur.
        return rows.OrderByDescending(r => r.PushedAtUtc).ThenBy(r => r.SkuCode).ToList();
    }

    private async Task<List<SalesChannelProductPushHistoryDto>> ReadN11HistoryAsync(Guid channelProductId, Guid companyId)
    {
        var query = (await _n11HistoryRepository.GetQueryableAsync())
            .Where(h => h.SalesChannelTrN11ProductId == channelProductId && h.CompanyId == companyId);

        return await AsyncExecuter.ToListAsync(query.Select(h => new SalesChannelProductPushHistoryDto
        {
            Id = h.Id,
            PushedAtUtc = h.PushedAtUtc,
            Kind = h.PushKind == N11ProductPushKind.PriceStockSync
                ? ChannelPushKind.PriceStockSync
                : ChannelPushKind.FullPush,
            Outcome = h.Outcome,
            ErrorMessage = h.ErrorMessage,
            SkuCode = h.SellerStockCode,
            SalePrice = h.SalePrice,
            // Liste fiyatı N11'de KAVRAM DEĞİL — boş bırakılır (uydurmak yanlış delil olurdu).
            ListPrice = null,
            CurrencyType = h.CurrencyType,
            Quantity = h.Quantity,
            Title = h.Title,
            RemoteReference = h.RemoteReference,
        }));
    }

    private async Task<List<SalesChannelProductPushHistoryDto>> ReadTrendyolHistoryAsync(Guid channelProductId, Guid companyId)
    {
        var query = (await _trendyolHistoryRepository.GetQueryableAsync())
            .Where(h => h.SalesChannelTrTrendyolProductId == channelProductId && h.CompanyId == companyId);

        return await AsyncExecuter.ToListAsync(query.Select(h => new SalesChannelProductPushHistoryDto
        {
            Id = h.Id,
            PushedAtUtc = h.PushedAtUtc,
            // EF ifade ağacı switch ifadesi taşıyamaz → iç içe koşul (SQL'e CASE olarak iner).
            Kind = h.PushKind == TrendyolProductPushKind.PriceStockSync
                ? ChannelPushKind.PriceStockSync
                : h.PushKind == TrendyolProductPushKind.ContentUpdate
                    ? ChannelPushKind.ContentUpdate
                    : h.PushKind == TrendyolProductPushKind.Delete
                        ? ChannelPushKind.Delete
                        : ChannelPushKind.FullPush,
            Outcome = h.Outcome,
            ErrorMessage = h.ErrorMessage,
            SkuCode = h.Barcode,
            SalePrice = h.SalePrice,
            ListPrice = h.ListPrice,
            // Para birimi Trendyol kaydında taşınmaz (kanal tek para birimiyle çalışır) → null.
            CurrencyType = null,
            Quantity = h.Quantity,
            Title = h.Title,
            RemoteReference = h.BatchRequestId,
        }));
    }

    // ── Kaynak sorguları (her biri SQL'de daraltılır; graf materyalize edilmez) ────────────────────────

    private async Task<List<ProjectedRow>> QueryN11Async(SalesChannelProductListRequestDto input, Guid companyId)
    {
        if (!WantsChannel(input, SalesChannelType.TrN11))
        {
            return new List<ProjectedRow>();
        }

        var query = (await _n11Repository.GetQueryableAsync())
            .Where(p => p.CompanyId == companyId);

        if (input.SalesChannelId is { } channelId)
        {
            query = query.Where(p => p.SalesChannelId == channelId);
        }

        if (input.ProductId is { } productId)
        {
            query = query.Where(p => p.ProductId == productId);
        }

        return await AsyncExecuter.ToListAsync(query.Select(p => new ProjectedRow
        {
            Id = p.Id,
            SalesChannelId = p.SalesChannelId,
            ChannelType = SalesChannelType.TrN11,
            ProductId = p.ProductId,
            ChannelProductCode = p.SellerCode,
            CategoryName = p.CategoryName,
            CategoryExternalId = p.CategoryExternalId,
            RemoteNumericId = p.N11ProductId,
            RemoteTextId = null,
            RemoteStatus = p.SaleStatus,
            RemoteStatusSecondary = p.ApprovalStatus,
            // N11'in bekleyen push task'ı: akıbet belirsiz (kuyrukta) — Pending'in TEK kaynağı.
            IsPending = p.PendingPushTaskId != null,
            HasOurPush = p.LastSyncedAt != null,
            RemotePrice = null,
            RemoteOnSale = null,
            LastSyncedAt = p.LastSyncedAt,
            LastError = p.LastError,
            ChannelPrice = p.Skus.Where(s => s.LastSentOptionPrice != null).Min(s => s.LastSentOptionPrice),
            ChannelPriceMax = p.Skus.Where(s => s.LastSentOptionPrice != null).Max(s => s.LastSentOptionPrice),
            // Sum BOŞ kümede 0 döner → "hiç göndermedik" ekranda "tükendi" diye okunurdu (bilgisizlik ≠ beyan).
            // Bu yüzden önce VARLIK sorulur; testle çivili (Channel_price_and_quantity_are_null_when_...).
            ChannelQuantity = p.Skus.Any(s => s.LastSentQuantity != null)
                ? p.Skus.Where(s => s.LastSentQuantity != null).Sum(s => s.LastSentQuantity)
                : null,
            SkuCount = p.Skus.Count,
            IsActive = p.IsActive,
        }));
    }

    private async Task<List<ProjectedRow>> QueryTrendyolAsync(SalesChannelProductListRequestDto input, Guid companyId)
    {
        if (!WantsChannel(input, SalesChannelType.TrTrendyol))
        {
            return new List<ProjectedRow>();
        }

        var query = (await _trendyolRepository.GetQueryableAsync())
            .Where(p => p.CompanyId == companyId);

        if (input.SalesChannelId is { } channelId)
        {
            query = query.Where(p => p.SalesChannelId == channelId);
        }

        if (input.ProductId is { } productId)
        {
            query = query.Where(p => p.ProductId == productId);
        }

        return await AsyncExecuter.ToListAsync(query.Select(p => new ProjectedRow
        {
            Id = p.Id,
            SalesChannelId = p.SalesChannelId,
            ChannelType = SalesChannelType.TrTrendyol,
            ProductId = p.ProductId,
            ChannelProductCode = p.ProductMainId,
            CategoryName = p.CategoryName,
            CategoryExternalId = p.CategoryId,
            RemoteNumericId = null,
            RemoteTextId = p.RemoteProductMainId,
            RemoteStatus = p.Status,
            RemoteStatusSecondary = null,
            // Trendyol asenkron yazar: batch açıldı ama SONUCU henüz alınmadıysa (Status=PROCESSING) akıbet
            // belirsizdir. Eski ölçüt "uzak kimlik yok" idi — o kimlik yalnız İMPORT'la dolar; kendi push'umuzla
            // açılan ürün batch COMPLETED olsa bile import edilmedikçe sonsuza dek "Bekliyor" görünüyordu
            // (2026-08-16 ilk canlı gönderim: DB COMPLETED, liste Bekliyor — Hakan tespiti).
            IsPending = p.BatchRequestId != null && p.Status == "PROCESSING",
            HasOurPush = p.LastSyncedAt != null,
            RemotePrice = p.ListPrice,
            RemoteOnSale = p.RemoteOnSale,
            // Pazaryerinin ENGEL beyanı — bu alanlar yanıtta hep vardı, hiç okunmuyordu ve reddedilen bir
            // gönderimin sebebi hiçbir ekranda görünmüyordu.
            ObstacleBlacklisted = p.Skus.Any(s => s.RemoteBlacklisted == true),
            ObstacleRejected = p.Skus.Any(s => s.RemoteRejected == true),
            ObstacleLocked = p.Skus.Any(s => s.RemoteLocked == true),
            ObstacleArchived = p.Skus.Any(s => s.RemoteArchived == true),
            ObstacleBlacklistReason = p.Skus.Where(s => s.RemoteBlacklisted == true && s.RemoteBlacklistReason != null)
                .Select(s => s.RemoteBlacklistReason).FirstOrDefault(),
            ObstacleRejectReason = p.Skus.Where(s => s.RemoteRejected == true && s.RemoteRejectReason != null)
                .Select(s => s.RemoteRejectReason).FirstOrDefault(),
            ObstacleLockReason = p.Skus.Where(s => s.RemoteLocked == true && s.RemoteLockReason != null)
                .Select(s => s.RemoteLockReason).FirstOrDefault(),
            CampaignKnown = p.Skus.Any(s => s.RemoteHasActiveCampaign != null),
            CampaignActive = p.Skus.Any(s => s.RemoteHasActiveCampaign == true),
            RemoteUrl = p.Skus.Where(s => s.RemoteProductUrl != null).Select(s => s.RemoteProductUrl).FirstOrDefault(),
            RemoteUpdatedAt = p.Skus.Max(s => s.RemoteUpdatedAtUtc),
            RemoteCreatedAt = p.Skus.Min(s => s.RemoteCreatedAtUtc),
            LastSyncedAt = p.LastSyncedAt,
            // Batch kısmen başarısız olabilir: hata METNİ olmasa da başarısız kalem sayısı hatanın kendisidir.
            LastError = p.LastError,
            FailedItemCount = p.FailedItemCount,
            // İKİ KAYNAK, BELİRLİ ÖNCELİK: önce BİZİM son başarılı gönderimimiz (LastSent*), o yoksa
            // PAZARYERİNİN İMPORT ANINDAKİ BEYANI (Remote*). Gerekçe: push, listelemeyi en son değiştiren
            // yazma işlemidir; ama hiç push edilmemiş kayıtta (canlıda 224/224 böyle) tek cevap import'tur
            // ve o cevabı atmak, elimizde dururken kolonu boş bırakmak olurdu.
            // ⚠ Bilinen sınır: push'tan SONRA yapılan bir import Remote*'u tazeler ama LastSent* öncelikli
            // kaldığı için görünmez. SKU başına zaman damgası olmadan hangisinin yeni olduğu bilinemez;
            // tahmin etmektense sabit ve açıklanabilir bir öncelik seçildi.
            // SIFIR FIYAT = FIYAT DEGIL (2026-08-10): Trendyol pasif/onaysiz kalemlerde salePrice 0 dondurur.
            // Sifiri gecerli fiyat saymak kolonu "0,00" ile doldurup gercek fiyati GIZLERDI; bu yuzden yalniz
            // POZITIF SKU fiyati kullanilir, yoksa urun seviyesindeki pazaryeri anlik goruntusune (ListPrice)
            // dusulur - o alan canlida 224/224 dolu ve gercek fiyati tasiyan tek kaynak.
            ChannelPrice = p.Skus.Any(s => s.LastSentSalePrice > 0m)
                ? p.Skus.Where(s => s.LastSentSalePrice > 0m).Min(s => s.LastSentSalePrice)
                : (p.Skus.Any(s => s.RemoteSalePrice > 0m)
                    ? p.Skus.Where(s => s.RemoteSalePrice > 0m).Min(s => s.RemoteSalePrice)
                    : p.ListPrice),
            ChannelPriceMax = p.Skus.Any(s => s.LastSentSalePrice > 0m)
                ? p.Skus.Where(s => s.LastSentSalePrice > 0m).Max(s => s.LastSentSalePrice)
                : (p.Skus.Any(s => s.RemoteSalePrice > 0m)
                    ? p.Skus.Where(s => s.RemoteSalePrice > 0m).Max(s => s.RemoteSalePrice)
                    : p.ListPrice),
            // Sum BOŞ kümede 0 döner → "hiç göndermedik" ekranda "tükendi" diye okunurdu (bilgisizlik ≠ beyan).
            // Bu yüzden her iki kaynakta da önce VARLIK sorulur; testle çivili.
            ChannelQuantity = p.Skus.Any(s => s.LastSentQuantity != null)
                ? p.Skus.Where(s => s.LastSentQuantity != null).Sum(s => s.LastSentQuantity)
                : (p.Skus.Any(s => s.RemoteQuantity != null)
                    ? p.Skus.Where(s => s.RemoteQuantity != null).Sum(s => s.RemoteQuantity)
                    : null),
            SkuCount = p.Skus.Count,
            IsActive = p.IsActive,
        }));
    }

    private async Task<List<ProjectedRow>> QueryEtsyAsync(SalesChannelProductListRequestDto input, Guid companyId)
    {
        if (!WantsChannel(input, SalesChannelType.Etsy))
        {
            return new List<ProjectedRow>();
        }

        var query = (await _etsyRepository.GetQueryableAsync())
            .Where(p => p.CompanyId == companyId);

        if (input.SalesChannelId is { } channelId)
        {
            query = query.Where(p => p.SalesChannelId == channelId);
        }

        if (input.ProductId is { } productId)
        {
            query = query.Where(p => p.ProductId == productId);
        }

        var rows = await AsyncExecuter.ToListAsync(query.Select(p => new ProjectedRow
        {
            Id = p.Id,
            SalesChannelId = p.SalesChannelId,
            ChannelType = SalesChannelType.Etsy,
            ProductId = p.ProductId,
            ChannelProductCode = p.SellerSkuBase,
            // Etsy taksonomi ADI kanal kaydında taşınmaz (yalnız id) — uydurulmaz, boş bırakılır.
            // Yol çözücüsü taksonomi ağacından adı GETİREBİLİR; getiremezse hücre boş kalır.
            CategoryName = null,
            ChannelPrice = p.Skus.Where(s => s.LastSentPrice != null).Min(s => s.LastSentPrice),
            ChannelPriceMax = p.Skus.Where(s => s.LastSentPrice != null).Max(s => s.LastSentPrice),
            // Sum BOŞ kümede 0 döner → "hiç göndermedik" ekranda "tükendi" diye okunurdu (bilgisizlik ≠ beyan).
            // Bu yüzden önce VARLIK sorulur; testle çivili (Channel_price_and_quantity_are_null_when_...).
            ChannelQuantity = p.Skus.Any(s => s.LastSentQuantity != null)
                ? p.Skus.Where(s => s.LastSentQuantity != null).Sum(s => s.LastSentQuantity)
                : null,
            CategoryTaxonomyId = p.TaxonomyId,
            RemoteNumericId = p.EtsyListingId,
            RemoteTextId = null,
            RemoteStatus = p.ListingState,
            RemoteStatusSecondary = null,
            // Etsy senkron yazar (batch/kuyruk yok) → ara durum ÜRETMEZ.
            IsPending = false,
            HasOurPush = p.LastSyncedAt != null,
            RemotePrice = null,
            RemoteOnSale = null,
            LastSyncedAt = p.LastSyncedAt,
            LastError = p.LastError,
            SkuCount = p.Skus.Count,
            IsActive = p.IsActive,
        }));

        // Sayısal taksonomi id'si metin anahtara çevrilir (ağaçta ExternalId metindir). SQL'de değil burada:
        // sağlayıcının long?.ToString() çevirisine bel bağlamamak için.
        foreach (var row in rows)
        {
            row.CategoryExternalId = row.CategoryTaxonomyId?.ToString(CultureInfo.InvariantCulture);
        }

        return rows;
    }

    private static bool WantsChannel(SalesChannelProductListRequestDto input, SalesChannelType type)
    {
        return input.ChannelType is null || input.ChannelType == type;
    }

    // ── Satır kurulumu (projeksiyon-satırı → DTO; entity→DTO eşlemesi DEĞİL) ──────────────────────────

    /// <summary>Projeksiyon satırını grid satırına çevirir. <b>Mapperly kullanılmaz</b> çünkü bu bir eşleme
    /// değil TÜRETMEdir: üç farklı kaynağın sinyalleri tek nötr duruma indirgenir ve uzak kimlik iki ayrı
    /// tipten (sayısal/metin) tek metne çözülür. Konvansiyon testi de projeksiyon-satırı→DTO dönüşümünü
    /// bilerek kapsam dışı bırakır (kaynak bir <c>IEntity</c> değildir).</summary>
    private SalesChannelProductListDto BuildRow(ProjectedRow source)
    {
        return new SalesChannelProductListDto
        {
            Id = source.Id,
            SalesChannelId = source.SalesChannelId,
            ChannelType = source.ChannelType,
            ProductId = source.ProductId,
            ChannelProductCode = source.ChannelProductCode,
            CategoryName = source.CategoryName,
            RemoteId = ResolveRemoteId(source),
            RemoteStatus = ResolveRemoteStatus(source),
            SyncState = ResolveSyncState(source),
            LastSyncedAt = source.LastSyncedAt,
            ChannelPrice = source.ChannelPrice,
            ChannelPriceMax = source.ChannelPriceMax,
            ChannelQuantity = source.ChannelQuantity,
            RemotePrice = source.RemotePrice,
            RemoteOnSale = source.RemoteOnSale,
            Obstacle = ResolveObstacle(source),
            ObstacleReason = ResolveObstacleReason(source),
            HasActiveCampaign = source.CampaignKnown ? source.CampaignActive : null,
            RemoteUrl = source.RemoteUrl,
            RemoteUpdatedAt = source.RemoteUpdatedAt,
            RemoteCreatedAt = source.RemoteCreatedAt,
            LastError = source.LastError,
            SkuCount = source.SkuCount,
            IsActive = source.IsActive,
        };
    }

    /// <summary>Kaydın PAZARYERİ ENGELİ — kalemleri arasındaki EN AĞIR olanı. SQL projeksiyonu bayrakları
    /// <c>Skus.Any(...)</c> ile TOPLAR (entity'ye erişemez); ağırlık SIRASI ise Domain'in tek çözücüsünden
    /// (<see cref="TrendyolListingObstacleResolver"/>) gelir — burada ikinci kez yazılmaz.</summary>
    private static ChannelListingObstacle ResolveObstacle(ProjectedRow source)
    {
        return TrendyolListingObstacleResolver.Resolve(
            source.ObstacleBlacklisted, source.ObstacleRejected, source.ObstacleLocked, source.ObstacleArchived);
    }

    /// <summary>Engelin gerekçesi — KANALIN kendi cümlesi; eşleme Domain çözücüsünde. Engel var ama gerekçe
    /// bildirilmemişse boş kalır: engelin varlığı ile gerekçesi ayrı sorulardır ve gerekçe uydurulmaz.</summary>
    private static string? ResolveObstacleReason(ProjectedRow source)
    {
        return TrendyolListingObstacleResolver.ResolveReason(
            ResolveObstacle(source), source.ObstacleBlacklistReason, source.ObstacleRejectReason, source.ObstacleLockReason);
    }

    /// <summary>Uzak kimlik metni — sayısal (N11/Etsy) ya da metin (Trendyol). İkisi de boşsa
    /// pazaryerinde karşılığı yoktur.</summary>
    private static string? ResolveRemoteId(ProjectedRow source)
    {
        if (source.RemoteNumericId is { } numeric)
        {
            return numeric.ToString(CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(source.RemoteTextId) ? null : source.RemoteTextId;
    }

    /// <summary>Ham kanal durumu. N11 iki ayrı durum taşır (satış + onay) ve biri diğerini anlatmaz —
    /// "satışta ama onay bekliyor" gerçek bir durumdur → ikisi de gösterilir.</summary>
    private static string? ResolveRemoteStatus(ProjectedRow source)
    {
        if (string.IsNullOrWhiteSpace(source.RemoteStatusSecondary))
        {
            return string.IsNullOrWhiteSpace(source.RemoteStatus) ? null : source.RemoteStatus;
        }

        if (string.IsNullOrWhiteSpace(source.RemoteStatus))
        {
            return source.RemoteStatusSecondary;
        }

        return $"{source.RemoteStatus} / {source.RemoteStatusSecondary}";
    }

    /// <summary>Nötr senkron durumu — ÖNCELİK ÜÇ KANALDA DA AYNI: hata → bekliyor → gönderildi → gönderilmedi.
    /// Gerekçe <see cref="ChannelProductSyncState"/> özetinde (liste "elimi bekleyen satır" listesidir).</summary>
    private static ChannelProductSyncState ResolveSyncState(ProjectedRow source)
    {
        if (!string.IsNullOrWhiteSpace(source.LastError) || source.FailedItemCount > 0)
        {
            return ChannelProductSyncState.Failed;
        }

        if (source.IsPending)
        {
            return ChannelProductSyncState.Pending;
        }

        // "GÖNDERİLDİ" ANCAK BİZİM GÖNDERDİĞİMİZİN KANITI VARSA (2026-08-10 düzeltmesi). Eskiden ölçüt
        // uzak kimliğin varlığıydı; oysa içe aktarılan kaydın kimliği ithal anında dolduğundan hiç
        // göndermediğimiz ürünler "Gönderildi" görünüyordu — canlıda 5 Trendyol kaydının tamamı öyleydi
        // (RemoteProductMainId dolu, LastSyncedAt NULL). Kimlik "orada var" der, damga "biz yazdık" der.
        if (source.HasOurPush)
        {
            return ChannelProductSyncState.Sent;
        }

        var hasRemote = source.RemoteNumericId is not null || !string.IsNullOrWhiteSpace(source.RemoteTextId);

        // Pazaryerinde var ama biz göndermedik → içe aktarılmış. "Gönderilmedi" demek eksik olurdu:
        // ürün orada canlı ve sipariş alabiliyor, yalnız bizim yönetimimize bağlı değil.
        return hasRemote ? ChannelProductSyncState.Imported : ChannelProductSyncState.NotSent;
    }

    // ── Zenginleştirme (id-only referanslar; TEK batch — satır başına sorgu YOK) ──────────────────────

    private async Task EnrichChannelsAsync(List<SalesChannelProductListDto> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var channelIds = rows.Select(r => r.SalesChannelId).Distinct().ToList();
        var channels = await AsyncExecuter.ToListAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => channelIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Code, c.Name }));

        var byId = channels.ToDictionary(c => c.Id);
        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.SalesChannelId, out var channel))
            {
                row.SalesChannelCode = channel.Code;
                row.SalesChannelName = channel.Name;
            }
        }
    }

    /// <summary>Ürün kimliği + TAHTA sinyalleri (görsel, varyant sayısı, reçete, satışa hazır) — hepsi
    /// fiyatlandırma tahtasının kullandığı <see cref="ChannelProductBoardBuilder"/>'dan.
    ///
    /// <para><b>Neden ayrı sorgu değil:</b> tahta bu dört sinyali dört TOPLU sorguyla üretiyor ve
    /// satılabilirlik kuralını tek yerde tutuyor. Burada ürün kodu/adını ayrıca çekip sinyalleri başka
    /// türlü hesaplamak, aynı kuralın ikinci bir kopyasını doğururdu: kural değişince biri güncellenir,
    /// diğeri sessizce eski kalırdı — ve fark ancak "bu ürün neden push edilmiyor?" diye sorulunca görülürdü.</para>
    ///
    /// <para>Eşleşmeyen satır ELENMEZ: öksüz kanal kaydı (ürünü silinmiş) gizlenecek değil GÖRÜNECEK bir
    /// sorundur — listeden düşürmek onu bulunamaz hâle getirirdi; alanları boş kalır.</para></summary>
    private async Task EnrichProductsAsync(List<SalesChannelProductListDto> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var board = await _boardBuilder.BuildAsync(productIds);

        foreach (var row in rows)
        {
            if (!board.TryGetValue(row.ProductId, out var signals))
            {
                continue;
            }

            row.ProductCode = signals.ProductCode;
            row.ProductName = signals.ProductName;
            row.ImageUrl = signals.ImageUrl;
            row.VariantCount = signals.VariantCount;
            row.HasRecipe = signals.HasRecipe;
            row.ReadyVariantCount = signals.ReadyVariantCount;

            // Türetilenler ALAN olarak taşınır: grid ancak veri alanı olan kolonu gruplayabilir/sıralayabilir.
            row.Readiness = ResolveReadiness(signals.HasRecipe, signals.ReadyVariantCount);
            row.HasImage = !string.IsNullOrWhiteSpace(signals.ImageUrl);
        }
    }

    /// <summary>Hazırlık kademesini TEK yerde karar verir — hem kolon hem varsayılan sıralama bunu okur.
    /// Kural iki yerde yaşasaydı biri değişince diğeri sessizce eskirdi.</summary>
    private static ChannelProductReadiness ResolveReadiness(bool hasRecipe, int readyVariantCount)
    {
        if (!hasRecipe)
        {
            return ChannelProductReadiness.NoRecipe;
        }

        return readyVariantCount == 0 ? ChannelProductReadiness.NotReady : ChannelProductReadiness.Ready;
    }

    /// <summary>
    /// Kategori hücresini YAPRAK adından KÖKTEN TAM YOLA yükseltir ("Kozmetik &gt; Cilt Bakımı &gt; Göz
    /// Makyaj Temizleyici"). Gerekçe <see cref="ChannelCategoryPathResolver"/> özetinde.
    ///
    /// <para><b>Kanal tipine göre GRUPLANIR:</b> her ağaç ayrı tablodur, tek sorguda birleşmezler. Gruplama
    /// aynı zamanda N+1'i keser — kanal başına tek çözüm turu, satır başına değil.</para>
    ///
    /// <para><b>Çözülemeyen id'de yaprak adı KORUNUR</b> (üzerine yazılmaz): pazaryeri kategoriyi kaldırmış
    /// olabilir ve elde duran doğru bilgiyi silmek, eksik bilgiyi göstermekten kötüdür.</para>
    /// </summary>
    private async Task EnrichCategoryPathsAsync(List<ProjectedRow> projected)
    {
        var groups = projected
            .Where(r => !string.IsNullOrWhiteSpace(r.CategoryExternalId))
            .GroupBy(r => r.ChannelType);

        foreach (var group in groups)
        {
            var externalIds = group
                .Select(r => r.CategoryExternalId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var paths = await _categoryPathResolver.ResolveAsync(group.Key, externalIds);
            if (paths.Count == 0)
            {
                continue;
            }

            foreach (var row in group)
            {
                if (paths.TryGetValue(row.CategoryExternalId!, out var path) && !string.IsNullOrWhiteSpace(path))
                {
                    row.CategoryName = path;
                }
            }
        }
    }

    /// <summary>Üç kaynağın ORTAK projeksiyon satırı — SQL'den yalnız bu alanlar çekilir (graf/koleksiyon
    /// materyalize edilmez). Kanal-özel sinyaller burada NÖTR adlar altında birleşir; nihai durum kararını
    /// <see cref="ResolveSyncState"/> tek yerde verir.</summary>
    private sealed class ProjectedRow
    {
        public Guid Id { get; set; }
        public Guid SalesChannelId { get; set; }
        public SalesChannelType ChannelType { get; set; }
        public Guid ProductId { get; set; }
        public string? ChannelProductCode { get; set; }

        /// <summary>Kanal kaydının dondurduğu YAPRAK adı; <see cref="EnrichCategoryPathsAsync"/> çözebilirse
        /// kökten tam yolla DEĞİŞTİRİLİR, çözemezse (bayat id) olduğu gibi kalır.</summary>
        public string? CategoryName { get; set; }

        /// <summary>Kanalın kategori ağacındaki yaprak id'si (N11 <c>CategoryExternalId</c> · Trendyol
        /// <c>CategoryId</c> · Etsy <c>TaxonomyId</c>). Tam yol bunun üzerinden yürünür.</summary>
        public string? CategoryExternalId { get; set; }

        /// <summary>Etsy taksonomi id'si SAYISAL saklanır. SQL projeksiyonunda metne çevirmek yerine ham
        /// çekilir ve bellekte çevrilir — sağlayıcının <c>ToString()</c> çevirisine bağımlılık kurmamak için.</summary>
        public long? CategoryTaxonomyId { get; set; }

        /// <summary>Uzak kimliğin sayısal biçimi (N11 ürün id · Etsy listing id).</summary>
        public long? RemoteNumericId { get; set; }

        /// <summary>Uzak kimliğin metin biçimi (Trendyol ana ürün kodu).</summary>
        public string? RemoteTextId { get; set; }

        public string? RemoteStatus { get; set; }

        /// <summary>İkinci durum metni — yalnız N11 (onay durumu).</summary>
        public string? RemoteStatusSecondary { get; set; }

        /// <summary>Gönderim yolda ve akıbeti belirsiz.</summary>
        public bool IsPending { get; set; }

        /// <summary>BİZİM başarılı gönderimimizin kanıtı (senkron damgası). Uzak kimlikten AYRI tutulur:
        /// kimlik içe aktarımda da dolar, bu yalnız gerçekten gönderdiğimizde.</summary>
        public bool HasOurPush { get; set; }

        /// <summary>KANALA ULAŞMIŞ son fiyat (SKU'ların <c>LastSent*</c> alt/üst ucu) ve toplam adet. Yalnız
        /// BAŞARILI gönderimde terfi ettikleri için "ulaştığını sandığımız" değil GERÇEKTEN ulaşan değerdir.
        /// Üç kanalın alan adları farklı (N11 <c>LastSentOptionPrice</c> · Trendyol <c>LastSentSalePrice</c> ·
        /// Etsy <c>LastSentPrice</c>) → burada nötr ada indirgenir.</summary>
        public decimal? ChannelPrice { get; set; }

        public decimal? ChannelPriceMax { get; set; }

        public int? ChannelQuantity { get; set; }

        /// <summary>Pazaryerinde gösterilen fiyat (yalnız Trendyol taşır).</summary>
        public decimal? RemotePrice { get; set; }

        /// <summary>Pazaryerinde satışta mı (yalnız Trendyol taşır; null = bilinmiyor).</summary>
        public bool? RemoteOnSale { get; set; }

        // ── PAZARYERİ ENGELİ (yalnız Trendyol beyan ediyor) ────────────────────────────────────────────
        // Bayraklar KAYIT seviyesinde değil SKU seviyesinde yaşar; buraya "en az bir kalemde var mı" olarak
        // indirgenirler. Tek kalemi karalistede olan kayıt "engelsiz" sayılamaz — o kalem satılamıyorsa
        // kullanıcının haberi olmalıdır. Gerekçe, engeli TAŞIYAN ilk kalemden alınır.
        public bool ObstacleBlacklisted { get; set; }
        public bool ObstacleRejected { get; set; }
        public bool ObstacleLocked { get; set; }
        public bool ObstacleArchived { get; set; }

        public string? ObstacleBlacklistReason { get; set; }
        public string? ObstacleRejectReason { get; set; }
        public string? ObstacleLockReason { get; set; }

        /// <summary>Kampanya bilgisi HİÇ bildirildi mi — "bilinmiyor" ile "kampanya yok" ayrımı bu ikili
        /// üzerinden kurulur; tek bool ile ikisi ayırt edilemezdi.</summary>
        public bool CampaignKnown { get; set; }
        public bool CampaignActive { get; set; }

        public string? RemoteUrl { get; set; }
        public DateTime? RemoteUpdatedAt { get; set; }
        public DateTime? RemoteCreatedAt { get; set; }

        public DateTime? LastSyncedAt { get; set; }
        public string? LastError { get; set; }

        /// <summary>Trendyol batch'inin başarısız kalem sayısı — hata metni olmasa da hatadır.</summary>
        public int? FailedItemCount { get; set; }

        public int SkuCount { get; set; }
        public bool IsActive { get; set; }
    }
}
