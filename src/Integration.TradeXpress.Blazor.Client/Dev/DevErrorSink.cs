using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.Blazor.Client.Dev;

/// <summary>
/// Yakalanan tüm runtime hatalarının tutulduğu tek merkez (WASM tek kullanıcı → Singleton).
/// JS tarafı (DeveloperErrorPanel.Report) ve .NET tarafı (ErrorBoundary / loglar) buraya yazar;
/// panel <see cref="Changed"/> ile dinler. Bellekte son <see cref="MaxEntries"/> kayıt tutulur.
/// </summary>
public sealed class DevErrorSink
{
    private const int MaxEntries = 200;
    private readonly List<DevErrorEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>Liste değişince tetiklenir (panel re-render için).</summary>
    public event Action? Changed;

    public IReadOnlyList<DevErrorEntry> Entries
    {
        get { lock (_lock) { return _entries.ToArray(); } }
    }

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    public void Add(DevErrorEntry entry)
    {
        lock (_lock)
        {
            // Ardışık aynı (level+message+stack) hata yeni satır açmaz, sayacı artar.
            var last = _entries.Count > 0 ? _entries[^1] : null;
            if (last != null && last.Level == entry.Level && last.Message == entry.Message && last.Stack == entry.Stack)
            {
                last.Count++;
            }
            else
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
        Changed?.Invoke();
    }
}
