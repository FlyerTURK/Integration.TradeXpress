namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// MDI sekme açma soyutlaması (framework seviyesi) — uygulamadaki TabManager bunu uygular.
/// CrudLayout, edit formunu sekmede açma (Tab modu) için bunu enjekte eder; böylece framework
/// uygulamanın somut TabManager tipine bağımlı olmaz.
/// </summary>
public interface IMdiTabOpener
{
    /// <summary>Aynı URL'li sekme varsa aktive eder, yoksa yeni iç sekme açar.</summary>
    Task OpenOrActivateAsync(string url, string title, string? icon = null);

    /// <summary>Yapısal başlıkla (3-satır caption) açar/aktive eder — açan taraf başlığı BAŞTAN doğru
    /// kurabilsin diye. Örn. yeni kayıt sekmesi doğrudan "Yeni {Entity}" ile açılır; aksi halde bileşen
    /// mount olup <see cref="UpdateTabHeader"/>'ı çağırana kadar düz "{Entity}" görünüp sonra değişirdi.</summary>
    Task OpenOrActivateAsync(string url, TabHeaderData headerData);

    /// <summary>Edit sayfası, model yüklenince/dirty değişince sekmesinin yapısal başlığını günceller
    /// (3-satır caption + dirty "*"). Bilinmeyen id → no-op.</summary>
    void UpdateTabHeader(Guid tabId, TabHeaderData header);

    /// <summary>SplitView'da embedded edit, başlığı EZMEDEN sadece dirty bayrağını set eder (liste tab'ına "*").
    /// Bilinmeyen id → no-op.</summary>
    void SetTabDirty(Guid tabId, bool isDirty);

    /// <summary>Edit sekmesini kapat (Kaydet&Kapat / Sil sonrası). CanCloseAsync guard'ını çalıştırır;
    /// reddedilirse false döner (açık kalır). Yeni yığın (CrudEditHost) tab-modunda kapatmak için kullanır.</summary>
    Task<bool> TryCloseAsync(Guid tabId);

    /// <summary>Sekmenin URL'ini günceller (bilinmeyen id → no-op). Yeni kayıt kaydedilince edit host
    /// sekmeyi "/entity/new" → "/entity/{id}" retarget eder — böylece listeden Düzelt aynı kayda İKİNCİ
    /// sekme açmaz (OpenOrActivateAsync URL eşleşmesiyle mevcut sekmeyi aktive eder).</summary>
    void UpdateTabUrl(Guid tabId, string url);

    /// <summary>Sekmenin sayfa-içi görünüm durumunu (JSON) günceller ve kalıcılaştırır — restore'da
    /// <see cref="IMdiTab.PageState"/> üzerinden geri okunur. null = temizle. Bilinmeyen id → no-op.
    /// Ham JSON yerine <see cref="TabPageState.Write{T}"/> kullanın (boyut tavanı + serileştirme tek yerde).</summary>
    void UpdateTabState(Guid tabId, string? stateJson);
}
