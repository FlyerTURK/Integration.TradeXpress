using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite grid satırı (KİMLİK = base/quote çifti). Oran burada DEĞİL — parite oranı
/// birimlerin efektif fiyatından canlı türetilir. <see cref="IsGlobal"/>: host kataloğu
/// (TenantId=null) mu; tenant bunu düzenleyemez, salt-okur.
/// </summary>
public class ParityListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCode { get; set; } = string.Empty;
    public string BaseName { get; set; } = string.Empty;

    public Guid QuoteCurrencyUnitId { get; set; }
    public string QuoteCode { get; set; } = string.Empty;
    public string QuoteName { get; set; } = string.Empty;

    /// <summary>USDTRY formatı (BaseCode + QuoteCode).</summary>
    public string Code => BaseCode + QuoteCode;

    /// <summary>"Amerikan Doları / Türk Lirası" formatı.</summary>
    public string Name => string.IsNullOrEmpty(BaseName) ? string.Empty : $"{BaseName} / {QuoteName}";

    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Host kataloğu (TenantId=null) mu? Tenant bunu düzenleyemez; salt-okur.</summary>
    public bool IsGlobal { get; set; }
}
