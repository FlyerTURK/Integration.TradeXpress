namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// Bir MDI sekmesinin İÇERİĞİNİN "yükleniyor" durumu — sekme açıldığında veri gelene kadar
/// yükleniyor paneli göstermek için.
///
/// <para><b>Neden sayaç (refcount), tek bir bool değil:</b> aynı sekmede birden çok yükleyici
/// yaşayabiliyor (SplitCrudView'de iç içe iki CrudLayout). Tek bool ile ilk tamamlanan paneli
/// erken kapatır, kullanıcı yarı dolu ekran görürdü.</para>
///
/// <para><b>Neden bilet IDisposable:</b> yükleme bitmeden bileşen imha edilirse (sekme kapandı,
/// sağ-tık Yenile ile yeniden mount oldu) panelin sonsuza asılı kalmaması MEKANİK olarak garanti
/// altına alınır — `using`/`Dispose` unutulsa bile bileşenin kendi Dispose'u bileti kapatır.</para>
///
/// <para><b>Kalıcılaştırılmaz:</b> durum circuit ömürlüdür. F5 sonrası her sekme yeniden "ilk
/// açılış" sayılır ve panel tekrar görünür — istenen davranış budur.</para>
/// </summary>
public sealed class TabContentLoad
{
    private int _pending;

    /// <summary>Açık bilet varken true. Panel görünürlüğü buna bağlanır.</summary>
    public bool IsLoading
    {
        get
        {
            return Volatile.Read(ref _pending) > 0;
        }
    }

    /// <summary>Yükleme durumu GERÇEKTEN değiştiğinde (0↔1 geçişi) tetiklenir; aradaki
    /// bilet alıp bırakmalar gereksiz render doğurmasın diye sessiz geçilir.</summary>
    public event Action? Changed;

    /// <summary>Yeni bir yükleme bileti açar. Bileti Dispose etmek yüklemeyi tamamlanmış sayar.</summary>
    public TabLoadTicket Begin()
    {
        var after = Interlocked.Increment(ref _pending);
        if (after == 1)
        {
            Changed?.Invoke();
        }

        return new TabLoadTicket(this);
    }

    internal void Release()
    {
        var after = Interlocked.Decrement(ref _pending);
        if (after == 0)
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>
/// <see cref="TabContentLoad.Begin"/> ile alınan yükleme bileti. Dispose IDEMPOTENT'tir:
/// aynı bilet iki kez kapatılsa da sayaç bozulmaz (bileşen hem normal akışta hem Dispose'da
/// emniyet amaçlı kapattığı için bu durum NORMAL, istisna değil).
/// </summary>
public sealed class TabLoadTicket : IDisposable
{
    private TabContentLoad? _owner;

    internal TabLoadTicket(TabContentLoad owner)
    {
        _owner = owner;
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner != null)
        {
            owner.Release();
        }
    }
}
