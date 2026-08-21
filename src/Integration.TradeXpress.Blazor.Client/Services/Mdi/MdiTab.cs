using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    /// Edit sayfası model yüklenince <see cref="ITabManager.UpdateTabHeader"/> ile doldurur. Header alanları
    /// da KALICILAŞTIRILIR — restore'da bileşen yüklenene kadar şeritte doğru başlık görünür.</summary>
    public TabHeaderData? Header { get; internal set; }

    /// <summary>Sayfa-içi görünüm durumu (JSON) — sekmeyle birlikte kalıcılaşır; restore'da sayfa
    /// <c>TabPageState.TryRead</c> ile okur. Yalnız görünüm/filtre/kimlik; model DTO'su ASLA.</summary>
    public string? PageState { get; internal set; }

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

    /// <summary>Sekme içeriğinin yükleme durumu (yükleniyor paneli). KALICILAŞMAZ — PersistedTab'a yazılmaz.</summary>
    public TabContentLoad Load { get; } = new();

    /// <summary>Yükleniyor panelinin bağlanacağı sekme panelinin (pane) DOM id'si. Kalıcı bir kimlik DEĞİL,
    /// mevcut <see cref="Id"/>'den türetilir (yeni Guid üretilmez).</summary>
    public string PaneElementId
    {
        get
        {
            return "mdi-pane-" + Id.ToString("N");
        }
    }

    /// <summary>DxLoadingPanel.PositionTarget için CSS seçici — "#mdi-pane-...".</summary>
    public string PaneTargetSelector
    {
        get
        {
            return "#" + PaneElementId;
        }
    }
}
