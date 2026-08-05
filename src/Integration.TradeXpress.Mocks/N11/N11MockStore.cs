using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// Sahte N11 mağazasının durumu — ürünler, task'lar, senaryo. Bellek-içi çalışma kopyası + JSON dosyası.
///
/// <para><b>EN KRİTİK KURAL — mutasyon yalnız OLGUNLAŞMADA uygulanır.</b> Yazma ucu çağrıldığında ürün mağazaya
/// İŞLENMEZ; yalnız task kuyruğa alınır. Ürün ancak task <c>PROCESSED</c>'e geçtiğinde <c>product-query</c>'de
/// görünür. Gerçek N11 böyle davranır ve sırayı ters kurmak <b>sessiz bir yalan</b> üretirdi: uygulama push'tan
/// hemen sonra geri okuma yapıyor (<c>N11ProductId</c>'nin TEK kaynağı o okuma) ve ürünü anında bulsaydı,
/// aslında hiç işlenmemiş bir task'ı başarılı sayıp <c>MarkSynced</c> ile yarım gerçek yazardı.</para>
///
/// <para><b>Eşzamanlılık:</b> tüm işlemler tek <see cref="SemaphoreSlim"/> ardında serileştirilir. Sahte sunucu
/// tek kullanıcılı bir geliştirme aracıdır; kilit basitliği doğru takas.</para>
///
/// <para><b>Kalıcılık:</b> her mutasyondan sonra dosyaya yazılır. Host sürekli yeniden başlatıldığından
/// salt-bellek depo her seferinde push'u sıfırdan yapmayı gerektirirdi. Dosyayı silmek depoyu sıfırlar.</para>
/// </summary>
public sealed class N11MockStore
{
    private static readonly JsonSerializerOptions FileJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly int _defaultQueuedPolls;
    private N11MockState? _state;

    public N11MockStore(string path, int defaultQueuedPolls)
    {
        _path = path;
        _defaultQueuedPolls = defaultQueuedPolls < 0 ? 0 : defaultQueuedPolls;
    }

    // ── Yazma uçları: task KUYRUĞA ALINIR, mutasyon uygulanmaz ──────────────────────────────────────

    /// <summary>Yazma isteğini kuyruğa alır ve <c>taskId</c> döner. Ürünler HENÜZ mağazaya işlenmez.</summary>
    public async Task<string> SubmitAsync(string taskType, IReadOnlyList<N11MockProduct> items)
    {
        await _gate.WaitAsync();
        try
        {
            var state = Load();
            var taskId = state.NextTaskId.ToString(CultureInfo.InvariantCulture);
            state.NextTaskId++;

            state.Tasks[taskId] = new N11MockTask
            {
                TaskId = taskId,
                Type = taskType,
                Status = N11MockTaskStates.InQueue,
                PollCount = 0,
                Items = items.ToList(),
            };

            Save(state);
            return taskId;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Sorgu: olgunlaşma BURADA olur ───────────────────────────────────────────────────────────────

    /// <summary>Task'ı sorgular. Eşik aşıldıysa task olgunlaşır: senaryoya göre sonuç üretilir ve
    /// <b>başarılı kalemler mağazaya İŞLENİR</b>. Bilinmeyen taskId için <c>null</c>.</summary>
    public async Task<N11MockTask?> PollTaskAsync(string taskId)
    {
        await _gate.WaitAsync();
        try
        {
            var state = Load();
            if (!state.Tasks.TryGetValue(taskId, out var task))
            {
                return null;
            }

            if (task.Status == N11MockTaskStates.InQueue)
            {
                task.PollCount++;

                var scenario = state.Scenario;
                var threshold = scenario.Mode == N11MockModes.Queued
                    ? int.MaxValue   // "Queued" kipi: hiç olgunlaşma — bekleyen push yolunu sınamak için
                    : ResolveThreshold(scenario);

                if (task.PollCount > threshold)
                {
                    Mature(state, task);
                }
            }

            Save(state);
            return Clone(task);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mağaza katalogunu sorgular (sayfalı + stok kodu filtreli) — yalnız OLGUNLAŞMIŞ ürünler.</summary>
    public async Task<(IReadOnlyList<N11MockProduct> Items, int TotalPages, long TotalCount)> QueryProductsAsync(
        int page, int size, string? stockCode, string? productStatus)
    {
        await _gate.WaitAsync();
        try
        {
            var state = Load();
            var all = state.Products.Values
                .Where(p => stockCode is null || string.Equals(p.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))
                .Where(p => productStatus is null || string.Equals(p.ProductStatus, productStatus, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.StockCode, StringComparer.Ordinal)
                .ToList();

            var effectiveSize = size <= 0 ? 20 : size;
            var totalPages = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)effectiveSize);
            var items = all.Skip(Math.Max(0, page) * effectiveSize).Take(effectiveSize).Select(Clone).ToList();

            return (items, totalPages, all.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Senaryoyu okur (uçların kip kararı için).</summary>
    public async Task<N11MockScenario> GetScenarioAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return Load().Scenario;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Olgunlaşma ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Task'ı sonuçlandırır: kalem başına senaryo kipine göre SUCCESS/FAILED üretir ve başarılı
    /// kalemleri mağazaya upsert eder (stok kodu anahtar → idempotent).</summary>
    private static void Mature(N11MockState state, N11MockTask task)
    {
        var scenario = state.Scenario;
        var anySuccess = false;

        foreach (var item in task.Items)
        {
            var mode = scenario.ModeFor(item.StockCode);
            var reason = mode switch
            {
                N11MockModes.PriceBand => N11MockErrorCatalog.PriceBandTooHigh,
                N11MockModes.Reject => N11MockErrorCatalog.GenericReject,
                _ => null,
            };

            task.Results.Add(new N11MockTaskItem
            {
                StockCode = item.StockCode,
                Status = reason is null ? N11MockTaskStates.ItemSuccess : N11MockTaskStates.ItemFailed,
                Reason = reason,
            });

            if (reason is not null)
            {
                continue;   // başarısız kalem mağazaya İŞLENMEZ
            }

            anySuccess = true;
            Upsert(state, item);
        }

        // Hiçbir kalem geçmediyse task'ın kendisi REJECT — gerçek N11 davranışı.
        task.Status = anySuccess ? N11MockTaskStates.Processed : N11MockTaskStates.Reject;
    }

    /// <summary>Ürünü mağazaya yazar. Stok kodu MEVCUTSA alanları günceller (n11ProductId KORUNUR — kimlik
    /// bir kez atanır); yoksa yeni kimlik üretir. İdempotent: aynı push iki kez olgunlaşsa ikinci ürün doğmaz.</summary>
    private static void Upsert(N11MockState state, N11MockProduct incoming)
    {
        if (state.Products.TryGetValue(incoming.StockCode, out var existing))
        {
            existing.ProductMainId = incoming.ProductMainId ?? existing.ProductMainId;
            existing.Title = incoming.Title ?? existing.Title;
            existing.SalePrice = incoming.SalePrice ?? existing.SalePrice;
            existing.ListPrice = incoming.ListPrice ?? existing.ListPrice;
            existing.Quantity = incoming.Quantity ?? existing.Quantity;
            existing.CategoryId = incoming.CategoryId ?? existing.CategoryId;
            if (incoming.ImageUrls.Count > 0)
            {
                existing.ImageUrls = incoming.ImageUrls.ToList();
            }

            return;
        }

        incoming.N11ProductId = state.NextProductId++;
        incoming.SaleStatus ??= "On_Sale";
        incoming.ProductStatus ??= "Active";
        state.Products[incoming.StockCode] = incoming;
    }

    private int ResolveThreshold(N11MockScenario scenario)
    {
        return scenario.QueuedPollsBeforeProcessed > 0 ? scenario.QueuedPollsBeforeProcessed : _defaultQueuedPolls;
    }

    // ── Kalıcılık ───────────────────────────────────────────────────────────────────────────────────

    private N11MockState Load()
    {
        if (_state is not null)
        {
            return _state;
        }

        if (File.Exists(_path))
        {
            try
            {
                _state = JsonSerializer.Deserialize<N11MockState>(File.ReadAllText(_path), FileJson) ?? new N11MockState();
                return _state;
            }
            catch (JsonException)
            {
                // Bozuk dosya sessizce YUTULMAZ ama akışı da durdurmaz: geliştirici dosyayı elle düzenliyor,
                // bir virgül hatası tüm host'u düşürmemeli. Taze depoyla devam edilir; dosya ilk yazımda düzelir.
                _state = new N11MockState();
                return _state;
            }
        }

        _state = new N11MockState();
        return _state;
    }

    private void Save(N11MockState state)
    {
        _state = state;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(state, FileJson));
    }

    // Dışarıya verilen nesneler KOPYA — çağıran deponun iç durumunu kazara değiştirmesin.
    private static N11MockProduct Clone(N11MockProduct p)
    {
        return new N11MockProduct
        {
            N11ProductId = p.N11ProductId,
            ProductMainId = p.ProductMainId,
            StockCode = p.StockCode,
            Title = p.Title,
            SalePrice = p.SalePrice,
            ListPrice = p.ListPrice,
            Quantity = p.Quantity,
            SaleStatus = p.SaleStatus,
            ProductStatus = p.ProductStatus,
            CategoryId = p.CategoryId,
            ImageUrls = p.ImageUrls.ToList(),
        };
    }

    private static N11MockTask Clone(N11MockTask t)
    {
        return new N11MockTask
        {
            TaskId = t.TaskId,
            Type = t.Type,
            Status = t.Status,
            PollCount = t.PollCount,
            Items = t.Items.Select(Clone).ToList(),
            Results = t.Results.Select(r => new N11MockTaskItem
            {
                StockCode = r.StockCode,
                Status = r.Status,
                Reason = r.Reason,
            }).ToList(),
        };
    }
}
