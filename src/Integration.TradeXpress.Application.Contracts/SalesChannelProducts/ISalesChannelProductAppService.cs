using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Kanal-ürünlerinin BİRLEŞİK OKUMA MODELİ — üç pazaryerinin kanal-ürün kayıtları için tek liste ucu.
///
/// <para><b>Salt okumadır ve öyle KALMALIDIR:</b> yazma işleri (düzenleme, push, silme) kanalın KENDİ
/// tipli servisindedir (<c>ISalesChannelTrN11ProductAppService</c> ve kardeşleri) — çünkü yazılan şey
/// kanal-özel graftır (varyant override'ları, kategori özellikleri, SKU'lar) ve her kanalın kuralı
/// başkadır. Buraya bir "güncelle" eklemek, üç kuralı tek imzada birleştirme baskısı yaratır ve o
/// birleştirme kaçınılmaz olarak en zayıf kanalın kurallarına iner.</para>
///
/// <para><b>İki liste, tek sorgu:</b> kanal edit formundaki liste ile standalone liste AYNI ucu
/// tüketir; tek fark <c>SalesChannelId</c>'nin dolu olup olmamasıdır. Ayrı iki uç açmak, kolon/durum
/// mantığını ikiye bölerdi.</para>
/// </summary>
public interface ISalesChannelProductAppService : IApplicationService
{
    /// <summary>Birleşik, sayfalı kanal-ürün listesi (şirket kapsamı sunucuda zorlanır).</summary>
    Task<PagedResultDto<SalesChannelProductListDto>> GetListAsync(SalesChannelProductListRequestDto input);

    /// <summary>Bir kanal-ürünün GÖNDERİM GEÇMİŞİ — append-only PushHistory'nin okunuşu, en yeni üstte.
    ///
    /// <para><paramref name="channelType"/> ZORUNLUDUR: geçmiş kanal başına AYRI tabloda tutulur (üç kanalın
    /// alanları farklı) ve id tek başına hangi tabloya bakılacağını söylemez. Tipi tahmin etmek için üç tabloyu
    /// birden yoklamak, aynı Guid'in başka bir kanalda çakışması hâlinde yanlış kanalın geçmişini gösterebilirdi.</para>
    ///
    /// <para>Sayfalama YOK: tek bir kanal-ürünün geçmişi sınırlı bir kümedir ve ekran onu bir bütün olarak
    /// okutur. Kayıt sayısı büyürse (saklama politikası kararı — bkz. CLAUDE.md) buraya sayfalama eklenir.</para></summary>
    Task<List<SalesChannelProductPushHistoryDto>> GetPushHistoryAsync(Guid channelProductId, SalesChannelType channelType);
}
