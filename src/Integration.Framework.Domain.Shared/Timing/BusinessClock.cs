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

    /// <summary>Wall-clock BUGÜN — yalnız gün (saat=00:00), <see cref="DateTimeKind.Unspecified"/>.
    /// <c>DateTime.Today</c>'in kaymasız muadili: date-only iş tarihi (DueDate/AsOf/dönem sınırı) default'ları
    /// ve rapor tarih filtreleri buradan üretilir → ABP UTC normalizasyonu günü kaydıramaz.</summary>
    public static DateTime Today()
    {
        return DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
    }

    /// <summary>Verilen değeri date-only wall-clock'a indirger: saat atılır (<c>.Date</c>) ve
    /// Kind <see cref="DateTimeKind.Unspecified"/>'e sabitlenir. <c>[DisableDateTimeNormalization]</c> ile
    /// işaretli date-only alanların entity-içi SSOT normalizeri (giriş Local/Utc gelse bile gün kaymaz).</summary>
    public static DateTime AsBusinessDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    }
}
