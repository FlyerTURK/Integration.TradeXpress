using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>Sahte mağazadaki bir ürün satırı. Alan seti <c>GET /ms/product-query</c> yanıtının TAM karşılığıdır —
/// uygulamanın gerçek ayrıştırıcısı (<c>N11ProductQueryClient</c>) bunu okuyacak, eksik alan sessiz null üretir.</summary>
public sealed class N11MockProduct
{
    /// <summary>N11'in atadığı ürün kimliği. Gerçekte 9-10 haneli; <c>long</c> (doküman taşma uyarısı).</summary>
    public long N11ProductId { get; set; }

    /// <summary>Varyantları gruplayan satıcı kodu.</summary>
    public string? ProductMainId { get; set; }

    /// <summary>Satıcı SKU kodu — sahte mağazanın BİRİNCİL ANAHTARI (N11'de de kimlik budur).</summary>
    public string StockCode { get; set; } = string.Empty;

    public string? Title { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? ListPrice { get; set; }
    public int? Quantity { get; set; }

    /// <summary>{Before_Sale, On_Sale, Out_Of_Stock, Sale_Closed}</summary>
    public string? SaleStatus { get; set; }

    /// <summary>{Active, InCatalogApproval, Suspended, CatalogRejected, Unlisted, Prohibited, InApproval}.
    /// Yanıtta alan adı <c>status</c>'tür (istek parametresi <c>productStatus</c>) — uçta böyle yazılır.</summary>
    public string? ProductStatus { get; set; }

    public string? CategoryId { get; set; }
    public List<string> ImageUrls { get; set; } = new();
}

/// <summary>Kuyruğa alınmış bir yazma isteği. <b>Mutasyon burada BEKLETİLİR</b> — task olgunlaşana dek
/// ürünler mağazaya işlenmez (gerçek N11 davranışı; ayrıntı <see cref="N11MockStore"/> doc'unda).</summary>
public sealed class N11MockTask
{
    public string TaskId { get; set; } = string.Empty;

    /// <summary>PRODUCT_CREATE · PRODUCT_UPDATE · PRICE_STOCK_UPDATE</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>IN_QUEUE · PROCESSED · REJECT</summary>
    public string Status { get; set; } = N11MockTaskStates.InQueue;

    /// <summary>Kaç kez sorgulandı — olgunlaşma eşiğiyle karşılaştırılır.</summary>
    public int PollCount { get; set; }

    /// <summary>Bu task'ın taşıdığı satırlar (olgunlaşınca uygulanacak).</summary>
    public List<N11MockProduct> Items { get; set; } = new();

    /// <summary>Kalem bazında sonuç: stockCode → (status, reason). PROCESSED task'ta bile bir satır REJECT olabilir —
    /// gerçek N11'in kısmi başarı davranışı.</summary>
    public List<N11MockTaskItem> Results { get; set; } = new();
}

/// <summary>Task'ın tek kalem sonucu — <c>task-details/page-query</c> yanıtındaki satır.</summary>
public sealed class N11MockTaskItem
{
    public string StockCode { get; set; } = string.Empty;

    /// <summary>SUCCESS · FAILED</summary>
    public string Status { get; set; } = N11MockTaskStates.ItemSuccess;

    /// <summary>Başarısızsa gerekçe — resmî hata sözlüğünden BİREBİR metin
    /// (<see cref="N11MockErrorCatalog"/>); uygulamanın özel-durum eşlemesi bu metinlere bakıyor.</summary>
    public string? Reason { get; set; }
}

/// <summary>Task durum sabitleri — uygulamadaki <c>N11TaskStates</c>'in birebir kopyası.
/// <b>Kasıtlı kopya:</b> mock projesi Application'a referans VERMEZ (bağımlılık yönü tersine dönerdi);
/// bu değerler N11'in tel sözleşmesidir, bizim iç sabitimiz değil.</summary>
public static class N11MockTaskStates
{
    public const string InQueue = "IN_QUEUE";
    public const string Processed = "PROCESSED";
    public const string Reject = "REJECT";
    public const string ItemSuccess = "SUCCESS";
    public const string ItemFailed = "FAILED";
}

/// <summary>Senaryo kipleri — dosyadan elle değiştirilir, kod değişikliği gerekmez.</summary>
public static class N11MockModes
{
    /// <summary>Her şey başarılı.</summary>
    public const string Success = "Success";

    /// <summary>Task REJECT döner (tüm kalemler başarısız).</summary>
    public const string Reject = "Reject";

    /// <summary>Task uzun süre IN_QUEUE kalır — uygulamanın "bekleyen push" yolunu sınamak için.</summary>
    public const string Queued = "Queued";

    /// <summary>Kalem, resmî FAHİŞ FİYAT metniyle reddedilir — uygulamanın özel hata kodunu tetikler.</summary>
    public const string PriceBand = "PriceBand";
}

/// <summary>Senaryo — depo dosyasının içinde yaşar, her istekte yeniden okunur (ikinci doğruluk kaynağı olmasın).</summary>
public sealed class N11MockScenario
{
    /// <summary>Genel kip (<see cref="N11MockModes"/>).</summary>
    public string Mode { get; set; } = N11MockModes.Success;

    /// <summary>Task kaç sorgudan sonra olgunlaşsın (0 = ilk sorguda).</summary>
    public int QueuedPollsBeforeProcessed { get; set; }

    /// <summary>Stok kodu bazında kip override'ı — "şu SKU fahiş fiyat alsın, gerisi geçsin".</summary>
    public Dictionary<string, string> PerStockCode { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bu stok kodu için geçerli kip (override varsa o, yoksa genel kip).</summary>
    public string ModeFor(string stockCode)
    {
        return PerStockCode.TryGetValue(stockCode, out var mode) && !string.IsNullOrWhiteSpace(mode)
            ? mode
            : Mode;
    }
}

/// <summary>Diskteki depo dosyasının kökü.</summary>
public sealed class N11MockState
{
    public N11MockScenario Scenario { get; set; } = new();

    /// <summary>Mağazadaki ürünler — anahtar stok kodu.</summary>
    public Dictionary<string, N11MockProduct> Products { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kuyruktaki/işlenmiş task'lar — anahtar taskId.</summary>
    public Dictionary<string, N11MockTask> Tasks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Bir sonraki ürün kimliği — gerçekçi olsun diye 10 haneli aralıktan başlar.</summary>
    public long NextProductId { get; set; } = 1000000001L;

    public long NextTaskId { get; set; } = 1001L;
}
