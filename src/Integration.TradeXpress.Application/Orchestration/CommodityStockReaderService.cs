using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// <see cref="ICommodityStockReader"/>'ın GERÇEK implementasyonu — TEK stok kaynağı ailenin STOK RAPORUDUR
/// (<c>GetStockAsync</c>; Available = Net − RezerveÇıkış; işaret kuralı raporda, BURADA DEĞİL).
/// ICurrentCompany ZORUNLU: şirket bağlamı yoksa rapor boş döner — çağıran (orkestrasyon job'ı) bağlamı
/// kurmakla yükümlü.
/// <para>Sözlük iki anahtar seti taşır: (aile, emtia, varyant) varyant-bazlı + (aile, emtia, null) emtia
/// TOPLAMI — <see cref="SellableStockCalculator"/>'ın varyantsız satır geri-düşüşü toplamı okur.</para>
/// <para><b>Bugün Metal + Good.</b> Diğer ailelerin (Scrap/Jewelry/Stone/Future) rapor servisi yok ya da
/// emtia kırılımı taşımıyor; desteklenmeyen aile <b>boş sözlük</b> döner → hesap o satırı 0 stok sayar
/// (fail-closed). Sessizce "sınırsız" saymak oversell kapısını açardı.</para>
/// </summary>
public class CommodityStockReaderService : ICommodityStockReader, ITransientDependency
{
    private readonly IMetalReportAppService _metalReport;
    private readonly IGoodReportAppService _goodReport;

    public CommodityStockReaderService(
        IMetalReportAppService metalReport,
        IGoodReportAppService goodReport)
    {
        _metalReport = metalReport;
        _goodReport  = goodReport;
    }

    public virtual async Task<IReadOnlyDictionary<CommodityStockKey, CommodityAvailability>> GetAvailableAsync(
        ProcessType family, IReadOnlyCollection<Guid> commodityIds)
    {
        if (commodityIds.Count == 0)
        {
            return new Dictionary<CommodityStockKey, CommodityAvailability>();
        }

        var wanted = commodityIds.ToHashSet();

        return family switch
        {
            ProcessType.Metal => await ReadMetalAsync(wanted),
            ProcessType.Good  => await ReadGoodAsync(wanted),
            _                 => new Dictionary<CommodityStockKey, CommodityAvailability>(),
        };
    }

    /// <summary>Maden: kanonik boyut GRAM (<c>AvailableAmount</c>); adet de taşınır ama reçete ihtiyacı
    /// gramdan kurulur (2026-07-25 inceleme bulgusu #10 — gram ihtiyacına adet bölmek satılabilir sayıyı
    /// katsayı kadar şişiriyordu).</summary>
    private async Task<Dictionary<CommodityStockKey, CommodityAvailability>> ReadMetalAsync(HashSet<Guid> wanted)
    {
        // Tek çağrı (şirketin tüm metal stoğu) + bellek içi daraltma — emtia başına N rapor sorgusu yerine.
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto());

        var result = new Dictionary<CommodityStockKey, CommodityAvailability>();
        foreach (var row in rows.Where(r => r.MetalId is { } id && wanted.Contains(id)))
        {
            Add(result, ProcessType.Metal, row.MetalId!.Value, row.VariantId,
                row.AvailableAmount, row.AvailableQuantity);
        }

        return result;
    }

    /// <summary>Mamül: kanonik boyut ADET (<c>AvailableQuantity</c>); miktar da taşınır (ağırlıkla izlenen
    /// mamüllerde reçete satırı miktar üzerinden kısıtlayabilir).</summary>
    private async Task<Dictionary<CommodityStockKey, CommodityAvailability>> ReadGoodAsync(HashSet<Guid> wanted)
    {
        var rows = await _goodReport.GetStockAsync(new GoodReportFilterDto());

        var result = new Dictionary<CommodityStockKey, CommodityAvailability>();
        foreach (var row in rows.Where(r => r.GoodId is { } id && wanted.Contains(id)))
        {
            Add(result, ProcessType.Good, row.GoodId!.Value, row.VariantId,
                row.AvailableAmount, row.AvailableQuantity);
        }

        return result;
    }

    /// <summary>Varyant anahtarını yazar ve emtia TOPLAMINA ekler. Varyantsız satır zaten (aile, emtia, null)
    /// anahtarının kendisidir — tekrar eklenmez (çift sayma olmaz).</summary>
    private static void Add(
        Dictionary<CommodityStockKey, CommodityAvailability> result,
        ProcessType family, Guid commodityId, Guid? variantId,
        decimal amount, decimal quantity)
    {
        var variantKey = new CommodityStockKey(family, commodityId, variantId);
        result[variantKey] = Sum(result.GetValueOrDefault(variantKey), amount, quantity);

        if (variantId is not null)
        {
            var totalKey = new CommodityStockKey(family, commodityId, null);
            result[totalKey] = Sum(result.GetValueOrDefault(totalKey), amount, quantity);
        }
    }

    private static CommodityAvailability Sum(CommodityAvailability current, decimal amount, decimal quantity)
    {
        return new CommodityAvailability(current.Amount + amount, current.Quantity + quantity);
    }
}
