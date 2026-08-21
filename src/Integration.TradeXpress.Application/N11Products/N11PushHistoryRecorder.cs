using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.SalesChannelProducts;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// PUSH GEÇMİŞİ YAZICISI — N11'e gerçekten ULAŞAN her gönderim için tarihli PushHistory kaydı üretir.
///
/// <para><b>Neden ayrı servis:</b> iki push yolu var (tam push + fiyat/stok senkronu) ve ikisi de aynı kaydı
/// yazmalı. Çağrı yerine kopyalanan bir kayıt mantığı zamanla ayrışır — biri görseli yazar, diğeri unutur;
/// ve eksiklik ancak ihtilaf çıktığında fark edilir.</para>
///
/// <para><b>⚠ Kayıt PUSH'U DÜŞÜRMEZ:</b> geçmiş yazılamazsa (ör. alan taşması) gönderim BAŞARILI sayılmaya
/// devam eder — mal zaten N11'e gitmiştir, kaydı tutamamak onu geri almaz. Hata YUTULMAZ, loglanır.
/// Tersi tercih edilseydi PushHistory kaydı, çalışan satışı bozan bir tek nokta arızasına dönerdi.</para>
///
/// <para>Görseller <c>MediaId</c> + <c>ContentHash</c> ile saklanır: id "hangi kayıt", hash "içerik o gün
/// buydu" der. Bugünkü DAM'da içerik blob'u zaten üzerine yazılmıyor — hash o güvenceyi belgeler.</para>
/// </summary>
public class N11PushHistoryRecorder : ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11ProductPushHistory, Guid> _repository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IClock _clock;
    private readonly ILogger<N11PushHistoryRecorder> _logger;

    public N11PushHistoryRecorder(
        IRepository<SalesChannelTrN11ProductPushHistory, Guid> repository,
        IRepository<Media, Guid> mediaRepository,
        IClock clock,
        ILogger<N11PushHistoryRecorder> logger)
    {
        _repository = repository;
        _mediaRepository = mediaRepository;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Gönderilen SKU'lar için geçmiş satırlarını yazar. Hata push'u DÜŞÜRMEZ (bkz. sınıf doc'u).
    /// <para><paramref name="outcome"/> ZORUNLUDUR (varsayılanı yok): başarısız bir denemenin başarılı
    /// yazılması, bu defterin önlemek için var olduğu hatadır — derleyici her çağrı yerini beyana zorlar.
    /// <paramref name="errorMessage"/> yalnız <see cref="ChannelPushOutcome.Failed"/>'da anlamlıdır.</para></summary>
    public virtual async Task RecordAsync(
        Guid companyId,
        Guid channelProductId,
        N11ProductPushKind pushKind,
        IReadOnlyCollection<N11PushHistoryEntry> entries,
        string? remoteReference,
        ChannelPushOutcome outcome,
        string? errorMessage = null)
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
                var history = new SalesChannelTrN11ProductPushHistory(
                    companyId, channelProductId, entry.SellerStockCode, pushedAt, pushKind, outcome);

                history.Fill(
                    entry.SalePrice,
                    entry.CurrencyType,
                    entry.Quantity,
                    entry.Title,
                    entry.Options,
                    entry.MediaIds?.Select(id => (id, hashByMedia.GetValueOrDefault(id))),
                    remoteReference,
                    outcome == ChannelPushOutcome.Failed ? errorMessage : null);

                await _repository.InsertAsync(history, autoSave: true);
            }
        }
        catch (Exception ex)
        {
            // Sessiz yutma DEĞİL: gönderim başarılı sayılır ama eksiklik kimlikleriyle loglanır.
            _logger.LogWarning(
                ex,
                "N11 push geçmişi yazılamadı (ChannelProduct={ChannelProductId}, Kind={PushKind}, Sku={SkuCount}). "
                + "Gönderim BAŞARILI sayıldı — mal N11'e ulaştı, kaydı tutamamak onu geri almaz.",
                channelProductId, pushKind, entries.Count);
        }
    }

    /// <summary>Görsellerin içerik hash'leri — tek sorguda. Okunamayan medya boş hash ile geçer:
    /// kaydın kendisini düşürmek, delilin tamamını kaybetmek olurdu.</summary>
    private async Task<Dictionary<Guid, string?>> LoadContentHashesAsync(
        IReadOnlyCollection<N11PushHistoryEntry> entries)
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

    /// <summary>Fiyat/stok gönderiminin geçmiş girdileri — GÖNDERİLEN (ya da gönderilmeye çalışılan) satırlardan
    /// kurulur; başarı ve başarısızlık dalları AYNI kaynaktan okur ki iki dal zamanla ayrışmasın. ORTAK gövde:
    /// senkron yolu (<c>SyncStockAndPriceAsync</c>) ile pasifleştirmenin adet-0 gönderimi
    /// (<see cref="N11StockWithdrawer"/>) aynı delil biçimini yazmalı — ikinci bir defter biçimi İCAT EDİLMEZ.
    /// <para>İçerik (başlık/görsel/seçenek) bu yolda gönderilmediği için <c>null</c> geçilir — gönderilmeyeni
    /// deftere yazmak yalan olurdu.</para></summary>
    public static List<N11PushHistoryEntry> BuildPriceStockEntries(IReadOnlyCollection<N11RestPriceStock> items)
    {
        return items
            .Select(item => new N11PushHistoryEntry(
                item.StockCode,
                item.SalePrice,
                item.CurrencyType,
                item.Quantity,
                Title: null,
                Options: null,
                MediaIds: null))
            .ToList();
    }
}

/// <summary>Tek bir SKU için gönderilen değerler — geçmiş yazıcısının girdisi.</summary>
public sealed record N11PushHistoryEntry(
    string SellerStockCode,
    decimal? SalePrice,
    string? CurrencyType,
    int? Quantity,
    string? Title,
    IReadOnlyList<(string Name, string Value)>? Options,
    IReadOnlyList<Guid>? MediaIds);
