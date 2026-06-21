namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// MDI sekmelerinin (WASM) framework tarafındaki soyutlaması.
/// Alt bileşenler, kapanma öncesi kontrollerini (kirli form) buraya kaydeder.
/// </summary>
public interface IMdiTab
{
    Guid Id { get; }
    Func<Task<bool>>? CanCloseAsync { get; set; }
}
