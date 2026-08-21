using System;
using System.Collections.Generic;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SATIŞA HAZIRLIK PANELİNİN SNAPSHOT'I (2026-08-19) — <see cref="ProductSaleValidator"/>'ın tek girdisi. Değer-tipi,
/// DB'siz: doğrulama kuralı bu snapshot'a bakar, repository'ye değil. Böylece kural saf birim testle sürülür
/// (her issue için elle kurulmuş bir snapshot) ve "hangi sorgu ne getirdi" ile "hangi kural ne dedi" ayrışır.
///
/// <para><b>Yalnız AKTİF varyantlar taşınır</b> — pasif varyant satışa kapalıdır, kullanıcı bunu kendisi seçmiştir;
/// onun eksiklerini issue olarak göstermek gürültü olurdu (verifier da pasifi doğrulamaz).</para>
///
/// <para><b><c>RecipeTemplateId</c> neden snapshot'ta:</b> reçete şablonu seçimi 2026-08-20 Hakan kararıyla
/// ZORUNLUDUR ama <i>"bu zorunluluk veritabanı seviyesinde olmasın"</i> — yani kolon nullable kalır, entity/servis
/// reddetmez, zorunluluk yalnız SATIŞA HAZIRLIK PANELİNDE bir issue olarak yaşar. Bu yüzden alan doğrulayıcının
/// görebileceği tek yere, snapshot'a taşınır.</para>
/// </summary>
public sealed record ProductSaleSnapshot(
    Guid ProductId,
    string ProductCode,
    bool IsActive,
    ProductStockPolicy StockPolicy,
    ProductVariantMode VariantMode,
    bool HasCategory,
    int? VatRate,
    Guid? RecipeTemplateId,
    IReadOnlyList<ProductSaleVariantSnapshot> ActiveVariants,
    IReadOnlySet<Guid> SellableVariantIds,
    int ImageCount,
    bool HasPoster,
    IReadOnlyList<ProductSaleChannelSnapshot> Channels);

/// <summary>Aktif bir varyantın satışa hazırlık paneli için gereken hâli. <paramref name="SalePrice"/> null = fiyat girilmemiş
/// (push aday seti de aynı ölçütle eler: <c>SalePrice is not null</c>); detay kaydı hiç yoksa da null.
/// <paramref name="SalePriceCurrencyUnitId"/> = fiyatın para birimi (<c>ProductVariantDetail.SalePriceCurrencyUnitId</c> —
/// push satırının birim kaynağıyla AYNI alan): varyantlar arası karışık birim push'u keser (MixedCurrency), panel
/// bunu önceden görebilsin diye taşınır. Detay kuralı gereği fiyat null ise birim de null'dur.</summary>
public sealed record ProductSaleVariantSnapshot(
    Guid VariantId,
    string Code,
    decimal? SalePrice,
    Guid? SalePriceCurrencyUnitId,
    ProductSaleStatus SaleStatus,
    IReadOnlyList<ProductSaleRecipeLineSnapshot> RecipeLines);

/// <summary>Reçete satırının satışa hazırlık paneli için gereken kısmı: tür, aile, adet/miktar, etiket.</summary>
public sealed record ProductSaleRecipeLineSnapshot(
    int LineOrder,
    RecipeComponentType ComponentType,
    ProcessType? CommodityFamily,
    decimal Quantity,
    decimal Amount,
    string? Description);

/// <summary>Kanal ürününün satışa hazırlık paneli için gereken özeti — kanal-agnostik sinyaller. Kanal-özel ham alanlar
/// (batch id, task id) burada değil <c>ChannelReadinessRowDto</c>'da taşınır; validator yalnız karara bakar.</summary>
public sealed record ProductSaleChannelSnapshot(
    Guid ChannelProductId,
    SalesChannelType ChannelType,
    string ChannelLabel,
    bool IsActive,
    bool IsListed,
    bool IsPending,
    bool IsStale,
    string? LastError,
    string? Obstacle,
    bool MissingRequiredFields);
