using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL PUSH GEÇMİŞİ YAZICISI — <see cref="N11PushHistoryRecorder"/>'ın Trendyol eşi.
///
/// <para><b>⚠ N11'DEN TEK ANLAMLI FARK — NE ZAMAN ÇAĞRILIR:</b> N11'de kayıt <b>submit başarısında</b> yazılır
/// (REST yazımı senkron çözülebiliyor). Trendyol'da yazım <b>batch COMPLETED olduğunda</b> yapılır — çünkü
/// Trendyol yazma uçları asenkron ve <b>batch REDDEDİLEBİLİR</b>. Submit anında yazsaydık delil "gönderdim"
/// derdi; delilin söylemesi gereken ise "kabul edildi"dir. Reddedilen bir batch'in geçmişte başarılı görünmesi,
/// kaydı delil olmaktan çıkarırdı. Çağrı noktası bu yüzden TEKTİR: <c>FinalizeCompletedBatchAsync</c>.</para>
///
/// <para><b>⚠ Kayıt PUSH'U DÜŞÜRMEZ:</b> geçmiş yazılamazsa gönderim BAŞARILI sayılmaya devam eder — mal zaten
/// Trendyol'a ulaşmıştır, kaydı tutamamak onu geri almaz. Hata YUTULMAZ, loglanır. Tersi tercih edilseydi delil
/// kaydı, çalışan satışı bozan bir tek nokta arızasına dönerdi.</para>
///
/// <para>Görseller <c>MediaId</c> + <c>ContentHash</c> ile saklanır: id "hangi kayıt", hash "içerik o gün
/// buydu" der.</para>
/// </summary>
public class TrendyolPushHistoryRecorder : ITransientDependency
{
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _repository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IClock _clock;
    private readonly ILogger<TrendyolPushHistoryRecorder> _logger;

    public TrendyolPushHistoryRecorder(
        IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> repository,
        IRepository<Media, Guid> mediaRepository,
        IClock clock,
        ILogger<TrendyolPushHistoryRecorder> logger)
    {
        _repository = repository;
        _mediaRepository = mediaRepository;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>KABUL EDİLEN (COMPLETED) gönderim için geçmiş satırlarını yazar. Hata push'u DÜŞÜRMEZ.</summary>
    public virtual async Task RecordAsync(
        Guid companyId,
        Guid channelProductId,
        TrendyolProductPushKind pushKind,
        IReadOnlyCollection<TrendyolPushHistoryEntry> entries,
        string? batchRequestId)
    {
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        try
        {
            var pushedAt = _clock.Now.ToUniversalTime();
            var hashByMedia = await LoadContentHashesAsync(entries);

            foreach (var entry in entries)
            {
                var history = new SalesChannelTrTrendyolProductPushHistory(
                    companyId, channelProductId, entry.Barcode, pushedAt, pushKind);

                history.Fill(
                    entry.ListPrice,
                    entry.SalePrice,
                    entry.Quantity,
                    entry.Title,
                    entry.Options,
                    entry.MediaIds?.Select(id => (id, hashByMedia.GetValueOrDefault(id))),
                    batchRequestId);

                await _repository.InsertAsync(history, autoSave: true);
            }
        }
        catch (Exception ex)
        {
            // Sessiz yutma DEĞİL: gönderim başarılı sayılır ama eksiklik kimlikleriyle loglanır.
            _logger.LogWarning(
                ex,
                "Trendyol push geçmişi yazılamadı (ChannelProduct={ChannelProductId}, Kind={PushKind}, Sku={SkuCount}). "
                + "Gönderim BAŞARILI sayıldı — mal Trendyol'a ulaştı, kaydı tutamamak onu geri almaz.",
                channelProductId, pushKind, entries.Count);
        }
    }

    /// <summary>Görsellerin içerik hash'leri — tek sorguda. Okunamayan medya boş hash ile geçer:
    /// kaydın kendisini düşürmek, delilin tamamını kaybetmek olurdu.</summary>
    private async Task<Dictionary<Guid, string?>> LoadContentHashesAsync(
        IReadOnlyCollection<TrendyolPushHistoryEntry> entries)
    {
        var mediaIds = entries
            .Where(e => e.MediaIds is not null)
            .SelectMany(e => e.MediaIds!)
            .Distinct()
            .ToList();

        if (mediaIds.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var rows = await _mediaRepository.GetListAsync(m => mediaIds.Contains(m.Id));
        return rows.ToDictionary(m => m.Id, m => (string?)m.ContentHash);
    }
}

/// <summary>Tek bir SKU için gönderilen değerler — geçmiş yazıcısının girdisi.
/// <c>ListPrice</c> + <c>SalePrice</c> BİRLİKTE taşınır: Trendyol'da indirim ayrı bir alan değil, ikisinin
/// farkıdır → yalnız biri saklansaydı indirim delili kurulamazdı.</summary>
public sealed record TrendyolPushHistoryEntry(
    string Barcode,
    decimal? ListPrice,
    decimal? SalePrice,
    int? Quantity,
    string? Title,
    IReadOnlyList<(string Name, string Value)>? Options,
    IReadOnlyList<Guid>? MediaIds);
