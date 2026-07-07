using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>N11 kargo firması (host-global).</summary>
public class N11ShipmentCompanyDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
}

/// <summary>N11 kargo firmaları — host-global (sync + okuma). Kargo listesi tüm tenant'lar için aynı.</summary>
public interface IN11ShipmentCompanyAppService : IApplicationService
{
    /// <summary>Host-only: kargo firmalarını SOAP'tan çekip upsert + stale-sil (tam re-sync). Değişiklik sayısını döner.</summary>
    Task<int> SyncAsync();

    /// <summary>Tüm kargo firmaları (host-global; host'a sabitlenmiş okuma).</summary>
    Task<List<N11ShipmentCompanyDto>> GetListAsync();
}
