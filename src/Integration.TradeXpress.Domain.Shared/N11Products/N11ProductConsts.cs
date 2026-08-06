using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 ürün listeleme (SalesChannelTrN11Product) alan sınırları.</summary>
public static class N11ProductConsts
{
    /// <summary>N11'in listelemede kabul ettiği para birimi kodları (2026-07-11 kullanıcı kararı: yalnız bu 3).
    /// Kodlar normalize (UPPER-invariant) saklandığından ordinal karşılaştırma yeterli.</summary>
    public static readonly IReadOnlyCollection<string> SupportedCurrencyCodes =
        new HashSet<string>(StringComparer.Ordinal) { "TRY", "USD", "EUR" };

    /// <summary>N11'in kabul ettiği KDV oranları (resmî v9.0 REST dokümanı) — serbest yüzde DEĞİL, kapalı küme.
    /// Kuyumcuda kritik: külçe/hurda ile işçilikli mücevher farklı orana tabidir; yanlış oran N11'in müşteriye
    /// YANLIŞ fatura kesmesine ve farkın satıcıya rücu edilmesine yol açar → oran tahmin edilmez, kullanıcı seçer.
    /// Hem entity guard'ı hem REST istemci doğrulaması bu tek kaynağı okur (SSOT).</summary>
    public static readonly IReadOnlyCollection<int> AllowedVatRates =
        new HashSet<int> { 0, 1, 10, 20 };

    /// <summary>Kargoya verilme süresi (preparingDay) alt/üst sınırı — N11 resmî hata sözlüğü (destek merkezi
    /// makale 10433): "preparingDay 1 değerinden büyük yada eşit olmalı" + "30 değerinden küçük yada eşit olmalı".</summary>
    public const int MinPreparingDay = 1;
    public const int MaxPreparingDay = 30;

    /// <summary>REST yazma uçlarının döndürdüğü task kimliği. Doküman örnekleri sayısal (ör. 2904402104) ama
    /// tip garantisi vermediği için string saklanır — genişlerse kırılmasın.</summary>
    public const int TaskIdMaxLength = 64;


    /// <summary>N11 kategori/ürün id'si (numerik ama matematik yapılmaz → string).</summary>
    public const int ExternalIdMaxLength = 32;

    /// <summary>Kayıt-bazlı N11 upsert kimliği ("{ÜrünKodu}-{SequenceNo}"). Ürün kodu (32) + ek payı.</summary>
    public const int SellerCodeMaxLength = 64;

    public const int CategoryNameMaxLength = 512;
    public const int ShipmentTemplateNameMaxLength = 128;
    public const int StatusMaxLength = 32;
    public const int LastErrorMaxLength = 2000;

    /// <summary>N11 satıcı notu (sellerNote) — kanal-özel kısa düz metin (MemoEdit).</summary>
    public const int SellerNoteMaxLength = 500;

    /// <summary>N11 kanal-özel açıklama (description; HTML — DxHtmlEditor). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public const int DescriptionMaxLength = 20000;

    // Grup ürün (SaveProduct opsiyonel): aynı grup üyeleri groupItemCode'da eşleşir, groupAttribute grubu ayıran
    // özellik adı (ör. "Renk"), itemName grup içindeki öğe adı. N11 belgesi net üst sınır vermez → makul limit.
    public const int GroupItemCodeMaxLength = 64;
    public const int GroupAttributeMaxLength = 64;
    public const int ItemNameMaxLength = 128;

    /// <summary>Varyant SKU stok kodu ("{VaryantKodu}-{SequenceNo}") — satıcı-geneli benzersiz N11 kimliği.</summary>
    public const int StockCodeMaxLength = 128;

    // Attribute / özel bilgi (owned → JSON) alan sınırları.
    public const int CategoryAttributeNameMaxLength = 256;
    public const int CategoryAttributeValueMaxLength = 4000;
    public const int SpecialInfoKeyMaxLength = 64;
    public const int SpecialInfoValueMaxLength = 20000;   // HTML olabilir

    // Kanal-özel varyant ÖZELLİĞİ/DEĞERİ (SalesChannelTrN11ProductAttribute/Value) — ERP ProductAttributeConsts
    // (Products/ProductAttributeConsts.cs) ile HİZALI (klon-sonra-ayrış deseni; aynı alan sınırları).
    public const int AttributeNameMaxLength = 64;    // ör. "Renk", "Beden"
    public const int AttributeValueMaxLength = 128;  // ör. "Kırmızı", "Siyah"

    /// <summary>Kartezyen kombinasyon imzası ("{AttributeId}={ValueId}|...") üst sınırı — makul özellik sayısı × Guid uzunluğu.</summary>
    public const int CombinationSignatureMaxLength = 600;
}

/// <summary>Push GEÇMİŞİ (append-only delil kaydı) alan sınırları — ayrı sınıf: bunlar N11'in sözleşme
/// sınırları DEĞİL, bizim delil kaydımızın saklama sınırlarıdır; ikisini karıştırmak yanlış guard doğurur.</summary>
public static class N11PushHistoryConsts
{
    public const int CurrencyTypeMaxLength = 8;

    /// <summary>Başlık — N11'in kendi sınırından bağımsız, gönderileni AYNEN saklamaya yeter.</summary>
    public const int TitleMaxLength = 512;

    /// <summary>"ad=değer; ad=değer" birleşimi — makul varyant ekseni sayısı × değer uzunluğu.</summary>
    public const int VariantOptionsMaxLength = 2000;

    /// <summary>"{mediaId:N}:{sha256-hex}" × görsel sayısı. 32 + 1 + 64 + ayraç ≈ 98 → 10 görsel için bol.</summary>
    public const int ImagesMaxLength = 1200;

    /// <summary>N11'in döndürdüğü task/ürün kimliği.</summary>
    public const int RemoteReferenceMaxLength = 128;

    // Fiyat: tutar N2 (financials.md yuvarlama kuralı).
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
}
