using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Timing;

/// <summary>
/// Kullanıcının tarayıcı/masaüstü saat dilimini (IANA id, ör. <c>"Europe/Istanbul"</c>) devre (circuit)
/// boyunca tutar. Blazor Server'da sunucu tarafı kullanıcının yerel saatini bilmez → değer ilk render'da
/// (<c>OnAfterRenderAsync</c>) JS interop ile bir kez yakalanıp <see cref="Set"/> ile buraya yazılır.
/// <para><b>Scoped:</b> her SignalR devresi (kullanıcı oturumu) ayrı örnek → farklı kullanıcılar farklı
/// saat dilimlerini karışmadan taşır. <see cref="IDisplayTimeConverter"/> bu accessor'ı okur (TEK kaynak).</para>
/// <para><b>Fallback:</b> TZ henüz yakalanmadıysa (<see cref="IsResolved"/> false) yerel dönüşüm UTC'yi
/// olduğu gibi bırakır — sunucu saat dilimi keyfîdir, UTC "henüz yerelleştirilmedi" durumunun dürüst
/// karşılığıdır ve yalnız ilk render karesi boyunca geçerlidir.</para>
/// </summary>
public class UserTimeZoneAccessor : IScopedDependency
{
    /// <summary>Yakalanan IANA saat dilimi kimliği; henüz yakalanmadıysa <c>null</c>.</summary>
    public string? IanaId { get; private set; }

    /// <summary>Tarayıcı saat dilimi bir kez başarıyla yakalandı mı.</summary>
    public bool IsResolved
    {
        get { return !string.IsNullOrWhiteSpace(IanaId); }
    }

    /// <summary>Yakalanan IANA kimliğini yazar (ilk render'daki JS interop sonucu). Boş/null yok sayılır.</summary>
    public void Set(string? ianaId)
    {
        if (!string.IsNullOrWhiteSpace(ianaId))
        {
            IanaId = ianaId;
        }
    }
}
