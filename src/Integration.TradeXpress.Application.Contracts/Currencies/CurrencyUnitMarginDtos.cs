using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Currencies;

/// <summary>Marj listesi sorgusu (per-scope). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class CurrencyUnitMarginListRequestDto : ListRequestDto
{
}

/// <summary>
/// Marj satırı (append-only). Liste = birim başına GÜNCEL marj (latest); history = tüm satırlar.
/// Birim Code/Name global <see cref="CurrencyUnit"/>'ten join'lenir (AppService doldurur).
/// </summary>
public class CurrencyUnitMarginListDto : EntityDto<Guid>, IListDto<Guid>
{
    // Id = CurrencyUnitMargin satır Id'si.
    public Guid CurrencyUnitId { get; set; }
    public string CurrencyUnitCode { get; set; } = string.Empty;
    public string CurrencyUnitName { get; set; } = string.Empty;
    public CurrencyUnitType UnitType { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Global host birimi mi (kimlik). Marj yine de bu scope'a özeldir.</summary>
    public bool IsGlobalUnit { get; set; }

    public MarginType MarginOnBuyType { get; set; }
    public decimal MarginOnBuyValue { get; set; }
    public MarginType MarginOnSellType { get; set; }
    public decimal MarginOnSellValue { get; set; }

    /// <summary>Bu marj satırının yazıldığı an (append-only).</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Bir birime marj BELİRLEME girdisi. Append-only: her çağrı YENİ satır ekler
/// (güncel = en son). Güncelleme/silme yok.
/// </summary>
public class CurrencyUnitMarginSetDto
{
    [Required]
    public Guid CurrencyUnitId { get; set; }

    public MarginType MarginOnBuyType { get; set; } = MarginType.Multiply;
    public decimal MarginOnBuyValue { get; set; } = 1m;
    public MarginType MarginOnSellType { get; set; } = MarginType.Multiply;
    public decimal MarginOnSellValue { get; set; } = 1m;
}
