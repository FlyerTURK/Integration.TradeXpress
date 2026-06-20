using System;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// SplitCrudView'in iç seçim durumu tutucusu (CurrentId + HasSelection + SelectionChanged).
/// new() ile oluşturulur; DI'ye kayıtlı değil, dışarıdan tüketilmez.
/// </summary>
public sealed class EditSession<TKey>
{
    public TKey? CurrentId { get; private set; }
    public bool HasSelection { get; private set; }

    public event Action? SelectionChanged;

    public void Select(TKey? id)
    {
        CurrentId = id;
        HasSelection = true;
        SelectionChanged?.Invoke();
    }

    public void SelectNew()
    {
        CurrentId = default;
        HasSelection = true;
        SelectionChanged?.Invoke();
    }

    /// <summary>Silme sonrası paneli boş duruma sıfırlar.</summary>
    public void Reset()
    {
        CurrentId = default;
        HasSelection = false;
        SelectionChanged?.Invoke();
    }
}
