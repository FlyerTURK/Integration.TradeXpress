using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface IMetalReportAppService : IApplicationService
{
    Task<List<MetalStockRowDto>> GetStockAsync(MetalReportFilterDto filter);
    Task<List<MetalMovementRowDto>> GetMovementsAsync(MetalReportFilterDto filter);

    /// <summary>Bilanço STOK(maden) için: kapsam (şirket ICurrentCompany'den) + branch/vault, asOfExclusive'den ÖNCE
    /// birikmiş fiziksel maden holding'i birim(MainUnit)-bazında (firma-perspektifi +). Gün-sonu dahil için
    /// asOfExclusive=asOf.Date.AddDays(1). Cari metal (ledger/BAKIYE) AYRI boyuttur — bu fiziksel vault stoğu (offset).</summary>
    Task<Dictionary<Guid, decimal>> GetMetalNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive);

    /// <summary>Bilanço İŞÇİLİK(Labor) için: maden Normal/İade/Emanet işçilik karşı tarafı (PayUnit/PayTotal) net,
    /// işçilik-birimi bazında — ÇIKIŞ(satış)=+, GİRİŞ(alış)=−. Merkez base'e çevirince ÇIKIŞ-HAS − GİRİŞ-HAS = işçilik K/Z.</summary>
    Task<Dictionary<Guid, decimal>> GetMetalLaborByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive);

    /// <summary>DRILL — metal fiziksel stok COMMODITY(metal kodu) bazında, tek birim için (bilanço Stok popup).</summary>
    Task<Dictionary<string, decimal>> GetMetalStockByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive);

    /// <summary>DRILL — metal işçilik maliyeti (on-hand) COMMODITY bazında, tek PayUnit için (bilanço İşçilik popup).</summary>
    Task<Dictionary<string, decimal>> GetMetalLaborByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive);
}
