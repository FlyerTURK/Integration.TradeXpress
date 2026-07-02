using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Integration.TradeXpress.Conventions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Navigation ↔ foreign-key-id tutarlılığının MEKANİK güvenlik ağı (governance). Politika ([[ef-navigation-id-coupling]]):
/// <b>aggregate'ler ARASI referans = SADECE id</b> (CurrencyUnit/Company/Branch/AssayOffice... hepsi ayrı root,
/// nav ile foreign aggregate yüklemek DDD anti-pattern'i + bu projede zaten kullanılmıyor). Navigation YALNIZ
/// aggregate-İÇİ (child→root) meşrudur ve <see cref="AllowNavigationAttribute"/> ile işaretlenir.
/// <para>Domain assembly'sindeki TÜM ABP entity'lerini reflection ile tarar; ihlal listesini tek mesajda verir.</para>
/// </summary>
public class NavigationConventionTests
{
    private static readonly Assembly DomainAssembly = typeof(CurrencyUnit).Assembly;

    // ABP Entity'sinden türeyen (IEntity) somut sınıflar — yalnız bu Domain'inkiler (ABP/Identity base'leri değil).
    private static IEnumerable<Type> EntityTypes() => DomainAssembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEntity).IsAssignableFrom(t));

    // Foreign-key-id SAYILMAYAN id'ler: kendi PK'sı + ABP altyapı id'leri.
    private static readonly HashSet<string> NonForeignKeyIdNames = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "CreatorId", "LastModifierId", "DeleterId",
    };

    private static bool IsForeignKeyId(PropertyInfo p)
        => p.Name.EndsWith("Id", StringComparison.Ordinal)
           && (p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
           && !NonForeignKeyIdNames.Contains(p.Name);

    // Single-reference navigation = tipi başka bir ABP entity olan property (koleksiyonlar IEntity DEĞİL → hariç).
    private static bool IsSingleNavigation(PropertyInfo p)
        => typeof(IEntity).IsAssignableFrom(p.PropertyType);

    private static bool IsAllowed(PropertyInfo p)
        => p.GetCustomAttribute<AllowNavigationAttribute>() is not null;

    private static PropertyInfo[] PublicProps(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// (1) <c>XId</c> foreign-key'i AYNI adlı bir navigation (<c>X</c>) ile birlikte TUTAMAZ → aggregate'ler arası
    /// id-only. İstisna: <see cref="AllowNavigationAttribute"/> (aggregate-içi).
    /// </summary>
    [Fact]
    public void Foreign_key_id_must_not_be_paired_with_a_same_named_navigation()
    {
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            var props = PublicProps(type);
            foreach (var idProp in props.Where(IsForeignKeyId))
            {
                var navName = idProp.Name[..^2]; // "FollowingUnitId" → "FollowingUnit"
                var nav = props.FirstOrDefault(p => p.Name == navName && IsSingleNavigation(p));
                if (nav is not null && !IsAllowed(nav))
                {
                    violations.Add(
                        $"{type.Name}.{navName} ({nav.PropertyType.Name}): '{idProp.Name}' ile birlikte navigation tutuyor — " +
                        "aggregate'ler arası id-only olmalı (nav'ı kaldır; aggregate-içiyse [AllowNavigation] ekle).");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity'ler XId + aynı adlı navigation çifti tutuyor (id-only ihlali):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct()));
    }

    /// <summary>
    /// (2) Bir navigation (<c>X</c>) MUTLAKA karşılığında <c>XId</c> property'sine sahip olmalı → orphan nav YASAK.
    /// İstisna: <see cref="AllowNavigationAttribute"/>.
    /// </summary>
    [Fact]
    public void Navigation_property_must_have_a_matching_foreign_key_id()
    {
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            var props = PublicProps(type);
            foreach (var nav in props.Where(IsSingleNavigation))
            {
                if (IsAllowed(nav)) continue;
                var idName = nav.Name + "Id";
                if (props.All(p => p.Name != idName))
                {
                    violations.Add(
                        $"{type.Name}.{nav.Name} ({nav.PropertyType.Name}): navigation var ama '{idName}' yok — " +
                        "orphan navigation YASAK (eşleşen id ekle; meşruysa [AllowNavigation]).");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity'ler eşleşen XId olmadan navigation tutuyor (orphan nav):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct()));
    }
}
