namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Doküman boyutunu (byte) insan-okur biçime çevirir (B / KB / MB) — doküman drill'i + edit alanları ortak
/// tüketir (DRY).</summary>
public static class EntityDocumentSize
{
    public static string Format(long bytes)
    {
        const long mb = 1024 * 1024;
        const long kb = 1024;

        if (bytes >= mb)
        {
            return $"{bytes / (double)mb:0.##} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / (double)kb:0.##} KB";
        }

        return $"{bytes} B";
    }
}
