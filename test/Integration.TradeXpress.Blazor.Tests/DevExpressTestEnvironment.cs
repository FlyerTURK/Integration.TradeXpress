using System;
using System.Globalization;
using System.Threading.Tasks;
using DevExpress.Blazor.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Tests;

/// <summary>
/// DevExpress'in ÇALIŞMA ORTAMI servislerini test için sahteleriyle değiştirir.
///
/// <para><b>Neden gerekli:</b> DevExpress bileşenleri açılışta tarayıcıdan cihaz bilgisi ister
/// (<c>UtilsModule.getDeviceInfo</c>) ve dönen nesneyi koşulsuz okur. bUnit'in sahte JS katmanı <c>null</c>
/// döndürdüğünden orada <c>NullReferenceException</c> patlar — JS çağrısını taklit etmeye çalışmak yerine
/// ORTAM SERVİSİNİN kendisini değiştirmek doğru çözümdür (DevExpress'in kendi bUnit rehberinin izlediği yol).</para>
///
/// <para><b>Kırılganlık uyarısı:</b> <c>DevExpress.Blazor.Internal</c> ad alanı ürünün İÇ yapısıdır; public
/// olsa da sürüm yükseltmesinde değişebilir. DevExpress paketi yükseltildiğinde bu dosya derlenmezse burada
/// güncelleme gerekir — testler o zaman sessizce atlanmaz, KIRMIZI yanar (istenen davranış).</para>
/// </summary>
public static class DevExpressTestEnvironment
{
    /// <summary>Ortam servislerini sahteleriyle kaydeder (DevExpress kayıtlarından SONRA çağrılmalı).</summary>
    public static IServiceCollection AddDevExpressTestEnvironment(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentInfoFactory, TestEnvironmentInfoFactory>();
        services.AddSingleton<IEnvironmentInfo>(_ => new TestEnvironmentInfo());
        return services;
    }

    private sealed class TestEnvironmentInfoFactory : IEnvironmentInfoFactory
    {
        public IEnvironmentInfo CreateEnvironmentInfo()
        {
            return new TestEnvironmentInfo();
        }
    }

    /// <summary>
    /// Sabit, tarayıcısız ortam: masaüstü cihaz, değişmez kültür, sabit saat.
    ///
    /// <para>Saat SABİT tutulur (<c>2026-01-01</c>): tarih içeren render çıktıları makinenin saatine göre
    /// değişirse testler zamanla kendiliğinden kırılırdı.</para>
    /// </summary>
    private sealed class TestEnvironmentInfo : IEnvironmentInfo
    {
        private static readonly DateTime FixedMoment = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public bool IsWasm => false;

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public Task<ApiScheme> ApiScheme => Task.FromResult(new ApiScheme());

        public Task<DeviceInfo> DeviceInfo => Task.FromResult(new DeviceInfo(false));

        public DateTime GetDateTimeNow()
        {
            return FixedMoment.ToLocalTime();
        }

        public DateTime GetDateTimeUtcNow()
        {
            return FixedMoment;
        }

        public Task InitializeRuntime()
        {
            return Task.CompletedTask;
        }
    }
}
