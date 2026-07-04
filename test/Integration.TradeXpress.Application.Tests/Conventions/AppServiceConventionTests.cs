using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Integration.Framework.Application;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// AppService konvansiyonlarının MEKANİK güvenlik ağı (governance, faz 2). KIRMIZIYSA bir AppService
/// dokümante kuralı çiğnemiştir. Reflection'la görünmeyen gövde-içi kurallar (ham exception vb.) faz 3 = Roslyn analyzer.
/// </summary>
public class AppServiceConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(CurrencyUnitAppService).Assembly;

    // Statik-mapping BORCU TAMAMEN ÖDENDİ — istisna listesi BOŞ. Eski tek istisna VoucherAppService.MapLine
    // idi; o eşleme artık VoucherLineDtoFactory'de yaşıyor (AppService değil → tarama dışı) ve Mapperly'ye
    // bilinçli çevrilmeme gerekçesi o dosyanın başında dokümante. YENİ basit static mapper bu listeye
    // GİREMEZ → yine kırmızı.
    private static readonly HashSet<string> StaticMapperExceptions = new(StringComparer.Ordinal);

    [Fact]
    public void Entity_to_dto_mapping_must_use_Mapperly_not_hand_rolled_static_helpers()
    {
        // Kural (viewmodel-getdto-direct / mapper standardı): entity→DTO eşlemesi Mapperly = ObjectMapper.Map ile;
        // AppService'te ELLE statik mapper YASAK (statik mapping = anti-pattern, kullanıcı 2026-06-28 işaret etti).
        // Tespit (dar/false-positive'siz): TEK 'IEntity' parametresi alıp '*Dto' dönen statik metot = elle entity→dto.
        //   HARİÇ (meşru, Mapperly yapamaz): projection-row→dto (param Row, IEntity DEĞİL) ve
        //   entity + dış-veri→dto enrichment (≥2 parametre).
        var violations = new List<string>();

        var appServiceTypes = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true } && t.Name.EndsWith("AppService", StringComparison.Ordinal));

        foreach (var type in appServiceTypes)
        {
            var methods = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                var isHandRolledEntityToDto =
                    ps.Length == 1
                    && typeof(IEntity).IsAssignableFrom(ps[0].ParameterType)
                    && m.ReturnType.Name.EndsWith("Dto", StringComparison.Ordinal);

                if (isHandRolledEntityToDto && !StaticMapperExceptions.Contains(type.Name))
                {
                    violations.Add($"{type.Name}.{m.Name}({ps[0].ParameterType.Name}) → {m.ReturnType.Name}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki AppService'ler entity→DTO'yu ELLE statik metotla eşliyor (ObjectMapper/Mapperly olmalı):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct()));
    }

    // MEŞRU İSTİSNA listesi — katalog CRUD servisi olup Create/Update/Delete policy'si BİLEREK atanmayanlar.
    // Başlangıçta BOŞ; yeni istisna ancak dosya-içi dokümante gerekçeyle girebilir (testi gevşetme YASAK).
    private static readonly HashSet<string> CatalogPolicyExceptions = new(StringComparer.Ordinal);

    [Fact]
    public void Host_catalog_crud_services_must_assign_create_update_delete_policies()
    {
        // Kural (permission tutarlılığı, Metal deseni): HostCatalogCrudAppService türevi her servis
        // Create/Update/Delete policy'lerini ctor'da atamalı (okuma/liste serbest kalabilir — [Authorize] yeter)
        // YA DA sınıf-seviyesi [Authorize("...")] policy'siyle komple kapılanmalı (Country/Cash/Parity deseni).
        // Doğrulama: servis null bağımlılıklarla instantiate edilir (ctor'lar yalnız atama yapar) ve
        // ABP'nin protected CreatePolicyName/UpdatePolicyName/DeletePolicyName property'leri okunur.
        var violations = new List<string>();

        var catalogServiceTypes = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && DerivesFromHostCatalogCrud(t));

        foreach (var type in catalogServiceTypes)
        {
            if (CatalogPolicyExceptions.Contains(type.Name))
            {
                continue;
            }

            // Sınıf-seviyesi policy'li [Authorize] tüm metotları zaten kapılar (Default izni CRUD'u kapsar).
            var classPolicy = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Any(a => !string.IsNullOrEmpty(a.Policy));
            if (classPolicy)
            {
                continue;
            }

            var instance = InstantiateWithNullDependencies(type);
            foreach (var policyProperty in new[] { "CreatePolicyName", "UpdatePolicyName", "DeletePolicyName" })
            {
                if (string.IsNullOrEmpty(ReadPolicy(instance, policyProperty)))
                {
                    violations.Add($"{type.Name}.{policyProperty} atanmamış");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki katalog CRUD AppService'leri Create/Update/Delete policy'lerini atamıyor "
            + "(Metal deseni: ctor'da TradeXpressPermissions.X.Create/Update/Delete):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool DerivesFromHostCatalogCrud(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HostCatalogCrudAppService<,,,,,>))
            {
                return true;
            }
        }

        return false;
    }

    private static object InstantiateWithNullDependencies(Type type)
    {
        // Ctor'lar konvansiyon gereği yalnız alan ataması + policy ataması yapar → null bağımlılık güvenli.
        var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        return ctor.Invoke(new object?[ctor.GetParameters().Length]);
    }

    private static string? ReadPolicy(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        property.ShouldNotBeNull($"ABP CrudAppService policy property'si bulunamadı: {propertyName}");
        return (string?)property.GetValue(instance);
    }
}
