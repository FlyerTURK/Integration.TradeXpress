using System;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace Integration.Framework.Blazor.Client.Timing;

/// <summary>
/// <see cref="IDisplayTimeConverter"/> varsayılan uygulaması. Kullanıcının IANA saat dilimini
/// <see cref="UserTimeZoneAccessor"/>'dan okur, ABP <see cref="ITimezoneProvider"/> (TimeZoneConverter/TZConvert
/// tabanlı — IANA kimliklerini platform-bağımsız çözer) ile <see cref="TimeZoneInfo"/>'ya çevirir.
/// <para><b>Scoped:</b> accessor devre-başına olduğundan converter da devre-başına; çözülen
/// <see cref="TimeZoneInfo"/> aynı IANA id için devre boyunca önbelleğe alınır (tekrar lookup yok).</para>
/// </summary>
public class DisplayTimeConverter : IDisplayTimeConverter, IScopedDependency
{
    private readonly UserTimeZoneAccessor _accessor;
    private readonly ITimezoneProvider _timezoneProvider;

    // Devre-içi önbellek: IanaId sabitlendiğinde TimeZoneInfo bir kez çözülüp tutulur.
    private string? _cachedIanaId;
    private TimeZoneInfo? _cachedZone;

    public DisplayTimeConverter(UserTimeZoneAccessor accessor, ITimezoneProvider timezoneProvider)
    {
        _accessor = accessor;
        _timezoneProvider = timezoneProvider;
    }

    public DateTime ToLocal(DateTime utc)
    {
        var zone = ResolveZone();
        // ConvertTimeFromUtc, Kind=Utc veya Unspecified ister (Local ArgumentException fırlatır) →
        // gelen değeri UTC "duvar" değeri olarak Unspecified'a sabitle.
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        if (zone is null)
        {
            return asUtc;   // TZ henüz yakalanmadı → UTC'yi olduğu gibi bırak (fallback).
        }

        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone);
    }

    public DateTime? ToLocal(DateTime? utc)
    {
        if (utc is null)
        {
            return null;
        }

        return ToLocal(utc.Value);
    }

    public DateTime ToUtc(DateTime local)
    {
        var zone = ResolveZone();
        var asLocal = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone is null)
        {
            return asLocal;   // TZ henüz yakalanmadı → değeri UTC kabul et (fallback).
        }

        return TimeZoneInfo.ConvertTimeToUtc(asLocal, zone);
    }

    // Accessor'daki IANA id'yi TimeZoneInfo'ya çözer; çözülemezse (yakalanmadı/geçersiz) null → fallback.
    private TimeZoneInfo? ResolveZone()
    {
        var ianaId = _accessor.IanaId;
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return null;
        }

        if (_cachedZone is not null && string.Equals(_cachedIanaId, ianaId, StringComparison.Ordinal))
        {
            return _cachedZone;
        }

        try
        {
            _cachedZone = _timezoneProvider.GetTimeZoneInfo(ianaId);
            _cachedIanaId = ianaId;
            return _cachedZone;
        }
        catch (TimeZoneNotFoundException)
        {
            return null;   // Geçersiz/tanınmayan IANA id → fallback (UTC).
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
