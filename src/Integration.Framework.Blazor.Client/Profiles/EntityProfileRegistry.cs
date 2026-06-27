using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.Framework.Blazor.Client.Profiles;

/// <summary>
/// <see cref="IEntityProfileRegistry"/> varsayılan uygulaması: DI'dan enjekte edilen tüm
/// <see cref="EntityProfile"/>'leri DTO tipine ve Key'e göre indeksler. Singleton (profiller stateless).
/// </summary>
public sealed class EntityProfileRegistry : IEntityProfileRegistry
{
    private readonly IReadOnlyList<EntityProfile> _all;
    private readonly Dictionary<Type, EntityProfile> _byDto = new();
    private readonly Dictionary<string, EntityProfile> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public EntityProfileRegistry(IEnumerable<EntityProfile> profiles)
    {
        _all = profiles.ToList();
        foreach (var p in _all)
        {
            _byKey[p.Key] = p;
            _byDto[p.GetDtoType] = p;
            _byDto[p.ListDtoType] = p;
        }
    }

    public IReadOnlyList<EntityProfile> All => _all;

    public EntityProfile? Find(Type dtoType) => _byDto.TryGetValue(dtoType, out var p) ? p : null;

    public EntityProfile Get(Type dtoType) => Find(dtoType)
        ?? throw new InvalidOperationException($"'{dtoType.FullName}' için kayıtlı EntityProfile yok.");

    public EntityProfile? FindByKey(string key) => _byKey.TryGetValue(key, out var p) ? p : null;

    public EntityProfile GetByKey(string key) => FindByKey(key)
        ?? throw new InvalidOperationException($"'{key}' anahtarlı EntityProfile yok.");
}
