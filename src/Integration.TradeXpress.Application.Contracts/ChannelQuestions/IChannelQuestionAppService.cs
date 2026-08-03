using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// NÖTR müşteri sorusu uygulaması — ORTAK GELEN KUTUSU (tüm kanallar tek grid, kanal yalnız discriminator).
/// <c>IOrderAppService</c> emsalinin soru karşılığıdır: company-owned + per-tenant, kapsamı sunucu zorlar
/// (global query filter), kayıtlar YALNIZ kanaldan çekilir.
///
/// <para><b>Bu servis pazaryerine HİÇ ÇIKMAZ.</b> Soru OLUŞTURMA/SİLME yok (katalog değil — kaynak
/// pazaryeridir) ve cevabın pazaryerine GÖNDERİMİ yok: cevap TradeXpress içinde yazılır, kanala GİTMEZ;
/// teslim durumu <see cref="ChannelQuestionListDto.AnswerState"/>'te görünür ve push açılana kadar hiçbir
/// satır <see cref="ChannelAnswerState.Sent"/> olmaz. <see cref="RequestSyncAsync"/> de bir ÇEKİM DEĞİL,
/// yalnız arka plan kuyruğuna bırakılan bir işarettir (gerekçe orada).</para>
/// </summary>
public interface IChannelQuestionAppService : IApplicationService
{
    /// <summary>Ortak soru gelen kutusu — server-side sayfalı, merkezi <c>ListRequestDto</c> motoruyla
    /// (whitelist'li filtre/sıralama/arama) + gelen kutusuna özel tipli eksenler (kanal/durum/okundu/bekleyen).
    /// VARSAYILAN SIRA: <see cref="ChannelQuestionListDto.FirstSeenAt"/> ARTAN — en eski bekleyen üstte (SLA).</summary>
    Task<PagedResultDto<ChannelQuestionListDto>> GetListAsync(ChannelQuestionListRequestDto input);

    /// <summary>Tek bir sorunun güncel satırı (cevap panelinin kaynağı). Kapsam dışı id kayıt YOKMUŞ gibi
    /// davranır (global filter + repository <c>GetAsync</c> → EntityNotFound).</summary>
    Task<ChannelQuestionListDto> GetAsync(Guid id);

    /// <summary>Cevap taslağını YEREL olarak yazar/günceller — pazaryerine HİÇBİR ŞEY GÖNDERMEZ.
    /// Gönderilmiş cevap yeniden yazılamaz (pazaryerinde cevap düzenleme operasyonu yoktur). Güncel satırı döner.</summary>
    Task<ChannelQuestionListDto> WriteAnswerAsync(Guid id, ChannelQuestionAnswerInput input);

    /// <summary>Okundu/okunmadı işaretler (gelen kutusu okunmamış sayacı). Güncel satırı döner.</summary>
    Task<ChannelQuestionListDto> SetReadAsync(Guid id, bool isRead);

    /// <summary>
    /// Çekimi SIRAYA ALIR — <b>hiçbir şey çekmez, pazaryerine GİTMEZ.</b> Çalışılan şirketin kanalları için
    /// arka plan kuyruğuna "sıradaki turda bunları öncelikli çek" işareti bırakır ve ANINDA döner.
    ///
    /// <para><b>Neden çekmiyor:</b> N11 ürün sorularını hesap başına DAKİKADA BİR KEZ listelemeye izin verir ve
    /// bu kotayı eşzamanlılık AŞMAZ (2026-08-01 canlı ölçümü: 3 paralel çağrıdan 2'si <c>accessLimit</c>).
    /// Kotayı harcayan TEK merkez arka plan işçisidir. Sayfayı açan her kullanıcı doğrudan pazaryerine gitseydi
    /// ikinci kullanıcı hata alır ve işçinin turunu da yerdi.</para>
    ///
    /// <para><b>Dönüş tipi neden <c>Task</c> (<c>int</c> DEĞİL):</b> bu çağrının sonucunda çekilmiş bir satır
    /// YOKTUR. Bir sayı döndürmek çağırana "şu kadar soru geldi" yalanını söylerdi. Aynı sebeple kullanıcıya
    /// verilecek geri bildirim <b>"sıraya alındı"</b> olmalıdır — "çekiliyor" DEĞİL: çekim en erken bir
    /// sonraki işçi turunda gerçekleşir.</para>
    /// </summary>
    Task RequestSyncAsync();
}
