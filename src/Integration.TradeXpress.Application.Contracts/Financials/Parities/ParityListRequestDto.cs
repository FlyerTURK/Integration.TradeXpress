using Integration.Framework.Base.Dtos;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite liste sorgusu. Merkezi <see cref="ListRequestDto"/> standardını kullanır
/// (SkipCount / MaxResultCount / Sorting + yapılandırılmış Sorts / Filters / global Filter).
/// </summary>
public class ParityListRequestDto : ListRequestDto
{
}
