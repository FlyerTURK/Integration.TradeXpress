namespace Integration.TradeXpress.Vouchers;

/// <summary>Voucher/VoucherLine alan sınırları ve decimal hassasiyetleri (ERPPROV3 paritesi).</summary>
public static class VoucherConsts
{
    // ── decimal precision (EF Core HasPrecision) ──────────────────────────────

    /// <summary>Amount / Total / PayTotal / Profit — para ve has miktarları (N2).</summary>
    public const int AmountPrecision = 18;
    public const int AmountScale     = 2;

    /// <summary>Factor / PayFactor / MarketPrice / Quantity — milyem / çarpan / parite / fiyat (N5).</summary>
    public const int FactorPrecision = 18;
    public const int FactorScale     = 5;

    // ── string lengths ────────────────────────────────────────────────────────

    /// <summary>CommodityCode / PayCommodityCode — relational olmayan snapshot gösterim kodu.</summary>
    public const int CommodityCodeMaxLength = 64;

    /// <summary>AccountCode / SubAccountCode — karşı tarafın kod SNAPSHOT'ı (tipe göre Account/SubAccount ‖
    /// Branch/Vault kodu). Sınır, kaynak kod alanlarının en genişi olan <see cref="Accounts.AccountConsts.CodeMaxLength"/>
    /// ile hizalıdır (Branch/Vault kodları da bu sınırın altında).</summary>
    public const int CounterpartyCodeMaxLength = 32;

    /// <summary>Karşı taraf kod snapshot'ının alt sınırı — kaynak entity'lerin kod kuralıyla (EntityFieldConsts.
    /// CodeMinLength) hizalı; snapshot kaynağından daha gevşek/sıkı olmamalı.</summary>
    public const int CounterpartyCodeMinLength = 3;

    public const int DescriptionMaxLength = 512;

    /// <summary>VoucherLineLog.Reason — değişiklik gerekçesi.</summary>
    public const int ReasonMaxLength = 512;
}
