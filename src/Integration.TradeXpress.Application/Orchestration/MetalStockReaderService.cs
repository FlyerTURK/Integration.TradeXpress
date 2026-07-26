using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Reports;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// <see cref="IMetalStockReader"/>'ın GERÇEK implementasyonu — TEK stok kaynağı
/// <see cref="IMetalReportAppService.GetStockAsync"/> sarmalanır (AvailableQuantity = Net − RezerveÇıkış;
/// işaret kuralı raporda, BURADA DEĞİL). ICurrentCompany ZORUNLU: şirket bağlamı yoksa rapor boş döner —
/// çağıran (orkestrasyon job'ı) bağlamı kurmakla yükümlü.
/// <para>Sözlük iki anahtar seti taşır: (MetalId, VariantId) varyant-bazlı + (MetalId, null) metal TOPLAMI —
/// <see cref="SellableStockCalculator"/>'ın varyantsız satır geri-düşüşü toplamı okur.</para>
/// </summary>
public class MetalStockReaderService : IMetalStockReader, ITransientDependency
{
    private readonly IMetalReportAppService _metalReport;

    public MetalStockReaderService(IMetalReportAppService metalReport)
    {
        _metalReport = metalReport;
    }

    public virtual async Task<IReadOnlyDictionary<(Guid MetalId, Guid? MetalVariantId), decimal>> GetAvailableAsync(
        IReadOnlyCollection<Guid> metalIds)
    {
        // Tek çağrı (şirketin tüm metal stoğu) + bellek içi daraltma — maden başına N rapor sorgusu yerine.
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto());
        var wanted = metalIds.ToHashSet();

        var result = new Dictionary<(Guid, Guid?), decimal>();
        foreach (var row in rows.Where(r => r.MetalId is { } id && wanted.Contains(id)))
        {
            var metalId = row.MetalId!.Value;

            // GRAM esas alınır: AvailableAmount = NetAmount − RezerveÇıkışAmount. AvailableQuantity ADETTİR —
            // hesap gram-ihtiyacına böldüğünden adet kullanmak satılabilir sayıyı katsayı kadar şişirirdi
            // (2026-07-25 inceleme bulgusu #10 — birim karışıklığı).
            var variantKey = (metalId, row.VariantId);
            result[variantKey] = result.GetValueOrDefault(variantKey) + row.AvailableAmount;

            // Metal toplamı (varyantsız geri-düşüş anahtarı) — varyantlı satırlar toplanır; varyantsız satır
            // zaten (metal, null) anahtarının kendisidir (üstteki atama), tekrar eklenmez (çift sayma olmaz).
            var totalKey = (metalId, (Guid?)null);
            if (row.VariantId is not null)
            {
                result[totalKey] = result.GetValueOrDefault(totalKey) + row.AvailableAmount;
            }
        }

        return result;
    }
}
