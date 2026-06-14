using Integration.Framework.Base.Dtos.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

public class TenantUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;
}
