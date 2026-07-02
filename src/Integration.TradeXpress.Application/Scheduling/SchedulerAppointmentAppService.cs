using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Scheduling;

/// <summary>
/// SchedulerAppointment (takvim randevusu) servisi — <b>company-scoped</b> (kapsam DAİMA çalışılan şirket;
/// <see cref="ICurrentCompany"/>, sunucu zorlar — client CompanyId GÖNDERMEZ). Okumalar Mapperly
/// (<c>ObjectMapper.Map</c>); yazmalar entity ctor/Set* (invariant'lar entity'de). DxScheduler CRUD event'lerinden çağrılır.
/// </summary>
[Authorize(TradeXpressPermissions.Appointments.Default)]
public class SchedulerAppointmentAppService : TradeXpressAppService, ISchedulerAppointmentAppService
{
    private readonly IRepository<SchedulerAppointment, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    public SchedulerAppointmentAppService(
        IRepository<SchedulerAppointment, Guid> repository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<List<SchedulerAppointmentDto>> GetListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
            return new List<SchedulerAppointmentDto>();

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.StartTime));

        return rows.Select(ObjectMapper.Map<SchedulerAppointment, SchedulerAppointmentDto>).ToList();
    }

    [Authorize(TradeXpressPermissions.Appointments.Create)]
    public virtual async Task<SchedulerAppointmentDto> CreateAsync(SchedulerAppointmentDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        var e = new SchedulerAppointment(companyId, input.Subject, input.StartTime, input.EndTime);
        ApplyMutableFields(e, input);
        await _repository.InsertAsync(e, autoSave: true);
        return ObjectMapper.Map<SchedulerAppointment, SchedulerAppointmentDto>(e);
    }

    [Authorize(TradeXpressPermissions.Appointments.Update)]
    public virtual async Task<SchedulerAppointmentDto> UpdateAsync(SchedulerAppointmentDto input)
    {
        var e = await _repository.GetAsync(input.Id);
        e.SetSubject(input.Subject);
        e.SetTimeRange(input.StartTime, input.EndTime);
        ApplyMutableFields(e, input);
        await _repository.UpdateAsync(e, autoSave: true);
        return ObjectMapper.Map<SchedulerAppointment, SchedulerAppointmentDto>(e);
    }

    [Authorize(TradeXpressPermissions.Appointments.Delete)]
    public virtual async Task DeleteAsync(Guid id)
        => await _repository.DeleteAsync(id);

    private static void ApplyMutableFields(SchedulerAppointment e, SchedulerAppointmentDto input)
    {
        e.SetDescription(input.Description);
        e.SetLocation(input.Location);
        e.SetAllDay(input.AllDay);
        e.SetLabel(input.Label);
        e.SetStatus(input.Status);
        e.SetAppointmentType(input.AppointmentType);
        e.SetRecurrenceInfo(input.RecurrenceInfo);
    }
}
