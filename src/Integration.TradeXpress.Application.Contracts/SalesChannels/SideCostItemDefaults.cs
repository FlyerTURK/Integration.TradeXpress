using System;
using System.Collections.Generic;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanal tipine göre <b>varsayılan gider satırı tohumu</b> (2026-07-10 kullanıcı kararı) — YENİ kanal kaydında
/// ve gider listesi BOŞKEN formda öneri olarak kullanılır (kullanıcı satırları düzenler/siler; zorlama yok).
/// Araştırma SSOT: .claude/research/channel-commissions/.
/// <list type="bullet">
/// <item>N11/Trendyol: Paketleme + Kargo sabit (fiş: genel gider / karşı cari), Sigortalı Gönderim
/// (varyant opt-in, KAPALI), Komisyon (N11: AutoRate — kategoriden efektif oran, Value fallback;
/// Trendyol: kanal-oran).</item>
/// <item>Etsy: + Satış Başı Sabit $0,45 USD (listing $0,20 + payment $0,25) + Offsite Ads GrossUp (KAPALI);
/// komisyon kanal-sabit %9,5 (=6,5+3).</item>
/// </list>
/// </summary>
public static class SideCostItemDefaults
{
    /// <summary>Kanal tipine göre önerilen satırlar. <paramref name="usdCurrencyUnitId"/> = USD biriminin id'si
    /// (Etsy satış-başı sabiti USD'dir; çağıran lookup'tan çözer, bulunamazsa null = kanal yereli).</summary>
    public static List<SideCostItemDto> Build(SalesChannelType channelType, Guid? usdCurrencyUnitId)
    {
        var items = new List<SideCostItemDto>
        {
            new()
            {
                Kind = SideCostKind.Packaging,
                CalcMode = SideCostCalcMode.FixedAmount,
                PostingMode = SideCostPostingMode.Expense,
                DisplayOrder = 0,
            },
            new()
            {
                Kind = SideCostKind.Cargo,
                CalcMode = SideCostCalcMode.FixedAmount,
                PostingMode = SideCostPostingMode.CounterpartyAccount,
                DisplayOrder = 1,
            },
            new()
            {
                Kind = SideCostKind.InsuredShipping,
                CalcMode = SideCostCalcMode.FixedAmount,
                PostingMode = SideCostPostingMode.CounterpartyAccount,
                IsEnabled = false,
                RequiresVariantOptIn = true,
                DisplayOrder = 2,
            },
        };

        if (channelType == SalesChannelType.Etsy)
        {
            // Satış başı sabit bedel: listing $0,20 + payment $0,25 = $0,45 (USD — değerlemeyle yerele çevrilir).
            items.Add(new SideCostItemDto
            {
                Kind = SideCostKind.ChannelFixed,
                CalcMode = SideCostCalcMode.FixedAmount,
                Value = 0.45m,
                CurrencyUnitId = usdCurrencyUnitId,
                PostingMode = SideCostPostingMode.CounterpartyAccount,
                DisplayOrder = 3,
            });
        }

        items.Add(new SideCostItemDto
        {
            Kind = SideCostKind.Commission,
            CalcMode = SideCostCalcMode.GrossUpPercent,
            // N11: oran kategoriden OTOMATİK (AutoRate; Value=fallback). Trendyol: kanal-oran (kullanıcı girer).
            // Etsy: kanal-sabit %9,5 (=6,5 komisyon + 3 ödeme işleme).
            AutoRate = channelType == SalesChannelType.TrN11,
            Value = channelType == SalesChannelType.Etsy ? 9.5m : 0m,
            PostingMode = SideCostPostingMode.CounterpartyAccount,
            DisplayOrder = items.Count,
        });

        if (channelType == SalesChannelType.Etsy)
        {
            // Offsite Ads — opsiyonel ek GrossUp (varsayılan KAPALI; kullanan satıcı oranını girip açar).
            items.Add(new SideCostItemDto
            {
                Kind = SideCostKind.Commission,
                DisplayName = "Offsite Ads",
                CalcMode = SideCostCalcMode.GrossUpPercent,
                IsEnabled = false,
                PostingMode = SideCostPostingMode.CounterpartyAccount,
                DisplayOrder = items.Count,
            });
        }

        return items;
    }
}
