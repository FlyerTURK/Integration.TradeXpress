using System;

namespace Integration.TradeXpress.Accounts;

/// <summary>Account / SubAccount alanları için merkezî sınırlar.</summary>
public static class AccountConsts
{
    public const int CodeMaxLength        = 32;
    public const int NameMaxLength        = 192;
    public const int DescriptionMaxLength = 512;

    /// <summary>Yeni cari hesapla birlikte otomatik açılan varsayılan alt hesabın kodu/adı (en az 1 alt hesap kuralı).</summary>
    public const string DefaultSubAccountCode = "ANAHESAP";
    public const string DefaultSubAccountName = "Ana Hesap";

    // NOT (2026-07-15 ürün kararı — vault-cari EMEKLİ): burada VaultCurrentAccountNameSuffix ("Kasa Carisi") +
    // VaultCurrentAccountCode(vaultId) vardı. Her kasa için SAHTE bir Account/SubAccount üretiliyordu; kod ham
    // GUID olduğundan cari listesinde okunmaz kayıtlar ("Sandık Kasa Carisi") oluşuyordu. Doğru model: kasa
    // KASADIR, cari CARİDİR; ayrım Voucher/BalanceLedgerEntry'deki AccountType alanındadır (Vault kipinde
    // AccountId/AccountCode=Şube, SubAccountId/SubAccountCode=Kasa). Bir daha sahte cari üretilmez.
}
