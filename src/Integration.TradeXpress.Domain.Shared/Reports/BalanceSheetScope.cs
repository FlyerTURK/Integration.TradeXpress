namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço kapsam kademesi — kullanıcı toolbar switch'iyle seçer. Hem sonuç DTO'su (Contracts) hem dondurulmuş
/// snapshot entity'si (Domain) bunu kullandığından Domain.Shared'da (paylaşılan çekirdek; her iki katman görür).
/// </summary>
public enum BalanceSheetScope
{
    /// <summary>Çalışılan ŞUBE bazında (Branch.Base biriminde).</summary>
    Branch = 0,

    /// <summary>Çalışılan ŞİRKET konsolide (Company.Base biriminde; tüm şubeleri toplar).</summary>
    Company = 1,
}
