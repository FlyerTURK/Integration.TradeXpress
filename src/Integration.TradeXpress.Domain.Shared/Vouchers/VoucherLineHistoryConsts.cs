namespace Integration.TradeXpress.Vouchers;

/// <summary>VoucherLineHistory alan sınırları — DB kolon boyutları tek kaynağı.</summary>
public static class VoucherLineHistoryConsts
{
    /// <summary>VoucherNumber snapshot gösterim genişliği (fiş numarası string DEĞİL — ama kod/açıklama alanları bu sınıfta yaşar).</summary>
    public const int CommodityCodeMaxLength = VoucherConsts.CommodityCodeMaxLength;

    public const int MainUnitCodeMaxLength = VoucherConsts.CommodityCodeMaxLength;

    public const int DescriptionMaxLength = VoucherConsts.DescriptionMaxLength;

    /// <summary>Serileştirilmiş tam <c>VoucherLineDto</c> anlık görüntüsü — Confirmation payload'undan GENİŞ
    /// (tarihçe kaydı okuma-anı denormalize alanlarını da taşıyabilir; kırpma yerine geniş sınır).</summary>
    public const int SnapshotMaxLength = 8192;
}
