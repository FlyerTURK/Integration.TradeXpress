namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Pay (karşılık) bacağı combosunun hangi kaynaktan besleneceği — ödeme tipine göre
/// belirlenir (Normal → para birimleri; WithCash/peşin → diğer nakit enstrümanları).
/// Karar motorda üretilir; UI yalnız ilgili listeyi bağlar.
/// </summary>
public enum PayCommoditySource : byte
{
    Units = 0,
    CashInstruments = 1,
}
