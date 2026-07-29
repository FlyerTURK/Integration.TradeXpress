namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// MDI sekmelerinin framework tarafındaki soyutlaması.
/// Alt bileşenler, kapanma öncesi kontrollerini (kirli form) buraya kaydeder.
/// </summary>
public interface IMdiTab
{
    Guid Id { get; }
    /// <summary>Sekmenin iç URL'i (kaynak doğruluk) — edit host, yeni kayıt kaydedilince
    /// "/entity/new" → "/entity/{id}" retarget'i için okur.</summary>
    string Url { get; }
    Func<Task<bool>>? CanCloseAsync { get; set; }

    /// <summary>Sekme İÇERİĞİNİN yükleme durumu — sekme ilk açıldığında veri gelene kadar yükleniyor
    /// paneli göstermek için. Yükleyen bileşen <see cref="TabContentLoad.Begin"/> ile bilet alır, veri
    /// yerleşince bileti kapatır. KALICILAŞTIRILMAZ (circuit ömürlü): F5 sonrası her sekme yeniden
    /// "ilk açılış" sayılır.</summary>
    TabContentLoad Load { get; }

    /// <summary>Sekmeyle birlikte kalıcılaşan sayfa-içi GÖRÜNÜM durumu (JSON) — restore edilen sekme
    /// açılışta buradan okur (<see cref="TabPageState.TryRead{T}"/>). Yazma: sayfa değişiklik anında
    /// <see cref="IMdiTabOpener.UpdateTabState"/> ile iter (push-model — UpdateTabHeader deseniyle aynı).
    /// KURAL: yalnız görünüm/filtre/kimlik taşır; kaydedilmemiş form verisi (model DTO'su) ASLA —
    /// bayat taslağın sessizce dirilmesi, bilinçli kayıp bildiriminden daha tehlikelidir.</summary>
    string? PageState { get; }
}
