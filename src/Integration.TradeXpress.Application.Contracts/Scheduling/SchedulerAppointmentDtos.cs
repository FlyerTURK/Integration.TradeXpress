using System;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Scheduling;

/// <summary>
/// Takvim randevusu DTO'su — hem okuma sonucu hem de DevExpress <c>DxScheduler</c>'ın <b>kaynak nesnesi</b>
/// (AppointmentsSource elemanı). Alan adları entity ile aynı → Mapperly otomatik eşler; DxScheduler
/// AppointmentMappings bu alan adlarına bağlanır. Parametresiz ctor + public set: scheduler nesneyi yerinde mutasyona uğratır.
/// </summary>
public class SchedulerAppointmentDto
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(SchedulerAppointmentConsts.SubjectMaxLength)]
    public string Subject { get; set; } = string.Empty;

    [StringLength(SchedulerAppointmentConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    [StringLength(SchedulerAppointmentConsts.LocationMaxLength)]
    public string? Location { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool AllDay { get; set; }

    public int Label { get; set; }
    public int Status { get; set; }
    public int AppointmentType { get; set; }

    [StringLength(SchedulerAppointmentConsts.RecurrenceInfoMaxLength)]
    public string? RecurrenceInfo { get; set; }
}
