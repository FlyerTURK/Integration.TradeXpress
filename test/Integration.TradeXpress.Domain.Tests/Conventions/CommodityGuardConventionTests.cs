using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// <b>EMTİA SİLME GUARD'I BAYPAS EDİLEMEZ</b> (2026-08-05).
///
/// <para><b>Neden bu test var — yaşanmış bir tuzak:</b> guard önce
/// <c>CommodityCatalogAppService.BeforeDeleteAsync</c>'e konmuştu. Ama Good/Metal/Jewelry/Stone o hook'u
/// override ediyor ve <b>hiçbiri <c>base</c>'i çağırmıyordu</b> → guard dört ailede SESSİZCE baypas oldu,
/// yalnız override etmeyen üçünde çalıştı. Entegrasyon testi yakaladı; guard genişletme noktasının ÜSTÜNE
/// (<c>DeleteAsync</c>) taşındı.</para>
///
/// <para>Bu test o taşınmayı KİLİTLER: bir emtia servisi <c>DeleteAsync</c>'i override ederse guard yine
/// atlanabilir hale gelir ve arıza yine SESSİZ olur (silme başarılı görünür, reçete çözülemeyen emtiaya
/// bakar kalır). Reflection değil KAYNAK taranır — <c>ConventionSource</c> deseni.</para>
///
/// <para><b>İstisna gerekirse:</b> allow-list'e ekleyip GEREKÇESİNİ yaz. "Testi gevşetme" ya da
/// <c>#pragma</c> ile susturma YASAK (CLAUDE.md §2/§8).</para>
/// </summary>
public class CommodityGuardConventionTests
{
    /// <summary>Guard'ın yaşadığı taban — türevleri bu dosyanın adıyla bulunur.</summary>
    private const string GuardBaseName = "CommodityCatalogAppService";

    /// <summary>Guard'ı taşıyan taban ile ara taban: kendileri override edebilir (guard ZATEN buradadır).</summary>
    private static readonly HashSet<string> AllowedToOverrideDelete = new()
    {
        "src/Integration.TradeXpress.Application/Commodities/CommodityCatalogAppService.cs",
        // FollowingUnitCatalogAppService guard'ı taşımaz ama zinciri kırmadığı sürece override edebilir;
        // bugün etmiyor — listede olması ileride meşru bir ihtiyaç doğarsa gerekçe yazılacak yer bırakır.
    };

    private static readonly Regex OverridesDelete = new(
        @"override\s+(async\s+)?Task\s+DeleteAsync\s*\(", RegexOptions.Compiled);

    private static readonly Regex InheritsGuardBase = new(
        @":\s*" + GuardBaseName + @"\s*<", RegexOptions.Compiled);

    [Fact]
    public void Commodity_catalog_services_must_not_override_DeleteAsync()
    {
        var offenders = new List<string>();

        foreach (var path in ConventionSource.EnumerateSource("*AppService.cs"))
        {
            var text = File.ReadAllText(path);
            if (!InheritsGuardBase.IsMatch(text) || !OverridesDelete.IsMatch(text))
            {
                continue;
            }

            var relative = ConventionSource.RelativePath(path);
            if (!AllowedToOverrideDelete.Contains(relative))
            {
                offenders.Add(relative);
            }
        }

        offenders.ShouldBeEmpty(
            "Emtia katalog servisi DeleteAsync'i override ederse reçete kullanım guard'ı ATLANIR ve silme "
            + "sessizce geçer. Türev temizliğini BeforeDeleteAsync'te yapın (base.DeleteAsync onu zaten çağırır). "
            + "Gerçekten gerekiyorsa AllowedToOverrideDelete'e GEREKÇESİYLE ekleyin. İhlal: "
            + string.Join(", ", offenders));
    }

    /// <summary>Guard'ın kendisi yerinde mi — taban <c>DeleteAsync</c>'i override edip kullanım sorgusunu
    /// çağırmalı. Biri guard'ı "sadeleştirme" adına kaldırırsa bu test kırmızı verir.</summary>
    [Fact]
    public void Guard_base_still_checks_recipe_usage_before_deleting()
    {
        var path = Path.Combine(
            ConventionSource.SrcRoot,
            "Integration.TradeXpress.Application", "Commodities", GuardBaseName + ".cs");

        File.Exists(path).ShouldBeTrue($"Guard tabanı bulunamadı: {path}");

        var text = File.ReadAllText(path);
        OverridesDelete.IsMatch(text).ShouldBeTrue(
            "Guard tabanı DeleteAsync'i override etmiyor — silme guard'ı devre dışı kalmış olur.");
        text.ShouldContain(
            "EnsureNotUsedInRecipesAsync",
            customMessage: "DeleteAsync override'ı var ama kullanım kontrolü çağrılmıyor.");
    }
}
