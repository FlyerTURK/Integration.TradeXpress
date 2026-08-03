using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Tek sayfalık çekim sonucu — kanal-agnostik. <see cref="TotalCount"/>/<see cref="PageCount"/> kanalın kendi
/// sayfalama bilgisinden gelir → kuyruk kör döngü kurmaz, kaç tur kaldığını bilir.
///
/// <para><b><see cref="RateLimited"/> bir HATA DEĞİL, bir CEVAPTIR.</b> N11 ürün sorularını dakikada bir kez
/// listeletir; kota dolduğunda dönen <c>accessLimit.reached</c> istisnaya çevrilseydi worker turu "başarısız"
/// sayılır, log gürültüsü üretir ve kuyruk ilerlemesi kaybolurdu. Bu bayrak kuyruğa "bu tur boş geçti, aynı işi
/// bir sonraki turda tekrar dene" der: <see cref="Items"/> boştur ve sayaçlar anlamsızdır.</para>
/// </summary>
public sealed record RemoteQuestionPage(
    IReadOnlyList<RemoteQuestion> Items,
    int TotalCount,
    int PageCount,
    bool RateLimited)
{
    /// <summary>Kota duvarına çarpmış tur — boş sayfa + <see cref="RateLimited"/> işareti. Sayaçlar 0'dır
    /// (kanal bu turda hiçbir sayım bildirmedi); kuyruk bunları "0 kayıt var" diye YORUMLAMAMALIDIR.</summary>
    public static RemoteQuestionPage FromRateLimit()
    {
        return new RemoteQuestionPage(Array.Empty<RemoteQuestion>(), 0, 0, true);
    }
}
