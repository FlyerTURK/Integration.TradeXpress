namespace Integration.TradeXpress.N11Cities;

/// <summary>N11 il (host-global).</summary>
public class N11CityDto
{
    public string CityCode { get; set; } = string.Empty;
    public string CityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>N11 ilçe (host-global) — <see cref="CityCode"/> ile iline bağlı.</summary>
public class N11DistrictDto
{
    public string DistrictId { get; set; } = string.Empty;
    public string CityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>N11 mahalle (on-demand; SAKLANMAZ).</summary>
public class N11NeighborhoodDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
