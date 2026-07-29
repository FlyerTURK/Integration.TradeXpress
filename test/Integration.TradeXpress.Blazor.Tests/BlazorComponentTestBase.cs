using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Volo.Abp.Authorization;
using Volo.Abp.Security.Claims;

namespace Integration.TradeXpress.Blazor.Tests;

/// <summary>
/// Blazor bileşen testlerinin ortak zemini — bileşenler GERÇEKTEN render edilir (DevExpress ağacı dahil).
///
/// <para><b>Neden bu katman var:</b> Blazor'da bir bileşene tanımsız parametre vermek, eksik bir servisi
/// enjekte etmeye çalışmak ya da şablon içinde async işleyici kullanmak DERLEME hatası DEĞİLDİR — hepsi
/// çalışma anında patlar ve kullanıcının ekranında circuit'i düşürür. 2026-07-27/28'de aynı sınıf hata iki kez
/// canlıda görüldü (<c>GridLinkColumn.MinWidth</c>, <c>NumericSpinEdit.ReadOnly</c>). Metin tabanlı konvansiyon
/// testi (<c>RazorComponentParameterTests</c>) bunları yakalar ama YALNIZ bildiği bileşenler için; gerçek render
/// hepsini yakalar.</para>
///
/// <para><b>Kurulum kararları:</b></para>
/// <list type="bullet">
/// <item><b>JSInterop LOOSE:</b> DevExpress bileşenleri render sırasında JS çağırır. Katı kipte her çağrı için
/// sahte kurmak gerekirdi; bu testlerin amacı JS davranışı değil BİLEŞEN AĞACININ ayakta kalması.</item>
/// <item><b>Lokalizasyon sahtesi anahtarı AYNEN döndürür:</b> çeviri doğruluğu ayrı bir testin işi
/// (<c>LocalizationParityTests</c>); burada önemli olan <c>L[...]</c> çağrısının patlamaması.</item>
/// <item><b>Servisler NSubstitute ile:</b> bileşen testinin sunucuya gitmesi gerekmez; amaç render.</item>
/// </list>
/// </summary>
public abstract class BlazorComponentTestBase : BunitContext
{
    protected BlazorComponentTestBase()
    {
        // DevExpress bileşenleri kendi servislerini ister (tema, boyut, JS aracıları).
        Services.AddDevExpressBlazor();

        // ABP lokalizasyonu: bileşenler CrudComponentBase üzerinden IStringLocalizerFactory ister.
        Services.AddSingleton<IStringLocalizerFactory>(new PassThroughLocalizerFactory());
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(PassThroughLocalizer<>));

        // Paylaşılan bileşenlerin (DrillList/CrudLayout) [Inject] ile istediği uygulama servisleri.
        // Davranışları sınanmıyor — amaç bileşen ağacının kurulabilmesi.
        AddSubstitute<IGridExportAssemblyLoader>();
        AddSubstitute<IEntityChangeNotifier>();
        AddSubstitute<IViewOpener>();
        AddSubstitute<IPopupService>();
        AddSubstitute<IUiStateService>();
        // Liste sayfaları (CrudPageBase/CrudToolbar) tenant/şirket bağlamını [Inject] ile ister.
        AddSubstitute<Volo.Abp.MultiTenancy.ICurrentTenant>();
        AddUiInteraction();

        // İzin kontrolü yapan bileşenler (LookupComboBox.CreatePolicy/UpdatePolicy) ABP'nin
        // IAbpAuthorizationService'ini bekler — düz IAuthorizationService sahtesi "should implement" diye
        // patlıyor. Testlerde amaç render olduğundan HER İZİN VERİLİR.
        Services.AddSingleton<IAuthorizationService>(new AlwaysGrantedAuthorizationService());

        // DevExpress ve Blazor'un JS köprüsü — render sırasında çağrılır, davranışı sınamıyoruz.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // DevExpress'in tarayıcı-bağımlı ORTAM servisleri sahteleriyle değiştirilir. JS çağrısını taklit
        // etmek yerine servisin kendisini değiştirmek doğru yoldur (gerekçe: DevExpressTestEnvironment).
        Services.AddDevExpressTestEnvironment();
    }

    /// <summary>Bir servisi sahtesiyle kaydeder (bileşen <c>[Inject]</c> ile isteyecek).</summary>
    protected TService AddSubstitute<TService>()
        where TService : class
    {
        var substitute = Substitute.For<TService>();
        Services.AddSingleton(substitute);
        return substitute;
    }

    /// <summary>Kullanıcı etkileşimi servisleri (toast/onay) — render sırasında enjekte edilir.</summary>
    protected void AddUiInteraction()
    {
        if (Services.All(d => d.ServiceType != typeof(IUiInteractionService)))
        {
            AddSubstitute<IUiInteractionService>();
        }
    }

    /// <summary>Anahtarı olduğu gibi döndüren lokalizasyon — çeviri DOĞRULUĞU burada sınanmaz.</summary>
    private sealed class PassThroughLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource)
        {
            return new PassThroughLocalizer();
        }

        public IStringLocalizer Create(string baseName, string location)
        {
            return new PassThroughLocalizer();
        }
    }

    private sealed class PassThroughLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, arguments.Length == 0 ? name : string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Array.Empty<LocalizedString>();
        }
    }

    private sealed class PassThroughLocalizer<T> : PassThroughLocalizerBase, IStringLocalizer<T>
    {
    }

    private class PassThroughLocalizerBase : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, arguments.Length == 0 ? name : string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Array.Empty<LocalizedString>();
        }
    }

    /// <summary>Testlerde her izni veren sahte — ABP'nin <c>IAbpAuthorizationService</c> sözleşmesini de
    /// karşılar (bileşenler <c>AsAbpAuthorizationService()</c> ile ona daralttığı için düz sahte yetmez).</summary>
    private sealed class AlwaysGrantedAuthorizationService : IAbpAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }

        public ClaimsPrincipal? CurrentPrincipal => new ClaimsPrincipal(new ClaimsIdentity());

        public IServiceProvider ServiceProvider => throw new NotSupportedException();
    }
}