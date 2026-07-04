using System;

namespace Integration.TradeXpress.Financials;

/// <summary>
/// Kalıcılaşan finansal değerlerin TEK yuvarlama noktası (SSOT).
///
/// <para><b>Ground-truth (ERPPRO, 2026-07-03 araştırması —
/// <c>.claude/research/financial-rules/rounding-and-branch-base.md</c>):</b> orijinal sistemde yuvarlama
/// uygulama kodunda değil SQL Server kolon scale'inde yaşar — tutar/miktar/bakiye kolonları
/// <c>decimal(18,2)</c>, milyem/kur/fiyat kolonları <c>decimal(18,5)</c>. Daha uzun ölçekli değer INSERT
/// edilince SQL Server implicit ROUND (<b>half away from zero</b>) uygular → yuvarlama fiilen KAYIT ANINDA
/// gerçekleşir. TradeXpress bu fiili davranışı açık kurala çevirir: ara hesaplar HAM kalır, yalnız
/// kalıcılaşan (ledger/snapshot) değer yazım anında yuvarlanır.</para>
///
/// <para><b>Politika:</b> tutar/miktar/net → N2, milyem/kur/faktör → N5;
/// <see cref="MidpointRounding.AwayFromZero"/> (SQL semantiğiyle birebir; ERPPROV3 ile de uyumlu).</para>
/// </summary>
public static class FinancialRounding
{
    /// <summary>Tutar/miktar/bakiye ondalık hanesi (ERPPRO <c>decimal(18,2)</c> paritesi).</summary>
    public const int AmountDecimals = 2;

    /// <summary>Milyem/kur/fiyat/faktör ondalık hanesi (ERPPRO <c>decimal(18,5)</c> paritesi).</summary>
    public const int RateDecimals = 5;

    /// <summary>Kalıcılaşan tutar/miktar/bakiye değerini N2 + AwayFromZero yuvarlar
    /// (0.005 → 0.01, −0.005 → −0.01 — SQL implicit ROUND davranışı).</summary>
    public static decimal RoundAmount(decimal value)
    {
        return Math.Round(value, AmountDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Kalıcılaşan milyem/kur/fiyat/faktör değerini N5 + AwayFromZero yuvarlar.</summary>
    public static decimal RoundRate(decimal value)
    {
        return Math.Round(value, RateDecimals, MidpointRounding.AwayFromZero);
    }
}
