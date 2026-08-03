namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// NÖTR (kanal-agnostik) soru durumu — ortak filtre/görüntü ekseni. Ham kanal durumu ayrıca
/// <c>ChannelQuestion.RemoteStatus</c>'ta saklanır (denetim + eşlemenin kaynağı).
///
/// <para><b>Neden nötr eksen:</b> N11 yalnız iki değer tanır (OPEN/CLOSED) ama detay yanıtında durum alanı
/// KISITSIZ metindir; Trendyol beş üstü durum + şikâyet/red akışı taşır. Kanal değerlerini doğrudan
/// saklasaydık ortak grid filtresi kanal başına ayrışırdı. Tanınmayan değer <see cref="Unknown"/>'a düşer —
/// fail-fast DEĞİL: pazaryeri yeni bir durum eklediğinde çekim komple patlamamalı, satır görünür kalmalı.</para>
/// </summary>
public enum ChannelQuestionStatus : byte
{
    /// <summary>Eşlenemeyen ham durum — satır yine de listelenir (bkz. tip özeti).</summary>
    Unknown = 0,

    /// <summary>Müşteri sordu, cevap bekliyor (N11 OPEN). SLA sayacının işlediği durum.</summary>
    Pending = 1,

    /// <summary>Kanalda cevaplanmış (N11 CLOSED / cevap alanı dolu).</summary>
    Answered = 2,

    /// <summary>Pazaryeri tarafından kapatılmış/reddedilmiş — cevap beklenmiyor (Trendyol'da açık akış;
    /// N11'de karşılığı yok, oradan gelmez).</summary>
    Closed = 3,
}
