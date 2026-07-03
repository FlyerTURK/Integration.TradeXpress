namespace Integration.TradeXpress.Scheduling;

/// <summary>
/// Takvim randevusu (Scheduler appointment) — DevExpress <c>DxScheduler</c>'a bağlanır. <b>Company-scoped</b>
/// (çalışılan şirkete ait; <see cref="CompanyId"/> id-only, nav YOK — Vault/AssayOffice deseni) + per-tenant
/// (IMultiTenant). DevExpress alan modeli: Subject/Start/End/AllDay + Description/Location + Label/Status
/// (int, yerleşik) + AppointmentType + RecurrenceInfo (tekrarlama serisi). İleride genişletilecek
/// (kaynak/resource, hatırlatma, cari/şube ilişkisi vb.).
/// </summary>
public class SchedulerAppointment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (nav YOK). Kapsam DAİMA çalışılan şirket (sunucu zorlar).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Subject { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual string? Location { get; protected set; }

    public virtual DateTime StartTime { get; protected set; }
    public virtual DateTime EndTime { get; protected set; }
    public virtual bool AllDay { get; protected set; }

    /// <summary>DevExpress yerleşik etiket id'si (renk grubu).</summary>
    public virtual int Label { get; protected set; }
    /// <summary>DevExpress yerleşik durum id'si (Free/Busy/Tentative/OutOfOffice...).</summary>
    public virtual int Status { get; protected set; }
    /// <summary>DevExpress randevu tipi (0=Normal, 1=Pattern, 2=DeletedOccurrence, 3=ChangedOccurrence) — tekrarlama için.</summary>
    public virtual int AppointmentType { get; protected set; }
    /// <summary>Tekrarlama bilgisi (DevExpress serileştirilmiş string); null = tek seferlik.</summary>
    public virtual string? RecurrenceInfo { get; protected set; }

    protected SchedulerAppointment() { }

    public SchedulerAppointment(
        Guid companyId,
        string subject,
        DateTime startTime,
        DateTime endTime)
    {
        SetCompany(companyId);
        SetSubject(subject);
        SetTimeRange(startTime, endTime);
    }

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:SchedulerAppointment:CompanyRequired");
        }

        CompanyId = companyId;
    }

    public virtual void SetSubject(string subject)
    {
        // Serbest kullanıcı metni: Trim + zorunlu + max. TitleCase/UPPER normalizasyonu YAPILMAZ
        // (randevu konusu kullanıcının yazdığı gibi kalır).
        Subject = StringFieldGuard.EnsureRequiredText(
            subject,
            nameof(Subject),
            1,
            SchedulerAppointmentConsts.SubjectMaxLength);
    }

    public virtual void SetTimeRange(DateTime start, DateTime end)
    {
        if (end < start)
        {
            throw new BusinessException("TradeXpress:SchedulerAppointment:EndBeforeStart");
        }

        StartTime = start;
        EndTime = end;
    }

    public virtual void SetDescription(string? description)
    {
        // Opsiyonel alan: yalnız üst sınır (min yok — mevcut davranış korunur). Aşılırsa tipli Framework exception'ı.
        if (description is { Length: > SchedulerAppointmentConsts.DescriptionMaxLength })
        {
            throw new TooLongPropertyException(nameof(Description), SchedulerAppointmentConsts.DescriptionMaxLength);
        }

        Description = description;
    }

    public virtual void SetLocation(string? location)
    {
        if (location is { Length: > SchedulerAppointmentConsts.LocationMaxLength })
        {
            throw new TooLongPropertyException(nameof(Location), SchedulerAppointmentConsts.LocationMaxLength);
        }

        Location = location;
    }

    public virtual void SetAllDay(bool allDay)
    {
        AllDay = allDay;
    }

    public virtual void SetLabel(int label)
    {
        Label = label;
    }

    public virtual void SetStatus(int status)
    {
        Status = status;
    }

    public virtual void SetAppointmentType(int type)
    {
        AppointmentType = type;
    }

    public virtual void SetRecurrenceInfo(string? recurrenceInfo)
    {
        if (recurrenceInfo is { Length: > SchedulerAppointmentConsts.RecurrenceInfoMaxLength })
        {
            throw new TooLongPropertyException(nameof(RecurrenceInfo), SchedulerAppointmentConsts.RecurrenceInfoMaxLength);
        }

        RecurrenceInfo = recurrenceInfo;
    }
}
