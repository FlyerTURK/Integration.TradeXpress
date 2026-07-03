namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// MDI sekmelerinin (WASM) framework tarafındaki soyutlaması.
/// Alt bileşenler, kapanma öncesi kontrollerini (kirli form) buraya kaydeder.
/// </summary>
public interface IMdiTab
{
    Guid Id { get; }
    /// <summary>Sekmenin iç URL'i (kaynak doğruluk) — edit host, yeni kayıt kaydedilince
    /// "/entity/new" → "/entity/{id}" retarget'i için okur.</summary>
    string Url { get; }
    Func<Task<bool>>? CanCloseAsync { get; set; }
}
