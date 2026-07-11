using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Form DTO'sundan (<see cref="SideCostSettingsDto"/>) domain owned VO'su (<see cref="SideCostSettings"/>) kurar —
/// 3 kanal AppService'inin (N11/Trendyol/Etsy) ORTAK create/update yolu (DRY). Guard'lar VO ctor'unda çalışır
/// (moda göre negatif tutar/oran sınırları, komisyonda GrossUp zorunluluğu, AutoRate yalnız komisyon,
/// ana-hesapsız alt-hesap) → burada ek doğrulama yok, fail-fast domain'de.
/// Null DTO = yapılandırma yok → null döner (entity.SetSideCosts(null) ayarı temizler).
/// </summary>
public static class SideCostSettingsFactory
{
    public static SideCostSettings? Build(SideCostSettingsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var items = dto.Items.Select(BuildItem).ToList();
        return new SideCostSettings(items);
    }

    private static SideCostItem BuildItem(SideCostItemDto dto)
    {
        return new SideCostItem(
            dto.Kind,
            dto.DisplayName,
            dto.CalcMode,
            dto.Value,
            dto.CurrencyUnitId,
            dto.ServiceId,
            dto.PostingMode,
            dto.AccountId,
            dto.SubAccountId,
            dto.AutoRate,
            dto.IsEnabled,
            dto.DisplayOrder,
            dto.RequiresVariantOptIn);
    }
}
