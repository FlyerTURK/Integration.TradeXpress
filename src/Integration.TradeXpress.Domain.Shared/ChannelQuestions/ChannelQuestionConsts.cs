namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>Kanal sorusu alan sınırları. Uzak kaynaklı metinler KIRPILIR (fail-fast değil): pazaryeri
/// sınırlarını değiştirdiğinde çekim durmamalı, satır görünür kalmalı.</summary>
public static class ChannelQuestionConsts
{
    /// <summary>Kanaldaki soru kimliği (N11 xs:long, Trendyol sayısal id) — metin olarak saklanır (kanal-agnostik).</summary>
    public const int RemoteQuestionIdMaxLength = 64;

    /// <summary>Kanaldaki ürün kimliği — snapshot (yerel ürün eşleşmese de soru anlamlı kalır).</summary>
    public const int RemoteProductIdMaxLength = 64;

    /// <summary>Ürün başlığı snapshot'ı — yerel ürün silinse bile soru satırı neyi sorduğunu bilir.</summary>
    public const int ProductTitleMaxLength = 512;

    /// <summary>Soru başlığı (N11 questionSubject).</summary>
    public const int SubjectMaxLength = 256;

    /// <summary>Soru gövdesi. Pazaryeri sınırı belgelenmemiş; cömert tutulur (kırpma son çare).</summary>
    public const int QuestionTextMaxLength = 4000;

    /// <summary>Cevap gövdesi. N11 WSDL'i <c>answer</c> için sınır TANIMLAMIYOR (düz xs:string) ve panel sınırı
    /// belgelenmemiş — bu değer bizim güvenli tavanımızdır, canlı ölçümden sonra daraltılabilir.</summary>
    public const int AnswerTextMaxLength = 4000;

    /// <summary>Soruyu soran müşterinin adı — grid/cevap ekranında GÖSTERİLİR.</summary>
    public const int CustomerNameMaxLength = 256;

    /// <summary>Müşterinin iletişim adresi (e-posta) — saklanır ve gösterilir (gerekçe <c>ChannelQuestion</c>
    /// tip özetinde). 320 = RFC 5321 azami e-posta uzunluğu.</summary>
    public const int CustomerEmailMaxLength = 320;

    /// <summary>Ham kanal durumu — denetim için saklanan serbest metin (tolerant eşlemenin kaynağı).</summary>
    public const int RemoteStatusMaxLength = 64;

    /// <summary>Gönderim hatası özeti (push açıldığında dolar).</summary>
    public const int AnswerPushErrorMaxLength = 1024;
}
