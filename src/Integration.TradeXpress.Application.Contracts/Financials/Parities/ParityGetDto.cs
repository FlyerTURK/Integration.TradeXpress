using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite edit modeli. Base/quote çifti değişmezdir (entity immutable) → edit'te yalnız
/// IsActive/DisplayOrder güncellenir. <see cref="BaseCode"/>/<see cref="QuoteCode"/> AppService'te
/// referans birimlerden zenginleştirilir (yapısal başlık L2 = "BASE/QUOTE").
/// </summary>
public class ParityGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCode { get; set; } = string.Empty;

    public Guid QuoteCurrencyUnitId { get; set; }
    public string QuoteCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Host kataloğu (TenantId=null) mu? Tenant bunu düzenleyemez; salt-okur.</summary>
    public bool IsGlobal { get; set; }
}
