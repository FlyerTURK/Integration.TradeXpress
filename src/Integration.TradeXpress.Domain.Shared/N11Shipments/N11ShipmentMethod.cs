namespace Integration.TradeXpress.N11Shipments;

/// <summary>N11 kargo şablonu teslimat yöntemi (<c>shipmentMethod</c>).</summary>
public enum N11ShipmentMethod : byte
{
    /// <summary>Kargo.</summary>
    Cargo = 1,

    /// <summary>Diğer (dijital / hediye / online teslimat).</summary>
    Other = 2,
}
