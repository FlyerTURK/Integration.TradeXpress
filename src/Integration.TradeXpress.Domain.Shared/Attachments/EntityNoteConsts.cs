namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik not (EntityNote — herhangi bir entity'ye EntityName+EntityId ile bağlı sade metin not)
/// alan sınırları.</summary>
public static class EntityNoteConsts
{
    public const int EntityNameMaxLength = 128;    // teknik: sahip entity tipi adı (ör. "Good")
    public const int TitleMaxLength      = 200;    // opsiyonel başlık
    public const int TextMaxLength       = 4000;   // zorunlu not metni (uzun serbest metin)
}
