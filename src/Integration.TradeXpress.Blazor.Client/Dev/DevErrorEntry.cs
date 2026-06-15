using System;

namespace Integration.TradeXpress.Blazor.Client.Dev;

/// <summary>
/// Developer Error Panel'de gösterilen tek bir yakalanmış hata kaydı.
/// Kaynak hem JS (console/window/promise) hem .NET (ErrorBoundary/logger) olabilir.
/// </summary>
public sealed class DevErrorEntry
{
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>error | warn | rejection | exception</summary>
    public string Level { get; init; } = "error";

    /// <summary>js | console | dotnet</summary>
    public string Source { get; init; } = "js";

    public string Message { get; init; } = "";

    public string? Stack { get; init; }

    /// <summary>Ardışık aynı hata katlanır; kaç kez tekrarladığı.</summary>
    public int Count { get; set; } = 1;
}
