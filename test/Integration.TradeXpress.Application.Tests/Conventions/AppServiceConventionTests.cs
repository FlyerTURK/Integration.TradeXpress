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

    // Commodity statik-mapping BORCU TAMAMEN ÖDENDİ: AssayOffice (referans) + Service/Scrap/Metal/Future/Jewelry/
    // Stone/UserScopedGrant → hepsi Mapperly'ye çevrildi. Aşağıdaki tek isim BORÇ DEĞİL → MEŞRU İSTİSNA:
    //   VoucherAppService.MapLine(VoucherLine) — basit full-entity→DTO DEĞİL. VoucherLineDto kompozit/çok-kaynaklı:
    //   entity'nin yalnız PERSISTED alt-kümesi + voucher-header bağlamı (CompanyId/BranchId/VoucherDate...) +
    //   okuma-anı çözülen *UnitCode + bullion + running-balance. Mapperly'ye zorlamak ~55 MapperIgnoreTarget /
    //   zayıf strateji getirir, netlik kazandırmaz; üstelik name-match, MapLine'ın BİLEREK atladığı alanları
    //   map'leyip davranış değiştirebilir. YENİ basit static mapper bu listeye GİREMEZ → yine kırmızı.
    private static readonly HashSet<string> StaticMapperExceptions = new(StringComparer.Ordinal)
    {
        "VoucherAppService",
    };

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
