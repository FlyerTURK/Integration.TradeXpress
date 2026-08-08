using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// DI KAYIT KONVANSİYONU — "arayüzü uyguladım ama konteyner onu AÇMIYOR" tuzağı.
///
/// <para><b>Neden bu test var (2026-08-08'de yaşandı):</b> <c>CommodityStockReaderService</c> sınıfı
/// <c>ICommodityStockReader</c>'ı uyguluyor ve <c>ITransientDependency</c> taşıyordu — ama ABP'nin varsayılan
/// kaydı bir arayüzü ancak <b>sınıf adı o arayüzün adıyla BİTİYORSA</b> açar. <c>ICommodityStockReader</c> için
/// aranan sonek <c>"CommodityStockReader"</c>, sınıf adı ise <c>"...ReaderService"</c> → eşleşme yok. Sonuç:
/// arayüz hiç kaydedilmedi, onu isteyen <c>ProductStockSyncJob</c> her turda
/// <c>Cannot resolve parameter</c> ile düştü ve bu <b>14 gün</b> fark edilmedi.</para>
///
/// <para><b>Neden hiçbir mevcut ağ yakalamadı:</b> hata derleme zamanında değil, konteyner kurulumunda doğar.
/// Derleme temiz, testler yeşil, hiçbir kural kırmızı — yalnız arka plan işi sessizce ölü. Bu, projenin en
/// pahalı hata sınıfının (sessiz-yanlış) DI ayağıdır.</para>
///
/// <para><b>Kural:</b> yapılandırma-adı arayüzünü (<c>I{X}</c>) uygulayan ve ABP yaşam-döngüsü işaretçisi taşıyan
/// bir sınıf, ya adlandırma konvansiyonuna UYAR ya da <c>[ExposeServices]</c> ile arayüzü AÇIKÇA açar.
/// Üçüncü bir seçenek yok — "uygular ama açmaz" hâli her zaman kazadır.</para>
/// </summary>
public class DependencyRegistrationConventionTests
{
    /// <summary>ABP yaşam-döngüsü işaretçileri — bunlardan birini taşıyan sınıf otomatik kaydolur.</summary>
    private static readonly Type[] LifetimeMarkers =
    {
        typeof(ITransientDependency), typeof(IScopedDependency), typeof(ISingletonDependency),
    };

    /// <summary>Taranan derlemeler — TradeXpress'in kendi kodu (Framework ve ABP hariç).</summary>
    private static IEnumerable<Assembly> TradeXpressAssemblies()
    {
        yield return typeof(TradeXpressApplicationModule).Assembly;
        yield return typeof(Integration.TradeXpress.Orchestration.ICommodityStockReader).Assembly;
    }

    /// <summary>ABP'nin <c>ExposedServiceExplorer</c> kuralının birebir aynası: <c>I</c> ön eki atılır ve
    /// sınıf adının o adla BİTİP bitmediğine bakılır.</summary>
    private static bool AbpWouldExpose(Type implementation, Type serviceInterface)
    {
        var name = serviceInterface.Name;
        if (name.StartsWith("I", StringComparison.Ordinal))
        {
            name = name.Substring(1);
        }

        return implementation.Name.EndsWith(name, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_lifetime_marked_class_actually_exposes_the_interfaces_it_implements()
    {
        var ihlaller = new List<string>();

        foreach (var type in TradeXpressAssemblies().SelectMany(a => a.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
            {
                continue;
            }

            if (!LifetimeMarkers.Any(m => m.IsAssignableFrom(type)))
            {
                continue;
            }

            // [ExposeServices] varsa kayıt AÇIKÇA beyan edilmiştir — konvansiyona bakılmaz.
            if (type.GetCustomAttribute<ExposeServicesAttribute>() is not null)
            {
                continue;
            }

            // Sınıfın KENDİ beyan ettiği (miras almadığı) TradeXpress arayüzleri.
            var kendiArayuzleri = type.GetInterfaces()
                .Where(i => i.Namespace?.StartsWith("Integration.TradeXpress", StringComparison.Ordinal) == true)
                .Where(i => type.BaseType is null || !i.IsAssignableFrom(type.BaseType))
                .ToList();

            foreach (var arayuz in kendiArayuzleri.Where(i => !AbpWouldExpose(type, i)))
            {
                ihlaller.Add($"{type.Name} → {arayuz.Name} (ad konvansiyonu tutmuyor; [ExposeServices] YOK)");
            }
        }

        ihlaller.ShouldBeEmpty(
            "Bu sınıflar bir arayüzü UYGULUYOR ama konteyner o arayüzü AÇMIYOR. Arayüzü isteyen her çağıran " +
            "çalışma zamanında 'Cannot resolve parameter' ile düşer — derleme temiz kalır, hiçbir test kırmızı " +
            "olmaz. Çözüm: sınıfa [ExposeServices(typeof(IArayuz))] ekle ya da sınıfı arayüz adıyla bitecek " +
            "şekilde yeniden adlandır." + Environment.NewLine +
            string.Join(Environment.NewLine, ihlaller));
    }

    /// <summary>Yaşanan somut vakayı ayrıca pinler — genel kural gevşetilse bile bu kayıt korunsun.
    /// Stok orkestrasyonunun TAMAMI bu tek çözüme bağlı.</summary>
    [Fact]
    public void The_commodity_stock_reader_interface_is_explicitly_exposed()
    {
        var attribute = typeof(Integration.TradeXpress.Orchestration.CommodityStockReaderService)
            .GetCustomAttribute<ExposeServicesAttribute>();

        attribute.ShouldNotBeNull();
        attribute!.ServiceTypes.ShouldContain(typeof(Integration.TradeXpress.Orchestration.ICommodityStockReader));
    }
}
