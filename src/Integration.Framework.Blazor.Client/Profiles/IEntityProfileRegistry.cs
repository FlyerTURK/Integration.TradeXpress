using System;
using System.Collections.Generic;

namespace Integration.Framework.Blazor.Client.Profiles;

/// <summary>
/// Tip→profil ve key→profil araması. DI'da kayıtlı tüm <see cref="EntityProfile"/>'leri toplar; her bağlam
/// (list page / edit host / menü) entity'nin kimliğini buradan çözer (tek kaynak).
/// </summary>
public interface IEntityProfileRegistry
{
    /// <summary>DTO tipinden (Get veya List) profili döner; yoksa null.</summary>
    EntityProfile? Find(Type dtoType);

    /// <summary>DTO tipinden profili döner; yoksa fail-fast.</summary>
    EntityProfile Get(Type dtoType);

    /// <summary><see cref="EntityProfile.Key"/>'den profili döner; yoksa null (parent çözümü).</summary>
    EntityProfile? FindByKey(string key);

    /// <summary><see cref="EntityProfile.Key"/>'den profili döner; yoksa fail-fast.</summary>
    EntityProfile GetByKey(string key);

    /// <summary>Kayıtlı tüm profiller.</summary>
    IReadOnlyList<EntityProfile> All { get; }
}
