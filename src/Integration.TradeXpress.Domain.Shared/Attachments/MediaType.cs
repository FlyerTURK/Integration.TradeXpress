namespace Integration.TradeXpress.Attachments;

/// <summary>Medya varlığının türü — görsel ya da video. Grid/edit sunumu buna göre (img vs video + poster/▶).</summary>
public enum MediaType : byte
{
    Image = 0,
    Video = 1,
}
