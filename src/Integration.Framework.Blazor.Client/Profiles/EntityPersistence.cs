namespace Integration.Framework.Blazor.Client.Profiles;

/// <summary>
/// Bir entity'nin persistence kademesi (iki-kademeli drill modeli — kullanıcı kararı).
/// </summary>
public enum EntityPersistence
{
    /// <summary>Kendi AppService'i + Coordinator'ı olan KALICI entity. Standalone tam-sayfa liste VEYA parent
    /// içinde persistent drill olarak AYNI makineyle çalışır — fark sadece "host".</summary>
    Persistent,

    /// <summary>Parent grafının IN-MEMORY düğümü (ör. CompanyGraphDto.Branches). Kendi servisi/state'i YOK;
    /// parent'ın koleksiyonunu mutasyona uğratır, commit PARENT'ta (graph save).</summary>
    InMemoryGraph
}
