namespace Integration.TradeXpress.EtsyProducts;

/// <summary>Etsy ürün listeleme (SalesChannelEtsyProduct) alan sınırları. N11ProductConsts deseninin Etsy karşılığı.</summary>
public static class SalesChannelEtsyProductConsts
{
    /// <summary>Kayıt-bazlı Etsy satıcı SKU tabanı ("{ÜrünKodu}-{SequenceNo}"). Ürün kodu (32) + ek payı.</summary>
    public const int SellerSkuBaseMaxLength = 64;

    /// <summary>Varyant dondurulmuş SKU ("{VaryantKodu}-{SequenceNo}") — satıcı-geneli benzersiz kanal kimliği.</summary>
    public const int StockCodeMaxLength = 128;

    // ── Etsy listing seviyesi ──
    /// <summary>Etsy listeleme başlığı override (<c>title</c>; boşsa push'ta ürün adı devralınır). Etsy sınırı 140.</summary>
    public const int TitleOverrideMaxLength = 140;

    /// <summary>Etsy kanal-özel açıklama override (<c>description</c>; boşsa ürün açıklaması devralınır).</summary>
    public const int DescriptionOverrideMaxLength = 20000;

    // ── Kişiselleştirme SORULARI (2026-07-28: Etsy'nin ÇOKLU soru modeli) ──
    // Eski tek-kutulu model (personalization_instructions / _is_required / _char_count_max) Etsy tarafında
    // 9 Nisan 2026'da KAPANDI — spec'in kendi notu: "This field will be removed on Apr. 9th, 2026."
    // Yerine listing başına 1-5 adet ADLANDIRILMIŞ soru geldi; her sorunun başlığını satıcı yazar.
    // Bizde bu soruların taşıyıcısı SpecialInfo'dur (aynı kavram N11 SOAP'ta da yaşıyor, orası DEĞİŞMEDİ).

    /// <summary>Listeleme başına kişiselleştirme sorusu üst sınırı — Etsy sınırı 5.</summary>
    public const int MaxSpecialInfoCount = 5;

    /// <summary>Bir sorunun alabileceği cevabın karakter tavanı (<c>max_allowed_characters</c>) — Etsy sınırı 1024.
    /// Soru BAŞINA belirlenir; değer verilirse 1..bu aralıkta olmalı (entity fail-fast).</summary>
    public const int SpecialInfoMaxAllowedCharactersLimit = 1024;

    /// <summary>Etsy etiketi (<c>tags</c>) tek eleman uzunluğu — Etsy sınırı 20 karakter.</summary>
    public const int TagMaxLength = 20;

    /// <summary>Etsy malzemesi (<c>materials</c>) tek eleman uzunluğu — Etsy sınırı 45 karakter.</summary>
    public const int MaterialMaxLength = 45;

    /// <summary>Etsy etiket/malzeme listesinin üst eleman sayısı (Etsy sınırı 13).</summary>
    public const int MaxTagCount = 13;
    public const int MaxMaterialCount = 13;

    // ── Ortak listeleme ──
    /// <summary>Satıcı notu (kanal-özel kısa düz metin).</summary>
    public const int SellerNoteMaxLength = 500;

    // ── Etsy'ye yazma (create/update) giriş sınırları ──
    /// <summary>Dükkân bölümü başlığı (<c>title</c>) — Etsy sınırı 24 karakter.</summary>
    public const int ShopSectionTitleMaxLength = 24;

    /// <summary>İade politikası iade süresi (<c>return_deadline</c>, gün) üst tavanı — Etsy sınırı 90 gün.</summary>
    public const int ReturnDeadlineMaxDays = 90;

    // ── Owned taksonomi (varyasyon-DIŞI) attribute + özel bilgi (JSON) alan sınırları ──
    public const int ListingAttributeNameMaxLength = 256;
    public const int ListingAttributeValueMaxLength = 4000;
    /// <summary>Soru başlığı (<c>question_text</c>) — Etsy sınırı 45 karakter (eskiden 64 idi, push'u kırardı).</summary>
    public const int SpecialInfoKeyMaxLength = 45;
    public const int SpecialInfoValueMaxLength = 20000;   // HTML olabilir

    // ── Kanal-özel varyant ÖZELLİĞİ/DEĞERİ (SalesChannelEtsyProductAttribute/Value) — ERP ProductAttributeConsts
    // ile HİZALI (klon-sonra-ayrış deseni; aynı alan sınırları). ──
    public const int AttributeNameMaxLength = 64;    // ör. "Renk", "Beden"
    public const int AttributeValueMaxLength = 128;  // ör. "Kırmızı", "Siyah"

    /// <summary>Kartezyen kombinasyon imzası ("{AttributeId}={ValueId}|...") üst sınırı.</summary>
    public const int CombinationSignatureMaxLength = 600;

    // ── Etsy senkron durumu ──
    /// <summary>Etsy listeleme durumu (<c>state</c>: draft/active/inactive).</summary>
    public const int ListingStateMaxLength = 32;
    public const int LastErrorMaxLength = 2000;
}
