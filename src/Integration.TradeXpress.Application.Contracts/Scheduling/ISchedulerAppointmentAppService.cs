using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Scheduling;

/// <summary>
/// Takvim randevusu servisi — DevExpress DxScheduler için. <b>Company-scoped</b> (sunucu zorlar). Standart
/// CRUD yerine scheduler akışına uygun: tüm (şirket) randevuları getir + ekle/güncelle/sil. İleride tarih-aralığı
/// yüklemesi, kaynak (resource) ve filtreler eklenecek.
/// </summary>
public interface ISchedulerAppointmentAppService : IApplicationService
{
    /// <summary>Çalışılan şirketin tüm randevuları (DxScheduler AppointmentsSource'u doldurur).</summary>
    Task<List<SchedulerAppointmentDto>> GetListAsync();

    /// <summary>Yeni randevu — kaydedilen Id atanmış DTO döner (scheduler kaynak nesnesine yazılır).</summary>
    Task<SchedulerAppointmentDto> CreateAsync(SchedulerAppointmentDto input);

    Task<SchedulerAppointmentDto> UpdateAsync(SchedulerAppointmentDto input);

    Task DeleteAsync(Guid id);
}
