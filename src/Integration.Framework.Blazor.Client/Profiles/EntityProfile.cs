using System;

namespace Integration.Framework.Blazor.Client.Profiles;

/// <summary>
/// Bir entity'nin TÜM UI bağlamlarına (listeleme / popup / drill / split / edit) yetecek TEK tarifi — XAF "BOModel"
/// karşılığı. Kimlik (ikon/başlık/parent/permission), DTO tipleri, edit host tipi ve persistence modu burada
/// TEK KAYNAKTIR; her bağlam bunu tüketir (ayrı komponentte tekrar yazmaz). Kolon/toolbar/edit-layout sonraki
/// fazlarda eklenir. Profiller stateless (yalnız metadata) → DI'da singleton, registry'de toplanır.
/// </summary>
public abstract class EntityProfile
{
    /// <summary>Profil anahtarı (parent referansı + lookup için); ör. "Vault", "Branch".</summary>
    public abstract string Key { get; }

    /// <summary>Edit/detay DTO tipi (registry indeksleme + host bağlama).</summary>
    public abstract Type GetDtoType { get; }

    /// <summary>Liste (grid) DTO tipi (registry indeksleme).</summary>
    public abstract Type ListDtoType { get; }

    /// <summary>Entity ikonu (CSS sınıfı; ör. TradeXpressIcons.Vault).</summary>
    public abstract string IconCssClass { get; }

    /// <summary>Tekil başlık localization anahtarı (ör. "Entity:Vault").</summary>
    public abstract string CaptionKey { get; }

    /// <summary>Çoğul / liste başlığı localization anahtarı (ör. "Menu:Vaults"); verilmezse <see cref="CaptionKey"/>.</summary>
    public virtual string PluralCaptionKey => CaptionKey;

    /// <summary>İzin ön-eki (ör. TradeXpressPermissions.Vaults.Default); .Create/.Update/.Delete türetilir. Yoksa null.</summary>
    public virtual string? PermissionPrefix => null;

    /// <summary>Parent entity profilinin <see cref="Key"/>'i (başlık/menü hiyerarşisi için); kök entity'de null.</summary>
    public virtual string? ParentProfileKey => null;

    /// <summary>Edit host komponent tipi — <c>IViewOpener</c> bunu açar (ör. typeof(VaultEditHost)).</summary>
    public abstract Type EditComponentType { get; }

    /// <summary>Liste/edit route kökü (ör. "/vaults"); menü/derin-link için. Yalnız drill ile erişilirse null.</summary>
    public virtual string? RouteBase => null;

    /// <summary>Persistence kademesi: kalıcı (kendi servisi) mi, parent-graf in-memory düğüm mü.</summary>
    public virtual EntityPersistence Persistence => EntityPersistence.Persistent;
}

/// <summary>
/// Tipli profil tabanı: <see cref="GetDtoType"/>/<see cref="ListDtoType"/>'ı typeof'tan otomatik doldurur
/// (alt sınıf yalnız kimlik/edit/persistence verir). Tipli selector/kolon/toolbar SONRAKİ fazlarda eklenir.
/// </summary>
public abstract class EntityProfile<TGetDto, TListDto, TKey> : EntityProfile
{
    public sealed override Type GetDtoType => typeof(TGetDto);
    public sealed override Type ListDtoType => typeof(TListDto);
}
