using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>N11 kargo firması istemcisi — SOAP ShipmentCompanyService.GetShipmentCompanies (REST'te yok). Host kimliğiyle sync'lenir.</summary>
public interface IN11ShipmentCompanyClient
{
    Task<IReadOnlyList<N11ShipmentCompanyRecord>> GetShipmentCompaniesAsync(string appKey, string appSecret, CancellationToken cancellationToken = default);
}

public sealed record N11ShipmentCompanyRecord(string ExternalId, string Name, string ShortName);
