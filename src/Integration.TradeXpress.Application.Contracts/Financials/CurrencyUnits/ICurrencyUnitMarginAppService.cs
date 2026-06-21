using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Marj yönetimi — <b>append-only</b> (CRUD değil). Her scope (tenant/host) kendi marjını
/// yönetir: güncel listeyi görür, marj BELİRLER (yeni satır), bir birimin geçmişini okur.
/// </summary>
public interface ICurrencyUnitMarginAppService : IApplicationService
{
    /// <summary>Görünür her birim için bu scope'un GÜNCEL marjı (latest/unit).</summary>
    Task<PagedResultDto<CurrencyUnitMarginListDto>> GetListAsync(CurrencyUnitMarginListRequestDto input);

    /// <summary>Bir birim için bu scope'un GÜNCEL marjı (edit form prefill); yoksa varsayılan.</summary>
    Task<CurrencyUnitMarginListDto> GetCurrentAsync(Guid currencyUnitId);

    /// <summary>Bir birime marj belirler (yeni append satır). Güncel marj bu olur.</summary>
    Task<CurrencyUnitMarginListDto> SetAsync(CurrencyUnitMarginSetDto input);

    /// <summary>Bir birimin marj geçmişi (en yeni önce).</summary>
    Task<List<CurrencyUnitMarginListDto>> GetHistoryAsync(Guid currencyUnitId);
}
