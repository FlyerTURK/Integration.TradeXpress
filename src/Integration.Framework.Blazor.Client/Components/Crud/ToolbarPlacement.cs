namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>Agnostic <c>EntityEditForm</c>'da toolbar'ın nereye çizileceği (sunum kararı, host'tan param).</summary>
public enum ToolbarPlacement
{
    /// <summary>Üstte — gezinilebilir/MDI/tam-sayfa edit için (◄► + Save grubu doğal).</summary>
    Top,

    /// <summary>Altta (footer) — hızlı popup / lookup edit için (modal dip aksiyonu).</summary>
    Bottom,
}
