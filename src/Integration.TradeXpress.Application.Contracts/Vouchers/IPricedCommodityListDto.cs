using System;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiyatlı-emtia fiş panellerinin (Taş/Mücevher) picker listesinden beklediği ortak arayüz.
/// Panel bu sözleşme üzerinden yön-bazlı fiyat/birim önerir; DTO'lar salt bu arayüzü uygular (ISP).
/// </summary>
public interface IPricedCommodityListDto : IIsActive
{
    Guid Id { get; }
    string Code { get; }

    /// <summary>IIsActive framework'te marker (üyesiz) — picker filtreleme için üye burada bildirilir.</summary>
    bool IsActive { get; }

    /// <summary>Adet takibi yapılır mı (Adet alanı görünür).</summary>
    bool IsQuantity { get; }

    /// <summary>Fiyat adet üzerinden mi (true) yoksa miktar üzerinden mi (false) hesaplanır.</summary>
    bool PriceByQuantity { get; }

    /// <summary>Kullanıcı fiyat tipini (Adet/Miktar) panelde değiştirebilir mi.</summary>
    bool PriceTypeChange { get; }

    decimal EntryPrice { get; }
    Guid? EntryPriceUnitId { get; }
    decimal ExitPrice { get; }
    Guid? ExitPriceUnitId { get; }
}
