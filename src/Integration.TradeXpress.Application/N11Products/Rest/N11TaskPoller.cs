using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 asenkron task sonucunu okur — <c>POST /ms/product/task-details/page-query</c>.
/// <para>
/// <b>VAROLUŞ SEBEBİ: HTTP 200 BAŞARI DEĞİLDİR; gerçek sonuç bu poller'dan gelir.</b>
/// N11'in üç REST yazma ucu (<c>product-create</c> · <c>product-update</c> · <c>price-stock-update</c>) senkron
/// SOAP <c>SaveProduct</c>'ın aksine yalnız bir <c>taskId</c> döndürür; ürünün gerçekten yüklenip yüklenmediği,
/// hangi SKU'nun neden reddedildiği <b>sadece</b> bu uçtan öğrenilir. Bu yüzden REST'e geçiş bir istemci
/// değişimi değil AKIŞ değişimidir: gönder → <c>taskId</c> sakla → yokla → SKU bazlı sonucu yorumla.
/// </para>
/// <para>
/// Uç, N11 dokümanının §1 servis kataloğunda LİSTELENMEMİŞTİR (yalnız gövdede anlatılır) ve metin yalnız
/// <c>UpdateProduct</c>/<c>UpdateProductPriceAndStock</c>'tan söz eder; ancak <c>CreateProduct</c> de aynı
/// TaskDetail'e yönlendirdiği için <b>üç uç için de geçerlidir</b>.
/// </para>
/// </summary>
/// <summary>Task sonucu sorgulayıcısının sözleşmesi — <b>somut sınıf yerine arayüz</b>: push akışı buna
/// bağlanır ki testte ağa çıkmadan sahtelenebilsin (IN11ProductRestClient ile aynı gerekçe).</summary>
public interface IN11TaskPoller : ITransientDependency
{
    Task<N11TaskResult> QueryAsync(string taskId, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

public sealed class N11TaskPoller : N11RestClientBase, IN11TaskPoller
{
    /// <summary>Yoklama sayfa boyutu. Doküman örneği 1000 istese de yanıtta 100000 raporluyor (kendi içinde
    /// tutarsız) ⇒ N11 boyutu yok sayabilir; döngü zaten <c>last</c>/<c>totalPages</c>'e göre biter.</summary>
    private const int PageSize = 100;

    /// <summary>Güvenlik freni: bozuk sayfalama sinyali gelirse sonsuz döngüye girmeyelim (100 sayfa × 100 = 10.000 SKU;
    /// tek istekteki azami 1000 SKU sınırının çok üstünde).</summary>
    private const int MaxPages = 100;

    /// <summary>taskId üst sınırı — cömert tutuldu: doküman sayısal id gösteriyor ama N11 alan genişliklerini
    /// büyütebiliyor (<c>n11ProductId</c> 9→10 hane notu), o yüzden dar sınır koymuyoruz.</summary>
    private const int MaxTaskIdLength = 64;

    private readonly ILogger<N11TaskPoller> _logger;

    public N11TaskPoller(ILogger<N11TaskPoller> logger, IOptions<N11EndpointOptions> endpointOptions)
        : base(endpointOptions)
    {
        _logger = logger;
    }

    /// <summary>
    /// Task'ın TÜM sayfalarını dolaşıp SKU bazlı sonucu tek sonuçta birleştirir.
    /// <para>
    /// <b>HTTP 200 başarı DEĞİLDİR; gerçek sonuç bu poller'dan gelir.</b> Yazma ucundan dönen 200 + <c>taskId</c>
    /// yalnız "kuyruğa girdi" demektir. Dönen <see cref="N11TaskResult.State"/> <see cref="N11TaskState.InQueue"/>
    /// ise sonuç HENÜZ YOKTUR — çağıran daha sonra tekrar sorgulamalıdır (bu metot kendi başına BEKLEMEZ/yeniden
    /// denemez; zamanlama kararı çağıranındır). <see cref="N11TaskState.Processed"/> bile SKU bazında başarı
    /// garanti etmez; kısmi başarı normaldir ⇒ <see cref="N11TaskResult.Items"/> tek tek okunmalıdır.
    /// </para>
    /// </summary>
    /// <param name="taskId">Yazma ucunun döndürdüğü <see cref="N11TaskSubmission.TaskId"/>.</param>
    public async Task<N11TaskResult> QueryAsync(
        string taskId,
        string appKey,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        // Fail-fast: boş taskId ile ağa çıkmanın anlamı yok. (Check.NotNullOrWhiteSpace bu projede BannedSymbols
        // ile YASAK — kök BannedSymbols.txt Application katmanına da bağlı; sanctioned kapı StringFieldGuard.)
        var normalizedTaskId = StringFieldGuard.EnsureRequiredText(taskId, nameof(taskId), 1, MaxTaskIdLength);

        var url = $"{RestProductBase}/task-details/page-query";
        var items = new List<N11TaskItemResult>();
        var state = N11TaskState.Unknown;
        string? rejectReason = null;
        var headerRead = false;
        var completed = false;

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = BuildPageQueryBody(normalizedTaskId, page, PageSize);
            var raw = await RestSendAsync(HttpMethod.Post, url, body, appKey, appSecret, cancellationToken);

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            // Task'ın kendi statüsü/gerekçesi her sayfada aynıdır — ilk sayfadan okunur.
            if (!headerRead)
            {
                var rawStatus = ReadString(root, "status");
                state = N11TaskStates.Parse(rawStatus);
                if (state == N11TaskState.Unknown)
                {
                    // Fail-loud DEĞİL (akışı kesmiyoruz) ama sessiz de değil: belgelenmemiş statü görünür olsun.
                    _logger.LogWarning(
                        "N11 task {TaskId} belgelenmemiş statü döndürdü: '{RawStatus}' → Unknown. Başarı SAYILMAMALIDIR.",
                        normalizedTaskId, rawStatus);
                }

                rejectReason = ReadReasons(root);
                headerRead = true;
            }

            if (!root.TryGetProperty("skus", out var skus) || skus.ValueKind != JsonValueKind.Object)
            {
                // SKU bloğu yok — REJECT edilmiş ya da henüz kuyrukta olan task'ta normaldir.
                completed = true;
                break;
            }

            if (!skus.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array
                || content.GetArrayLength() == 0)
            {
                completed = true;   // boş sayfa = son sayfa (dokümanın önerdiği bitiş ölçütü)
                break;
            }

            foreach (var element in content.EnumerateArray())
            {
                items.Add(ReadItem(element, normalizedTaskId));
            }

            if (IsLastPage(skus, page))
            {
                completed = true;
                break;
            }
        }

        if (!completed)
        {
            _logger.LogWarning(
                "N11 task {TaskId} yoklaması {MaxPages} sayfa sınırına takıldı — sonuç EKSİK olabilir ({ItemCount} SKU okundu).",
                normalizedTaskId, MaxPages, items.Count);
        }

        return new N11TaskResult(state, items, rejectReason);
    }

    // ── Gövde kurulumu ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>{"taskId":…,"pageable":{"page":…,"size":…}}</c>. Doküman örneği <c>taskId</c>'yi SAYI olarak gönderiyor;
    /// sözleşmemizde ise dizgi taşınıyor (id genişleyebilir — <c>n11ProductId</c>'nin 9→10 haneye çıkması gibi).
    /// Bu yüzden sayıya çevrilebiliyorsa SAYI, çevrilemiyorsa dizgi yazılır: N11'in beklediği biçim korunur,
    /// gelecekteki alfanümerik id de kırılmaz.
    /// </summary>
    /// <param name="taskId"><see cref="StringFieldGuard"/>'dan geçmiş (trim'li, boş olmayan) değer.</param>
    private static string BuildPageQueryBody(string taskId, int page, int size)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (long.TryParse(taskId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericTaskId))
            {
                writer.WriteNumber("taskId", numericTaskId);
            }
            else
            {
                writer.WriteString("taskId", taskId);
            }

            writer.WriteStartObject("pageable");
            writer.WriteNumber("page", page);
            writer.WriteNumber("size", size);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // ── Yanıt okuma ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sayfalama bitiş ölçütü: önce <c>last</c>, yoksa <c>totalPages</c>. Sinyal hiç yoksa TEK sayfa
    /// varsayılır — bilinmezlikte döngüyü uzatmak yerine keseriz (sonsuz yoklama riski).</summary>
    private static bool IsLastPage(JsonElement skus, int page)
    {
        if (skus.TryGetProperty("last", out var last)
            && last.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return last.ValueKind == JsonValueKind.True;
        }

        if (skus.TryGetProperty("totalPages", out var totalPages)
            && totalPages.ValueKind == JsonValueKind.Number
            && totalPages.TryGetInt32(out var pageCount))
        {
            return page + 1 >= pageCount;
        }

        return true;
    }

    private N11TaskItemResult ReadItem(JsonElement element, string taskId)
    {
        var itemCode = ReadString(element, "itemCode")?.Trim() ?? string.Empty;
        var rawStatus = ReadString(element, "status");

        // Gerekçe önce öğe seviyesinde, yoksa iç `sku` bloğunda aranır (doküman ikisini de dolduruyor).
        var reason = ReadReasons(element);
        if (reason is null
            && element.TryGetProperty("sku", out var sku)
            && sku.ValueKind == JsonValueKind.Object)
        {
            reason = ReadReasons(sku);
        }

        if (itemCode.Length == 0)
        {
            // Kayıt ATILMAZ (gerekçe kaybolmasın) ama eşlemesi yapılamayacağı için görünür kılınır.
            _logger.LogWarning(
                "N11 task {TaskId} sonucunda itemCode BOŞ geldi (status='{Status}', reason='{Reason}') — SKU eşlemesi yapılamaz.",
                taskId, rawStatus, reason);
        }

        return new N11TaskItemResult(itemCode, N11TaskStates.IsItemSuccess(rawStatus), reason);
    }

    /// <summary><c>reasons[]</c> dizisini tek metne birleştirir; yoksa tekil <c>reason</c> alanına düşer.</summary>
    private static string? ReadReasons(JsonElement element)
    {
        if (element.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
        {
            var joined = string.Join("; ", reasons
                .EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.String)
                .Select(r => r.GetString()?.Trim())
                .Where(s => !string.IsNullOrEmpty(s)));

            if (joined.Length > 0)
            {
                return joined;
            }
        }

        var single = ReadString(element, "reason")?.Trim();
        return string.IsNullOrEmpty(single) ? null : single;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
