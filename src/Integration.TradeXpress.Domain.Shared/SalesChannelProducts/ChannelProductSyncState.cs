namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Kanal-ürününün pazaryeri ile SENKRON durumu — KANAL-AGNOSTİK (nötr) gösterim.
///
/// <para><b>Neden nötr bir enum:</b> üç kanalın senkron alanları birbirine benzemez — N11 sayısal
/// <c>N11ProductId</c> + iki ayrı durum metni (<c>SaleStatus</c>/<c>ApprovalStatus</c>) taşır, Trendyol
/// asenkron <c>BatchRequestId</c> + <c>FailedItemCount</c> ile konuşur, Etsy tek <c>EtsyListingId</c> +
/// <c>ListingState</c>. Birleşik listede bu üç dili yan yana göstermek kullanıcıyı kanal-özel jargonu
/// öğrenmeye zorlardı. Ham alanlar KAYBOLMAZ (satırda uzak kimlik ve son hata ayrıca taşınır);
/// bu enum yalnız "şu an ne durumdayım" sorusunun ortak cevabıdır.</para>
///
/// <para><b>ÖNCELİK SIRASI ÜÇ KANALDA DA AYNIDIR</b> (<c>SalesChannelProductAppService</c> uygular):
/// <c>Failed → Pending → Sent → NotSent</c>. Yani son denemesi hata vermiş bir kayıt, pazaryerinde
/// canlı olsa bile <see cref="Failed"/> görünür — çünkü listenin işi "hangi satır ELİMİ bekliyor"
/// sorusuna cevap vermektir, envanter beyanı yapmak değil. Kaydın gerçekten yayında olduğu bilgisi
/// aynı satırdaki uzak kimlik kolonundan okunur; iki bilgi birbirinin yerine geçmez.</para>
/// </summary>
public enum ChannelProductSyncState
{
    /// <summary>Pazaryerine hiç gönderilmemiş — yalnız yerelde duruyor.</summary>
    NotSent = 0,

    /// <summary>Gönderim yolda: akıbeti HENÜZ BELLİ DEĞİL (N11 bekleyen task, Trendyol kapanmamış batch).
    /// Etsy bu duruma GİRMEZ — senkron (anlık) yazar, ara durum üretmez.</summary>
    Pending = 1,

    /// <summary>BİZİM gönderdiğimiz kanıtlı (başarılı senkronun <c>LastSyncedAt</c> zamanı dolu) ve son denemesi hatasız.
    /// <para><b>Uzak kimliğin varlığı YETMEZ</b> — 2026-08-10'da bu yüzden yanlıştı: içe aktarılan ürünün
    /// uzak kimliği ithal anında dolduğu için hiç göndermediğimiz kayıtlar "Gönderildi" görünüyordu.
    /// Kanıt artık <c>LastSyncedAt</c>'tır; kimlik yalnız "orada var" der.</para></summary>
    Sent = 2,

    /// <summary>Son deneme hata verdi — kaydın pazaryerinde canlı olup olmamasından BAĞIMSIZ (bkz. tip özeti).</summary>
    Failed = 3,

    /// <summary>Pazaryerinde KARŞILIĞI VAR ama BİZ hiç göndermedik — kayıt oraya içe aktarımla girdi.
    /// <para>Kendi durumu olmasının sebebi: "gönderildi" demek yalan, "gönderilmedi" demek ise eksik olurdu —
    /// ürün pazaryerinde canlı ve sipariş alabiliyor. Bu satırlar otonom fiyat/stok yönetimine henüz
    /// bağlanmamış demektir; ekranın gösterdiği en anlamlı iş listesi budur.</para></summary>
    Imported = 4,
}
