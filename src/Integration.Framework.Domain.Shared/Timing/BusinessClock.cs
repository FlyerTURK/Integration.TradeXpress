using System;

namespace Integration.Framework.Timing;

/// <summary>
/// İş (duvar-saati / wall-clock) tarih-saat kaynağı. Kullanıcının GÖRDÜĞÜ yerel günü+saatini
/// <b>Kind=Unspecified</b> olarak verir — böylece ABP <c>IClock</c> (AbpClockOptions.Kind=Utc)
/// bu değeri UTC'ye çevirip gün/saat KAYDIRAMAZ.
/// <para><b>Neden gerekli:</b> <see cref="DateTime.Now"/> Kind=Local üretir; ABP save sırasında bunu
/// UTC'ye normalize eder (Türkiye'de −3s) → date-only iş tarihleri (fiş tarihi) bir önceki güne
/// kayabilir. Kaymaması gereken iş tarihleri bu kaynaktan üretilir ve saklandıkları alan
/// <c>[DisableDateTimeNormalization]</c> ile işaretlenir (SSOT: giriş Unspecified + alan normalize-dışı).</para>
/// <para>Sistem/audit zaman damgaları (CreationTime vb.) DEĞİL — onlar ABP <c>IClock</c> (UTC) ile kalır.
/// Bu kaynak yalnız kullanıcının wall-clock iş tarihleri içindir.</para>
/// </summary>
public static class BusinessClock
{
    /// <summary>Şu anki wall-clock (yerel gün + saat), <see cref="DateTimeKind.Unspecified"/>.
    /// ABP normalizasyonu bu Kind'ı çevirmez; saat KORUNUR, gün kaymaz.</summary>
    public static DateTime Now()
    {
        return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
    }
}
