namespace Integration.TradeXpress.Commodities;

/// <summary>
/// FollowingUnit (takip edilen para birimi) taşıyan katalog DTO'ları (Metal/Scrap/Future List+Get).
/// <see cref="FollowingUnitCode"/>, FollowingUnitCatalogAppService tabanınca map sonrası doldurulur.
/// </summary>
public interface IFollowingUnitDto
{
    string? FollowingUnitCode { get; set; }
}
