namespace Integration.TradeXpress.Confirmations;

/// <summary>Teyit (Confirmation) alan sınırları — DB kolon boyutları + validasyon tek kaynağı.</summary>
public static class ConfirmationConsts
{
    /// <summary>Gönderenin açıklaması üst sınırı.</summary>
    public const int NoteMaxLength = 512;

    /// <summary>Alıcının karar (red gerekçesi / teyit notu) açıklaması üst sınırı.</summary>
    public const int DecisionNoteMaxLength = 512;

    /// <summary>Serileştirilmiş process payload'u (pending <c>VoucherLineInput</c>) üst sınırı.</summary>
    public const int PayloadMaxLength = 8192;
}
