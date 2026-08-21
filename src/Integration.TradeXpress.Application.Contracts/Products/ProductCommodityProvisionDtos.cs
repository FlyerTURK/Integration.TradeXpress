using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SINIFLANDIRILMAMIŞ ÜRÜN — reçetesi olmadığı için stok/maliyet zincirine hiç girmeyen kayıt.
/// <para>Mağaza içe aktarımı ürünü <c>StockPolicy=Fixed</c> + reçetesiz getiriyor; orkestrasyonun hesaplayacağı
/// bir şey olmuyor ve pazaryerinin eski adedi geçerli olmayı sürdürüyor. Sihirbazın sınıflandırma adımı bu
/// listeyi kapatmak içindir.</para>
/// </summary>
public class ProductCommodityCandidateDto
{
    public Guid ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Varyant sayısı — sınıflandırma TÜM varyantlara aynı emtiayı bağlar (kullanıcı kaç satırın
    /// etkileneceğini görmeli).</summary>
    public int VariantCount { get; set; }

    /// <summary>Ürünün hangi kanal(lar)dan geldiği — kullanıcıya bağlam verir (ör. "Trendyol").</summary>
    public string? Origin { get; set; }
}

/// <summary>Kullanıcının TEK bir ürün için verdiği sınıflandırma kararı.</summary>
public class ProductCommodityProvisionItemDto
{
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Emtia ailesi — Metal · Scrap · Future · Jewelry · Stone · Good · <b>Service</b>.
    /// <para>Service AYRI davranır: katalog kaydı AÇILMAZ, reçeteye ücret satırı yazılır ve ürün
    /// <c>Unlimited</c> olur (hizmetin stoğu yoktur).</para></summary>
    [Required]
    public ProcessType Family { get; set; }

    public ProductCommodityProvisionMode Mode { get; set; }

    /// <summary><see cref="ProductCommodityProvisionMode.UseExisting"/> ve
    /// <see cref="ProductCommodityProvisionMode.CloneExisting"/>'de ZORUNLU — kullanılacak ya da ŞABLON
    /// alınacak mevcut katalog kaydı.</summary>
    public Guid? ExistingCommodityId { get; set; }

    /// <summary>MİLYEM / katsayı (Maden <c>Factor</c>, Hurda <c>Factor</c>, Vadeli <c>FollowingFactor</c>).
    /// <para><b>Metal tarafı olan ailelerde YENİ kayıt açarken ZORUNLU</b> (2026-08-06): boş bırakılırsa entity
    /// varsayılanına düşerdi (Maden 0.995 / Hurda 0.570) ve bu MAKUL GÖRÜNEN BİR TAHMİNDİR — 22 ayar bilezik
    /// 0.916'dır. Sessizce her değerlemeye girer. Sistem tahmin etmez, kullanıcı beyan eder.</para>
    /// <para>Klonda GEREKMEZ: kopya değeri kaynak kayıttan devralır.</para></summary>
    public decimal? Factor { get; set; }

    /// <summary>Adet→gram katsayısı (<c>StableQuantity</c>) — adetli emtiada bir adedin kaç gram olduğu.
    /// null/0 = gram bazlı takip (bu bir MOD beyanıdır, uydurulmuş sayı değil).</summary>
    public decimal? StableQuantity { get; set; }

    /// <summary>Yeni emtianın kodu (boşsa üründen türetilir + benzersizleştirilir).</summary>
    public string? Code { get; set; }

    /// <summary>Yeni emtianın adı (boşsa ürünün adı).</summary>
    public string? Name { get; set; }

    /// <summary>Doğal birim — Metal/Scrap/Future'da <b>ZORUNLU</b> (katalog kaydı onsuz doğamaz) ve
    /// ön-doldurulamaz: takip edilen birim iş kararıdır, üründen türetilemez.</summary>
    public Guid? FollowingUnitId { get; set; }

    /// <summary>Bir birim ürün için gereken ADET (mamül/adetli emtiada). 0 = adet kısıtı yok.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Bir birim ürün için gereken MİKTAR (madende gram). 0 = miktar kısıtı yok.</summary>
    public decimal Amount { get; set; }
}

/// <summary>Sihirbaz adımının sunucuya gönderdiği TEK çağrı — istemci ürün başına ayrı istek atmaz
/// (103 ürün × 3 çağrı = 300+ round-trip; Blazor Server circuit'inde kabul edilemez).</summary>
public class ProductCommodityProvisionInputDto
{
    public List<ProductCommodityProvisionItemDto> Items { get; set; } = new();
}

/// <summary>Sınıflandırma sonucu — sihirbazın özet adımında gösterilir.</summary>
public class ProductCommodityProvisionResultDto
{
    public int ProvisionedProducts { get; set; }
    public int CreatedCommodities { get; set; }
    public int CreatedRecipeLines { get; set; }

    /// <summary>Otorite devrinde temizlenen kanal override'ı sayısı (pazaryerinin eski stok/fiyatı).</summary>
    public int ClearedChannelOverrides { get; set; }

    /// <summary>Atlanan/kısmen işlenen satırların gerekçeleri — SESSİZ geçilmez; kullanıcı neyin
    /// yapılmadığını görmeli.</summary>
    public List<string> Issues { get; set; } = new();
}
