using System;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.HttpApi.Host.MultiCompany;

/// <summary>
/// HTTP API (:44388) için ŞİRKET BAĞLAMI — daima <see cref="Guid.Empty"/> sentinel.
///
/// <para><b>Kapatılan açık:</b> API modülünde hiç <see cref="ICompanyContextProvider"/> kaydı yoktu →
/// <c>NullCompanyContextProvider</c> devredeydi → <c>ICurrentCompany.Id</c> DAİMA <c>null</c> → DbContext'in
/// şirket filtresi <b>PERMISSIVE</b> (konsolide) kola düşüyordu. Yani tenant'ı olan bir token'la API'ye giren
/// istemci, tenant içindeki <b>TÜM şirketlerin</b> kayıtlarını okuyabiliyor, güncelleyebiliyor ve
/// silebiliyordu — Blazor UI'da imkânsız olan şey HTTP API üzerinden serbestti.</para>
///
/// <para><b>Neden null değil sentinel:</b> <c>null</c> filtrede "kısıtlama yok" demektir (§ konsolide okuma);
/// güvenlik açığının ta kendisi odur. <see cref="WorkingCompanyScope"/>'un "erişim yok" temsilcisi
/// <see cref="Guid.Empty"/>'dir: şirkete ait (<c>ICompanyOwned</c>) kayıtlar GÖRÜNMEZ, host/tenant ortak
/// katalogları (para birimi, ülke, nakit) görünür kalır. Fail-closed.</para>
///
/// <para><b>Kapsam:</b> bu faz kimlikten working-context TÜRETMEZ (kullanıcının izinli şirketlerini API'de
/// çözmek, WorkingContextService zincirini API'ye taşımak demektir; API'nin bugünkü tek tüketicisi Swagger).
/// Konsolide okumaya meşru ihtiyacı olan sunucu-içi işler zaten <c>ICurrentCompany.Change</c> override'ını
/// kullanıyor ve bu provider'dan etkilenmez — bypass bilinçli ve açıktır.</para>
/// </summary>
public class ApiCompanyContextProvider : ICompanyContextProvider, ITransientDependency
{
    public Guid? GetCurrentCompanyId()
    {
        return Guid.Empty;
    }
}
