using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Kanal-ürün push emniyet alanları (emniyet payı + fiyat tabanı/tavanı) — kanal formlarının ortak parçası.
/// Kuralın kendisi sunucuda (<c>ChannelPushGuard</c>); burada yalnız giriş alanları vardır.</summary>
public partial class ChannelPushGuardFields : CrudComponentBase
{
    /// <summary>Kanalda gösterilmeyen stok payı (opsiyonel).</summary>
    [Parameter] public int? SafetyStock { get; set; }

    [Parameter] public EventCallback<int?> SafetyStockChanged { get; set; }

    /// <summary>Push fiyat tabanı (opsiyonel).</summary>
    [Parameter] public decimal? MinPrice { get; set; }

    [Parameter] public EventCallback<decimal?> MinPriceChanged { get; set; }

    /// <summary>Push fiyat tavanı (opsiyonel).</summary>
    [Parameter] public decimal? MaxPrice { get; set; }

    [Parameter] public EventCallback<decimal?> MaxPriceChanged { get; set; }
}
