using System.Collections.Generic;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Kanal ürünleri sayfası (MDI sekme) — TÜM satış kanallarının kanal-ürün kayıtları tek listede.
/// Listeleme/düzenleme işini <see cref="ChannelProductsPanel"/> yapar; bu sayfa yalnız sayfa kabuğu
/// ve kanal TÜRÜ süzgecidir (kanal edit formundan tek farkı budur).
/// </summary>
public partial class SalesChannelProductListPage
{
    /// <summary>Kanal türü süzgeci (null = tüm kanallar).</summary>
    private SalesChannelType? _channelType;

    private IReadOnlyList<ChannelTypeOption> ChannelTypeOptions
    {
        get { return _channelTypeOptions ??= BuildChannelTypeOptions(); }
    }

    private IReadOnlyList<ChannelTypeOption>? _channelTypeOptions;

    private void OnChannelTypeChanged(SalesChannelType? value)
    {
        _channelType = value;
    }

    /// <summary>Süzgeç seçenekleri — enum çevirisi yoksa ham ad (gelen kutusuyla aynı düşüş kuralı).</summary>
    private IReadOnlyList<ChannelTypeOption> BuildChannelTypeOptions()
    {
        var types = new[] { SalesChannelType.TrN11, SalesChannelType.TrTrendyol, SalesChannelType.Etsy };
        var options = new List<ChannelTypeOption>(types.Length);

        foreach (var type in types)
        {
            var localized = L[$"Enum:SalesChannelType:{type}"];
            options.Add(new ChannelTypeOption(type, localized.ResourceNotFound ? type.ToString() : localized.Value));
        }

        return options;
    }

    private sealed record ChannelTypeOption(SalesChannelType Value, string Text);
}
