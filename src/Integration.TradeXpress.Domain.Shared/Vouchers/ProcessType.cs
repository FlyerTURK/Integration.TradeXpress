namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// İşlem türü — bir <see cref="VoucherLine"/>'ın hangi tür işlem olduğunu belirtir.
/// Görünen ad / kısa kod lokalizasyondan üretilir; DB'de tutulmaz. Değerler
/// ERPPROV3 (legacy <c>tIslemKodu</c>) ile hizalı.
///
/// <para>Şu an yalnız <see cref="Cash"/> uçtan uca çalışır; diğer türler enum +
/// hesap-motoru dalı olarak hazırdır, destek entity'leri (Commodity, Bullion,
/// Assay...) geldikçe etkinleşir.</para>
/// </summary>
public enum ProcessType : byte
{
    /// <summary>Maden (altın/gümüş/platin/paladyum) alış-satış işlemi.</summary>
    Metal    = 1,

    /// <summary>Hurda maden işlemi.</summary>
    Scrap    = 2,

    /// <summary>Nakit (para) giriş/çıkış işlemi.</summary>
    Cash     = 3,

    /// <summary>Çevrim — bakiye/birim çevirme (madenden paraya vb.).</summary>
    Convert  = 4,

    /// <summary>Hizmet (gider/gelir) işlemi.</summary>
    Service  = 5,

    /// <summary>Vadeli işlem.</summary>
    Future   = 6,

    /// <summary>Taş (değerli taş) alış-satış işlemi — parasal/adet, milyem/işçilik yok.</summary>
    Stone    = 7,

    /// <summary>Mücevher (bitmiş ürün) alış-satış işlemi — parasal/adet, company-scoped.</summary>
    Jewelry  = 8,

    /// <summary>Virman — hesaplar arası aktarım (satır-seviyesi karşı kayıt ile).</summary>
    Transfer = 11,

    /// <summary>Çeşni — biriken çeşni stoğundan cariye metal verilmesi. Yön daima ÇIKIŞ.</summary>
    Assay    = 14,

    /// <summary>Takoz (külçe) giriş/çıkış işlemi.</summary>
    Bullion  = 15,

    /// <summary>Borç/Alacak dekontu (Türkçe UI "Dekont") — kategorili serbest tutar hareketi; Miktar alanı yok (0 gider).
    /// Legacy ERPPRO tIslemKodu.BORC=999 karşılığı — byte aralığı nedeniyle 99; import mapping'inde 999→99 çevrilir.</summary>
    DebitNote = 99,
}
