using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Orders;

/// <summary>Sipariş liste sorgusu (per-tenant, company-owned) — sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ). Merkezi <see cref="ListRequestDto"/> standardı: kanal/durum/tarih filtreleri
/// whitelist üzerinden <c>Filters</c> ile uygulanır.</summary>
public class OrderListRequestDto : ListRequestDto
{
}

/// <summary>Ortak sipariş paneli grid satırı — TÜM kanalların siparişleri (base sorgu). Kanal yalnız discriminator
/// (<see cref="ChannelType"/> + enrich edilmiş <see cref="SalesChannelCode"/>). Satırlar SNAPSHOT'tan çizilir.</summary>
public class OrderListDto : EntityDto<Guid>, IListDto<Guid>
{
    public Guid SalesChannelId { get; set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + filtre.</summary>
    public SalesChannelType ChannelType { get; set; }

    /// <summary>Kanal kodu — AppService'te enrich edilir (id-only referanstan; mapper doldurmaz).</summary>
    public string? SalesChannelCode { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus NeutralStatus { get; set; }
    public string? RemoteStatus { get; set; }
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public string? CargoProvider { get; set; }
    public string? CargoTrackingNumber { get; set; }
    public DateTime FetchedAt { get; set; }
}

/// <summary>Sipariş satırı görünümü — ürün-agnostik SNAPSHOT (yerel ürün olmasa da tam anlamlı).</summary>
public class OrderLineDto
{
    public Guid Id { get; set; }
    public string? RemoteLineId { get; set; }
    public string? Barcode { get; set; }
    public string? StockCode { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? RemoteLineStatus { get; set; }

    /// <summary>Yerel varyant bağı — id-only OPSİYONEL zenginleştirme (null = yerel eşleşme yok; NORMAL durum).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Türetilmiş görüntü bayrağı: yerel ürüne navigasyon sunulabilir mi (aksi halde "yerel ürün yok" ipucu).</summary>
    public bool HasLocalVariant => ProductVariantId.HasValue;
}

/// <summary>Sipariş detay görünümü (basit) — başlık alanları + satırlar. O0'da kanal-özel izleme formu YOK.
/// CrudLayout TGetDto sözleşmesi için <see cref="IGetDto{TKey}"/> (salt-okuma; edit formu yok).</summary>
public class OrderDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid SalesChannelId { get; set; }
    public SalesChannelType ChannelType { get; set; }
    public string? SalesChannelCode { get; set; }
    public string RemoteOrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus NeutralStatus { get; set; }
    public string? RemoteStatus { get; set; }
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public string? CargoProvider { get; set; }
    public string? CargoTrackingNumber { get; set; }
    public DateTime FetchedAt { get; set; }
    public List<OrderLineDto> Lines { get; set; } = new();
}

/// <summary>Sipariş çekim SONUÇ RAPORU — sessiz geçilmez: çekilen / yeni / güncellenen sipariş sayıları + uyarılar
/// (ör. TRY para birimi çözülemedi, anahtarsız uzak kayıt atlandı) ekranda gösterilir (komisyon import raporu deseni).</summary>
public class OrderFetchResultDto
{
    /// <summary>Pazaryerinden çekilen toplam sipariş (paket) sayısı.</summary>
    public int FetchedOrders { get; set; }

    /// <summary>Bu çekimde üretilen YENİ sipariş sayısı.</summary>
    public int NewOrders { get; set; }

    /// <summary>Mevcut olup GÜNCELLENEN sipariş sayısı (idempotent ikinci çekim).</summary>
    public int UpdatedOrders { get; set; }

    /// <summary>Çekilen toplam sipariş SATIRI sayısı.</summary>
    public int TotalLines { get; set; }

    /// <summary>İşlenen kanal sayısı (toplu çekimde &gt; 1).</summary>
    public int ChannelsProcessed { get; set; }

    /// <summary>Çekim penceresinin BAŞLANGICI (UTC) — siparişler bu tarihten şimdiye kadar tarandı. Şeffaflık:
    /// pazaryerinin döndürdüğü tarih aralığı sessizce daraltılmaz; kullanıcı hangi geçmişin tarandığını görür.</summary>
    public DateTime? FetchedSinceUtc { get; set; }

    /// <summary>Uyarılar (LOKALİZE) — anahtarsız uzak kayıt atlandı / TRY çözülemedi vb. (sessiz geçilmez).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// NÖTR sipariş uygulaması — ortak sipariş paneli (tüm kanallar tek grid) + pazaryerinden SALT-OKUMA çekim (O0).
/// FİŞ YOK, REZERVASYON YOK, STOK HAREKETİ YOK, pazaryerine YAZMA YOK. Company-owned (sunucu <c>ICurrentCompany</c> zorlar).
/// </summary>
public interface IOrderAppService : IApplicationService
{
    /// <summary>Ortak sipariş listesi — TÜM kanalların siparişleri, server-side filtre/sıralama (kanal/durum/tarih).</summary>
    Task<PagedResultDto<OrderListDto>> GetListAsync(OrderListRequestDto input);

    /// <summary>Bir siparişin basit detayı (başlık + satırlar).</summary>
    Task<OrderDto> GetAsync(Guid id);

    /// <summary>Bir Trendyol kanalının siparişlerini pazaryerinden çeker (salt GET) → nötr Order'a idempotent upsert.
    /// İkinci çağrı dublike üretmez; durumu/satırları günceller. FİŞ/REZERVASYON/STOK'a HİÇ dokunmaz.</summary>
    Task<OrderFetchResultDto> FetchOrdersAsync(Guid salesChannelId);

    /// <summary>Şirketin TÜM bağlı kanallarının (Trendyol + N11) siparişlerini çeker (tek düğme; kanal-başına dolaşma
    /// yok) — sonuç raporu birleştirilir.</summary>
    Task<OrderFetchResultDto> FetchAllOrdersAsync();
}
