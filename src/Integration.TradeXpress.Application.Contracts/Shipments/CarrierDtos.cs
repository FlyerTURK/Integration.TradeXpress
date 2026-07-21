using System;

namespace Integration.TradeXpress.Shipments;

/// <summary>Kargo firması — salt-okuma tekil DTO (host-global çekirdek katalog). Kimlik + kod + görüntü adı.</summary>
public class CarrierDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Kargo firması — combo/picker liste satırı (host-global; SALT SEÇİM). Şablon formu Name ile gösterir,
/// Id'yi (<see cref="ShipmentTemplateGetDto.CarrierId"/>) bağlar. N11City DTO deseni (yalın host-global okuma).</summary>
public class CarrierListDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
