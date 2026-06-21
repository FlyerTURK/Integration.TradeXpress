namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Toplu (batch) bir operasyonun öğe bazlı sonucu: başarılı ve başarısız öğeler.
/// </summary>
public class BatchOperationResult<T>
{
    public List<T> Succeeded { get; } = new();
    public List<(T Item, Exception Exception)> Failed { get; } = new();

    public bool HasFailures => Failed.Count > 0;

    /// <summary>En az bir öğe işlendi ve hiçbiri başarısız olmadı.</summary>
    public bool AllSucceeded => Succeeded.Count > 0 && Failed.Count == 0;

    /// <summary>Bir kısmı başarılı, bir kısmı başarısız.</summary>
    public bool IsPartial => Succeeded.Count > 0 && Failed.Count > 0;
}
