using System;

namespace Integration.Framework.Blazor.Client.Timing;

/// <summary>
/// UTC zaman damgaları ile kullanıcının YEREL saati arasındaki TEK dönüşüm noktası (görüntü katmanı).
/// Uygulama genelinde timestamp'ler (CreationTime, LastModificationTime, ExchangeRate.RateDate,
/// CurrentPriceDto.RateDate gibi bir <b>an</b> ifade eden alanlar) burada UTC→yerele çevrilir.
/// <para><b>DİKKAT — date-only iş tarihleri ÇEVRİLMEZ:</b> VoucherDate / DueDate / AsOfDate /
/// ProfitResetDate wall-clock (duvar-saati) iş günleridir; zaten kullanıcının yerel günüdür, çevirmek
/// günü kaydırır. Onları doğrudan formatla, bu converter'a SOKMA.</para>
/// </summary>
public interface IDisplayTimeConverter
{
    /// <summary>UTC bir zaman damgasını kullanıcının yerel saatine çevirir (<see cref="DateTimeKind.Unspecified"/>
    /// döner — görüntü için). TZ henüz yakalanmadıysa değeri UTC olarak olduğu gibi bırakır.</summary>
    DateTime ToLocal(DateTime utc);

    /// <summary>Nullable aşırı yüklemesi; <c>null</c> ise <c>null</c> döner.</summary>
    DateTime? ToLocal(DateTime? utc);

    /// <summary>Kullanıcının yerel saatinde girilen bir değeri UTC'ye çevirir (editör girişleri için tersi yön).
    /// TZ henüz yakalanmadıysa değeri UTC kabul edip olduğu gibi bırakır.</summary>
    DateTime ToUtc(DateTime local);
}
