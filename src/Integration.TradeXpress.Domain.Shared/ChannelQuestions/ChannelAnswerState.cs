namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// YEREL cevap teslim durumu — cevabın pazaryerine gidip gitmediğini söyler. Sorunun kanaldaki durumundan
/// (<see cref="ChannelQuestionStatus"/>) BAĞIMSIZDIR ve onunla karıştırılmamalıdır.
///
/// <para><b>Neden gün-1'de var (2026-08-01 Hakan kararı):</b> cevap yazma açık ama pazaryerine GÖNDERME kapalı.
/// Teslim durumu sonradan eklenen bir alan olsaydı, arada yazılan cevapların gerçekten gidip gitmediği
/// belirsiz kalırdı. Bu yüzden ayrım baştan modelde: kullanıcı yazdığı cevabın durumunu her zaman görür.</para>
///
/// <para><b>Operasyonel uyarı:</b> pazaryerinde cevap süresi satıcı puanına işler. Kullanıcının cevabı
/// "gönderildi" sanması gerçek zarardır — UI hiçbir yerde <see cref="Sent"/> dışında "gönderildi" demez.</para>
/// </summary>
public enum ChannelAnswerState : byte
{
    /// <summary>Henüz cevap yazılmamış.</summary>
    None = 0,

    /// <summary>Cevap yazıldı, TASLAK — kullanıcı üzerinde çalışıyor, gönderim sırasına girmedi.</summary>
    Draft = 1,

    /// <summary>Cevap tamam, gönderilmeyi bekliyor. Push açıldığında drenaj bu durumdakileri alır.
    /// Push kapalıyken kuyruk BÜYÜR — bekleyen sayacı kullanıcıya görünür tutulmalı.</summary>
    ReadyToSend = 2,

    /// <summary>Pazaryerine GERÇEKTEN gönderildi (<c>AnswerPushedAt</c> dolu). Push açılana kadar bu duruma
    /// hiçbir satır GEÇMEZ.</summary>
    Sent = 3,

    /// <summary>Gönderim denendi ve başarısız — hata <c>AnswerPushError</c>'da. Yeniden denenebilir.</summary>
    Failed = 4,
}
