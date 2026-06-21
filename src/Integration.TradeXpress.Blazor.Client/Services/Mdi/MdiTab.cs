using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Integration.Framework.Blazor.Client.Services.Mdi;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

public enum TabKind { Internal, External }

/// <summary>Tek bir MDI sekmesinin tanımı. Id = @key kimliği; RefreshNonce yenilemede değişir.</summary>
public sealed class MdiTab : IMdiTab
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string Title { get; set; }
    public string? IconCssClass { get; set; }
    public TabKind Kind { get; set; } = TabKind.Internal;

    /// <summary>Edit sekmeleri için yapısal başlık (3-satır caption + dirty). Düz menü/liste tab'larında null.
    /// Edit sayfası model yüklenince <see cref="ITabManager.UpdateTabHeader"/> ile doldurur; kalıcılaştırılmaz.</summary>
    public TabHeaderData? Header { get; internal set; }

    /// <summary>SplitView'da liste tab'ı için dirty bayrağı: embedded edit kirliyken düz Title'a "*" eklenir
    /// (Header'ı EZMEDEN). Standalone edit'te dirty <see cref="Header"/>.IsDirty'den gelir; bu kullanılmaz.</summary>
    public bool IsDirty { get; internal set; }

    /// <summary>İç sayfa için göreli URL (kaynak doğruluk), harici için mutlak URL.</summary>
    public required string Url { get; set; }

    /// <summary>İç sayfa tipi (DynamicComponent ile render). Harici sekmede null.</summary>
    public Type? PageType { get; set; }

    /// <summary>DynamicComponent'e splat edilen parametreler — route + query, C# property adıyla key'lenir.</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    public bool IsPinned { get; set; }

    /// <summary>Yenile komutunda değişir → DynamicComponent @key bozulur → sayfa yeniden init olur.</summary>
    public Guid RefreshNonce { get; set; } = Guid.NewGuid();

    /// <summary>Sekmenin kapatılabilip kapatılamayacağını (ör. kirli form kontrolü) belirleyen asenkron delege.</summary>
    public Func<Task<bool>>? CanCloseAsync { get; set; }
}
