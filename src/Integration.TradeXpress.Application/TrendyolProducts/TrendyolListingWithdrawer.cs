using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// KANAL ÜRÜNÜNÜ TRENDYOL'DAN DA KALDIRIR — "Sil" artık yalnız bizdeki kaydı değil pazaryerindeki listing'i de
/// düşürür (2026-08-16 Hakan kararı: <i>"hem sistemimizde hem Trendyol'dan kaldırılsın; önce DB sonra Trendyol;
/// Trendyol'dan silinemezse DB geri dönsün"</i>).
///
/// <para><b>Sıra ve geri dönüş:</b> çağıran ÖNCE DB grafını soft-delete eder, SONRA burası Trendyol'a delete gönderir —
/// ikisi AYNI unit-of-work'tedir; Trendyol HTTP'de reddederse (istisna) UoW rollback eder ve DB kaydı kendiliğinden
/// geri gelir. Yani "DB önce" ile "silinemezse geri dön" tek mekanizmadan çıkar, ikinci bir telafi kodu yoktur.</para>
///
/// <para><b>Ne zaman Trendyol'a gidilir:</b> yalnız kanala FİİLEN ULAŞMIŞ kayıtta — barkodu bilinen SKU'su olan
/// (kendi push'umuz ya da import). Hiç gönderilmemiş/içe aktarılmamış kayıtta pazaryerinde silinecek şey yoktur;
/// oraya çağrı yapmak anlamsız bir red üretirdi.</para>
///
/// <para><b>Trendyol kısıtı (resmî doküman):</b> yalnız ONAY BEKLEYEN ürünler ile bir günden eski ARŞİVLENMİŞ ürünler
/// silinir; onaylı/satıştaki ürün doğrudan silinemez (önce arşiv). HTTP kabulü asenkron batch'tir — kabul "kanal
/// aldı" demektir, nihai akıbet batch sonucudur. Kayıt bizde silindiği için o sonucu sonradan sorgulayacak bir
/// işçi yoktur; bu yüzden gönderim anı DELİL DEFTERİNE <c>Delete</c> türüyle yazılır (batch id ile) — silinen
/// kaydın kanaldaki akıbetini araştırmanın tek izi bu satırdır.</para>
/// </summary>
public class TrendyolListingWithdrawer : ITransientDependency
{
    private readonly ITrendyolProductClient _client;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly TrendyolPushHistoryRecorder _historyRecorder;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TrendyolListingWithdrawer(
        ITrendyolProductClient client,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        TrendyolPushHistoryRecorder historyRecorder,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _client = client;
        _channelRepository = channelRepository;
        _historyRecorder = historyRecorder;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>Kaydın barkodlarını Trendyol'dan siler (kanala hiç ulaşmamışsa no-op). DB grafı silindikten SONRA,
    /// aynı UoW içinde çağrılır — istisna DB'yi geri alır. Delil satırı yazılır (başarı da red de).</summary>
    public virtual Task WithdrawAsync(SalesChannelTrTrendyolProduct entity)
    {
        return SendAsync(entity, TrendyolProductPushKind.Delete,
            (barcodes, credentials) => _client.DeleteProductsAsync(barcodes, credentials));
    }

    /// <summary>
    /// Kanaldaki ARŞİV durumunu bizim <c>IsActive</c>'e eşitler (2026-08-17 Hakan kararı: "IsActive bunun eşleniği
    /// olsun"): pasif → Trendyol'da arşive (satıştan çekilir, silinmez); aktif → arşivden çıkar (yeniden satışa).
    /// Kanala hiç ulaşmamış kayıtta no-op. Çağıran bayrağı yazdıktan SONRA, aynı UoW'da çağırır — Trendyol reddederse
    /// bayrak değişimi geri döner (kanal ile biz farklı şey söyleyemeyiz).
    /// </summary>
    public virtual Task SetArchivedAsync(SalesChannelTrTrendyolProduct entity, bool archived)
    {
        return SendAsync(entity, archived ? TrendyolProductPushKind.Archive : TrendyolProductPushKind.Unarchive,
            (barcodes, credentials) => _client.ArchiveProductsAsync(barcodes, archived, credentials));
    }

    // Ortak SendAsync: barkodlar → kanal çağrısı → PushHistory satırı (başarı/red) → red'de istisna yukarı (UoW rollback).
    // Red satırı AYRI requiresNew transaction'da yazılır ki çağıranın rollback'i delili de silmesin (aşağıdaki metot).
    private async Task SendAsync(
        SalesChannelTrTrendyolProduct entity,
        TrendyolProductPushKind kind,
        Func<IReadOnlyList<string>, TrendyolCredentials, Task<TrendyolSubmitResult>> send)
    {
        var barcodes = entity.Skus.Select(s => s.Barcode).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
        if (barcodes.Count == 0)
        {
            return;   // pazaryerine hiç ulaşmamış kayıt — kanalda dokunulacak listing yok
        }

        var channel = await _channelRepository.GetAsync(entity.SalesChannelId);
        var credentials = new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
        var entries = barcodes
            .Select(b => new TrendyolPushHistoryEntry(b, null, null, null, null, null, null))
            .ToList();

        try
        {
            var result = await send(barcodes, credentials);
            await _historyRecorder.RecordAsync(
                entity.CompanyId, entity.Id, kind, entries, result.BatchRequestId, ChannelPushOutcome.Succeeded);
        }
        catch (Exception ex)
        {
            // Red de deftere yazılır (defter yalnız başarıyı yazsaydı "denendi, kanal reddetti" ile "hiç denenmedi"
            // ayırt edilemezdi — 2026-08-10 kuralı); ardından istisna yukarı: çağıranın UoW'u DB değişimini geri alır.
            // Tipli hatada mesaj ABP'nin jenerik cümlesi olurdu — kod daha bilgilendiricidir (N11 ile aynı tercih).
            await RecordFailureSurvivingRollbackAsync(
                entity.CompanyId, entity.Id, kind, entries,
                ex is BusinessException { Code: { Length: > 0 } code } ? code : ex.Message);
            throw;
        }
    }

    /// <summary>
    /// BAŞARISIZ delil satırı AYRI (requiresNew) transaction'da yazılır — dıştaki UoW rollback olsa bile KALIR.
    ///
    /// <para><b>Neden (2026-08-21 hakem bulgusu — <c>N11StockWithdrawer</c> ile aynı gün düzeltilen aynı kusur):</b>
    /// iki giriş de (silme yolu · arşiv/bayrak yolu) transactional UoW kapsamında ve kanal reddi BİLİNÇLE yukarı
    /// fırlatılıyor (DB işi geri dönsün diye — sınıf sözleşmesinin kendisi). Delil aynı transaction'da yazılsaydı
    /// rollback onu da SİLERDİ: üretimde başarısız deneme deftere asla kalıcı geçmez, "denendi, kanal reddetti"
    /// ile "hiç denenmedi" yine ayırt edilemezdi — 2026-08-10 "başarısız gönderim de yazılır" kuralının bu yoldaki
    /// sessiz ihlali. Satır yalnız kimlik + değer fotoğraflarıyla kurulur (şirket/kayıt id'si + barkod entry'leri;
    /// dış entity state'ine bağımlılık yok), yeni transaction'da güvenle yazılır.</para>
    /// </summary>
    private async Task RecordFailureSurvivingRollbackAsync(
        Guid companyId,
        Guid channelProductId,
        TrendyolProductPushKind kind,
        IReadOnlyCollection<TrendyolPushHistoryEntry> entries,
        string errorMessage)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _historyRecorder.RecordAsync(
            companyId, channelProductId, kind, entries, batchRequestId: null, ChannelPushOutcome.Failed, errorMessage);
        await uow.CompleteAsync();
    }
}
