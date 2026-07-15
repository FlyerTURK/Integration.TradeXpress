namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fişin KARŞI TARAF tipi (polimorfik karşı-taraf ayrımı — legacy ERPPRO <c>HesapType</c> paritesi).
/// Kasa bakiyeleri bu ayrımla, <b>sahte cari hesap/alt hesap üretmeden</b> ayrışır (2026-07-15 ürün kararı).
///
/// <para><see cref="CurrentAccount"/> = 0 (VARSAYILAN): karşı taraf gerçek bir cari — fiş
/// <c>AccountId</c> (+ opsiyonel <c>SubAccountId</c>) taşır, <c>CounterpartyVaultId</c> boştur.
/// Mevcut tüm fişler bu değerdedir (backfill gerekmez).</para>
///
/// <para><see cref="Vault"/> = 1: karşı taraf bir İÇ KASA — fişin cari hesabı YOKTUR
/// (<c>AccountId</c>/<c>SubAccountId</c> boş), karşı taraf doğrudan <c>CounterpartyVaultId</c>'dir.</para>
/// </summary>
public enum AccountType : byte
{
    /// <summary>Karşı taraf cari hesap (dış cari akışı) — varsayılan.</summary>
    CurrentAccount = 0,

    /// <summary>Karşı taraf iç kasa — cari hesap üretilmez, kasa doğrudan referanslanır.</summary>
    Vault = 1,
}
