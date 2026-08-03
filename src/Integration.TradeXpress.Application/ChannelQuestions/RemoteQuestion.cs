using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Pazaryerinden çekilmiş HAM soru — kanal-agnostik taşıyıcı (<c>RemoteOrder</c> emsali). Kanal istemcisi bunu
/// üretir, senkron katmanı <see cref="ChannelQuestion.ApplyRemote"/> ile aggregate'e uygular. Bu tip HİÇBİR
/// eşleme/karar taşımaz: ham alanları olduğu gibi geçirir.
///
/// <para><b>Alanların çoğu LİSTEDEN gelmez</b> (N11 liste/detay asimetrisi, canlı keşif 2026-08-01): liste öğesinde
/// yalnız id/ürün/başlık/soru/cevap/görsel vardır; <see cref="CustomerName"/>, <see cref="CustomerEmail"/>,
/// <see cref="QuestionDate"/>, <see cref="RemoteStatus"/>, <see cref="IsPublic"/> YALNIZ detay çağrısıyla dolar.
/// Liste yolundan gelen kayıtta bu alanlar <c>null</c>'dır — "bilgi yok" demektir, "boş" değil; uygulayan katman
/// null alanla mevcut değeri EZMEMELİDİR.</para>
///
/// <para><b><see cref="ExistingAnswer"/> LİSTEDE de vardır</b> — "bu soru cevaplanmış mı" bilgisi detay çağrısı
/// YAPMADAN öğrenilir. Dakikada-1 kota altında bu, detay çağrısını gerçekten gereken satırlara saklamanın
/// ana kaldıracıdır.</para>
///
/// <para><b><see cref="RemoteStatus"/> HAM metindir</b> (kısıtsız): arama filtresi yalnız OPEN/CLOSED kabul etse de
/// detaydaki <c>status</c> serbest metindir → nötr eşleme SENKRON katmanında ve TOLERANT yapılır
/// (tanınmayan → <see cref="ChannelQuestionStatus.Unknown"/> + log). İstemci burada fail-fast YAPMAZ; tek bir
/// bilinmeyen durum metni yüzünden tüm çekim düşmemelidir.</para>
/// </summary>
public sealed record RemoteQuestion(
    string RemoteQuestionId,
    string? RemoteProductId,
    string? ProductTitle,
    string? Subject,
    string? QuestionText,
    string? CustomerName,
    string? CustomerEmail,
    DateTime? QuestionDate,
    string? RemoteStatus,
    bool? IsPublic,
    string? ExistingAnswer,
    IReadOnlyList<string> ImageUrls)
{
    /// <summary>Yalnız uzak kimliği yazar. <b>Record'un üretilmiş ToString'i BİLEREK ezilir:</b> varsayılan hâli
    /// TÜM üyeleri (müşteri adı + e-posta + soru gövdesi) düz metne döker; bu nesne log/exception satırlarında
    /// geçtiğinde kişisel veri sızardı.</summary>
    public override string ToString()
    {
        return RemoteQuestionId;
    }
}
