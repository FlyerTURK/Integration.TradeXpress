using System;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Kanal soru listesinin TEK SAYFALIK çekim isteği — kanal-agnostik. Sayfa döngüsü İSTEMCİDE DEĞİL, senkron
/// kuyruğundadır: N11 bu ucu <b>dakikada 1 çağrıya</b> kısar (canlı keşif 2026-08-01) ve paralellik limiti aşmaz
/// (3 eşzamanlı çağrıdan 2'si <c>accessLimit</c> aldı). Bu yüzden "hepsini çek" diye bir istek biçimi YOKTUR —
/// her tur tek bir sayfa ister.
///
/// <para><b><see cref="OnlyOpen"/> = açık/kapalı ekseni:</b> N11 arama filtresi yalnız <c>OPEN</c>/<c>CLOSED</c>
/// kabul eder. <c>OPEN</c> tarihsiz çalışır; <c>CLOSED</c> ise <see cref="StartDate"/> ZORUNLUDUR (aksi hâlde
/// <c>SELLER_API.nullParam</c>) ve aralık sınırlıdır (1 ay çalışıyor, ~6,5 yıl reddedildi) → geçmiş taraması
/// AY AY yapılır.</para>
///
/// <para><b>Tarihler İŞ TARİHİDİR</b> (gün hassasiyetli, saat/timezone semantiği YOK): kanala <c>dd/MM/yyyy</c>
/// olarak gider. UTC↔yerel kaydırması UYGULANMAZ — gün kayması yasağı (CLAUDE.md zaman kuralı).</para>
///
/// <para><b><see cref="PageIndex"/> 0-TABANLIDIR</b> (N11 <c>currentPage</c> ile birebir; canlı doğrulandı).
/// <see cref="PageSize"/> 100'e kadar kabul edilir.</para>
/// </summary>
public sealed record ChannelQuestionQuery(
    bool OnlyOpen,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageIndex,
    int PageSize);
