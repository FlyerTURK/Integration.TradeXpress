using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Entity yazım konvansiyonlarının MEKANİK güvenlik ağı (governance). Bu test KIRMIZIYSA bir entity
/// dokümante kuralı çiğnemiştir — kurallar yalnız memory'de/insan dikkatinde kalmasın, <c>dotnet test</c>'te
/// yakalansın. Kaynak kurallar: entity-yazim-kurallari, abp-id-guidgenerator. Yeni kural çıkınca buraya assert ekle.
/// <para>Domain assembly'sindeki TÜM Guid-anahtarlı entity'leri reflection ile tarar; ihlal listesini tek mesajda verir.</para>
/// </summary>
public class EntityConventionTests
{
    private static readonly Assembly DomainAssembly = typeof(CurrencyUnit).Assembly;

    private static IEnumerable<Type> EntityTypes() => DomainAssembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEntity<Guid>).IsAssignableFrom(t));

    // MEŞRU İSTİSNA (allow-list) — ctor'da 'Guid id': aggregate-child olup AYNI işlemde ledger-sync'e id ÖNCEDEN
    // lazım (VoucherLine.Id → BalanceLedgerEntry; pre-assigned GuidGenerator). Save-then-read'e refactor edilene kadar
    // bilinçli muaf. YENİ entity'ler bu listeye GİREMEZ — yeni Guid-id ihlali yine kırmızı olur.
    private static readonly HashSet<string> GuidIdCtorAllowList = new() { "VoucherLine", "BalanceLedgerEntry" };

    [Fact]
    public void Entity_ctor_must_not_take_id_or_tenantId_ABP_assigns_them()
    {
        // Kural (abp-id-guidgenerator): Id VE TenantId'yi ABP otomatik atar → entity ctor'una parametre olarak KOYMA.
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var p in ctor.GetParameters())
                {
                    if (string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.Name}: ctor 'tenantId' parametresi almamalı (ABP CurrentTenant'tan atar).");
                    }

                    if (p.ParameterType == typeof(Guid)
                        && string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase)
                        && !GuidIdCtorAllowList.Contains(type.Name))
                    {
                        violations.Add($"{type.Name}: ctor 'Guid id' parametresi almamalı (ABP GuidGenerator atar).");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity ctor'ları Id/TenantId parametresi alıyor (ABP atar, kaldırılmalı):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Distinct()));
    }

    // MEŞRU İSTİSNA — public setter allow-list: aggregate-child entity'ler veya EF mapping zorunlulukları
    private static readonly HashSet<string> PublicSetterAllowList = new() { "VoucherLine", "BalanceLedgerEntry" };

    [Fact]
    public void Entity_must_not_have_parameterless_Activate_or_Deactivate_use_SetActive_bool()
    {
        // Kural (entity-conventions §SetActive): IsActive toggle için SetActive(bool) kullan;
        // Activate()/Deactivate() çift-metot YOK (SetAsHeadquarters/SetAsDefault ile hizalı).
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            var declared = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (declared.Any(m => m.Name == "Activate" && m.GetParameters().Length == 0))
            {
                violations.Add($"{type.Name}: 'void Activate()' yasak — 'SetActive(bool)' kullan (entity-conventions §SetActive).");
            }

            if (declared.Any(m => m.Name == "Deactivate" && m.GetParameters().Length == 0))
            {
                violations.Add($"{type.Name}: 'void Deactivate()' yasak — 'SetActive(bool)' kullan (entity-conventions §SetActive).");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity'ler parametre almayan Activate/Deactivate metodu kullanıyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Entity_with_Code_property_must_override_ToString()
    {
        // Kural (entity-conventions §ToString): string Code property'si olan her entity
        // ToString() override etmeli → 'return Code' (log/exception okunabilirliği).
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            var codeProp = type.GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
            if (codeProp == null || codeProp.PropertyType != typeof(string))
            {
                continue; // Code property yoksa kural uygulanmaz
            }

            var hasDeclaredToString = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.Name == "ToString" && m.GetParameters().Length == 0);

            if (!hasDeclaredToString)
            {
                violations.Add($"{type.Name}: 'Code' property'si olan entity ToString() override etmeli → 'return Code;' (entity-conventions §ToString).");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity'ler ToString() override etmiyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Entity_properties_must_not_have_public_setters()
    {
        // Kural (entity-conventions §protected-set): entity property'leri 'protected set' kullanmalı;
        // public setter EF proxy/encapsulation'ı bozar.
        var violations = new List<string>();

        foreach (var type in EntityTypes())
        {
            if (PublicSetterAllowList.Contains(type.Name))
            {
                continue;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var prop in props)
            {
                var setter = prop.GetSetMethod(nonPublic: false); // yalnız public setter döner
                if (setter != null)
                {
                    violations.Add($"{type.Name}.{prop.Name}: public setter var, 'protected set' kullan (entity-conventions §protected-set).");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki entity property'leri public setter içeriyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
