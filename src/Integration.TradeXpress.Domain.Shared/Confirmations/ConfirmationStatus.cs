namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// <b>Teyit</b> (organizasyon-içi karşılıklı ayna onayı) yaşam döngüsü. İki iç taraf (kasa↔kasa veya
/// kasa↔cari) arası bir process, iki tarafça teyitlenmeden POSTLANMAZ (zero-trust: tek taraflı beyan
/// ötekinin defterini kımıldatmaz). Değer, teyit kapanana dek gönderenin sorumluluğundadır ("yolda").
/// <para><b>NOT:</b> Bu "Teyit"tir — işlem-seviyesi ayna onayı. Bakiye/ilişki seviyesindeki denkleştirme
/// (<c>SettledBy</c>/<c>SettlementDate</c>) ayrı bir kavramdır: <b>Mutabakat</b>. Karıştırma.</para>
/// </summary>
public enum ConfirmationStatus : byte
{
    /// <summary>Teklif — gönderen çıkışı kaydetti; alıcının GELEN'inde onayı bekleniyor. Postlama YOK.
    /// Gönderen İptal, alıcı Reddedebilir.</summary>
    Proposed = 0,

    /// <summary>Beyan edildi — alıcı, KENDİ ELİYLE kendi girişini oluşturdu (sistem aynalamaz) ve bu kayıt
    /// gönderenin çıkışıyla AYNA doğrulandı; gönderenin GELEN'inde teyidi bekleniyor. Postlama YOK.</summary>
    Declared = 1,

    /// <summary>Teyitlendi — gönderen alıcının kaydını teyit etti; iki bacak (gönderen −, alıcı +) atomik
    /// postlandı, kapandı.</summary>
    Confirmed = 2,

    /// <summary>Reddedildi — alıcı kabul etmedi; postlanmış bacak yok, yalnız durum kapanır. (Gönderen teklifi
    /// GERİ ÇEKEMEZ — iptal yoktur; süreci yalnız alıcı reddederek durdurabilir.)</summary>
    Rejected = 3,
}
