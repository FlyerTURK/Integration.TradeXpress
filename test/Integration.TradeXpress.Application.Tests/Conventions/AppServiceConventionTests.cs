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
}
