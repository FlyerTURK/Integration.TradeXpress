using System;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 uç adreslerinin TEK kaynağı (config bölümü <c>"N11:Endpoints"</c>).
///
/// <para><b>Neden var:</b> <c>https://api.n11.com</c> dokuz ayrı istemcide sabit olarak gömülüydü. Dış bir konağı
/// kaynağa dağıtmak zaten koku; ama asıl tetikleyici şu: hesap erişimi kapalıyken (2026-08-05 doğrulandı — iki
/// ayrı gerçek hesap da <c>401 "erişiminiz durdurulmuştur"</c> alıyor) N11 kodunu denemenin tek yolu istekleri
/// N11 gibi konuşan YEREL bir sunucuya yönlendirmek. Bunun için tabanın yapılandırılabilir olması gerekiyor.</para>
///
/// <para><b>Davranış-nötr:</b> varsayılan bugünkü adres. Config'te bölüm yoksa hiçbir şey değişmez —
/// üretimde davranış birebir aynı kalır.</para>
///
/// <para><b>Sınır:</b> burası yalnız ADRESİ taşır. Sahte sunucunun açık olup olmadığı ayrı bir koşuldur
/// (<c>N11:Mock:Enabled</c> + <c>IsDevelopment</c>); taban adresi mock'u göstermedikçe hiçbir istek oraya gitmez.</para>
/// </summary>
public sealed class N11EndpointOptions
{
    public const string SectionName = "N11:Endpoints";

    /// <summary>Şema + konak (sonda eğik çizgi OLMADAN, ör. <c>https://api.n11.com</c>). Tüm REST ve SOAP
    /// adresleri bundan türer.</summary>
    public string BaseUrl { get; set; } = "https://api.n11.com";

    // ── REST ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ürün YAZMA + task sorgulama tabanı: <c>/tasks/product-create</c>, <c>/tasks/product-update</c>,
    /// <c>/tasks/price-stock-update</c>, <c>/task-details/page-query</c>.</summary>
    public string RestProductBase
    {
        get { return Combine("/ms/product"); }
    }

    /// <summary>Satıcı ürünlerini listeleme ucu — tek SENKRON REST ucu (yazma uçları asenkron).</summary>
    public string RestQueryBase
    {
        get { return Combine("/ms/product-query"); }
    }

    /// <summary>Kategori CDN tabanı — ağaç (<c>/categories</c>) + yaprak nitelikleri
    /// (<c>/category/{id}/attribute</c>).</summary>
    public string RestCdnBase
    {
        get { return Combine("/cdn"); }
    }

    // ── SOAP (WSDL uçları) ──────────────────────────────────────────────────────────────────────────

    public string CategoryServiceEndpoint
    {
        get { return Soap("CategoryService"); }
    }

    public string CityServiceEndpoint
    {
        get { return Soap("CityService"); }
    }

    public string ProductServiceEndpoint
    {
        get { return Soap("ProductService"); }
    }

    public string OrderServiceEndpoint
    {
        get { return Soap("OrderService"); }
    }

    public string ShipmentServiceEndpoint
    {
        get { return Soap("ShipmentService"); }
    }

    public string ShipmentCompanyServiceEndpoint
    {
        get { return Soap("ShipmentCompanyService"); }
    }

    /// <summary>Taban adresi mock'u mu gösteriyor — UI rozeti ve worker'ın çalışma koşulu bunu okur. Ölçüt: varsayılan
    /// N11 konağından FARKLI olması. "Mock" kelimesini aramıyoruz; adres neresi olursa olsun, gerçek N11
    /// değilse kullanıcı bunu görmelidir.</summary>
    public bool IsRedirected
    {
        get { return !string.Equals(Normalized, DefaultBaseUrl, StringComparison.OrdinalIgnoreCase); }
    }

    private const string DefaultBaseUrl = "https://api.n11.com";

    /// <summary>Sondaki eğik çizgi ayıklanmış taban — birleştirmede çift eğik çizgi üretmemek için.</summary>
    private string Normalized
    {
        get { return (BaseUrl ?? DefaultBaseUrl).TrimEnd('/'); }
    }

    private string Combine(string path)
    {
        return Normalized + path;
    }

    private string Soap(string serviceName)
    {
        return $"{Normalized}/ws/{serviceName}.wsdl";
    }
}
