using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>Bir kategorinin kanal eşleştirmeleri (kanal başına en fazla bir satır).</summary>
public class ProductCategoryChannelMappingDto : EntityDto<Guid>, IListDto<Guid>
{
    /// <summary>In-memory drill satır kimliği — persist EDİLMEZ. Grid satırları bununla ayırt eder; kaydedilmemiş
    /// satırın <see cref="EntityDto{TKey}.Id"/>'si Guid.Empty olduğundan Id anahtar olarak kullanılamaz
    /// (iki yeni satır aynı anahtara düşer ve grid seçim/başlık durumunu hesaplarken çakışır).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public Guid ProductCategoryId { get; set; }

    public SalesChannelType Channel { get; set; }

    [Required]
    [StringLength(ProductCategoryChannelMappingConsts.ChannelCategoryIdMaxLength)]
    public string ChannelCategoryExternalId { get; set; } = string.Empty;

    [StringLength(ProductCategoryChannelMappingConsts.ChannelCategoryNameMaxLength)]
    public string? ChannelCategoryName { get; set; }

    /// <summary>Bu kategorinin bu KANALDAKİ nitelik eşleştirmeleri ("Ayar" → N11 "Maden Ayarı").
    /// Kategori eşleştirmesiyle BİRLİKTE okunur/kaydedilir: ikisi tek bir kullanıcı kararının parçası
    /// (kanal kategorisi seçildikten sonra o kategorinin nitelikleri eşleştirilir) ve ayrı uçlara bölünseydi
    /// yarım kaydedilmiş bir eşleştirme mümkün olurdu.</summary>
    public List<ProductCategoryChannelAttributeMappingDto> AttributeMappings { get; set; } = new();

    /// <summary>Bu eşleştirmenin ÇÖZÜLMÜŞ efektif komisyon oranı (kategori komisyonu + kanalın zorunlu hizmet
    /// bedelleri, KDV brütüyle). Salt gösterim: kullanıcı eşleştirmenin fiyata etkisini anında görsün.
    /// <c>null</c> = kanal taksonomisinde oran yok ya da kategori çözümlenemedi.</summary>
    public decimal? EffectiveCommissionRate { get; set; }

    public override string ToString()
    {
        return $"{Channel}:{ChannelCategoryExternalId}";
    }
}

/// <summary>Eşleştirme kur/değiştir girdisi — kanal başına tek satır olduğundan "upsert" semantiği taşır.</summary>
public class ProductCategoryChannelMappingSaveDto
{
    public SalesChannelType Channel { get; set; }

    [Required]
    [StringLength(ProductCategoryChannelMappingConsts.ChannelCategoryIdMaxLength)]
    public string ChannelCategoryExternalId { get; set; } = string.Empty;

    [StringLength(ProductCategoryChannelMappingConsts.ChannelCategoryNameMaxLength)]
    public string? ChannelCategoryName { get; set; }

    /// <summary>Nitelik eşleştirmeleri — gönderilen liste o kanal için TAM kümedir (gelmeyen satır silinir).
    /// Kısmi güncelleme desteklenmez: form zaten tüm nitelikleri gösterir, kısmi kabul etmek "sildim ama
    /// gitmedi" davranışı üretirdi.</summary>
    public List<ProductCategoryChannelAttributeMappingDto> AttributeMappings { get; set; } = new();
}

/// <summary>
/// Çekirdek nitelik ↔ kanal niteliği eşleştirmesi. <see cref="ProductCategoryAttributeId"/> çekirdek niteliğin
/// KALICI kimliği, <see cref="ChannelAttributeExternalId"/> kanaldaki karşılığının kimliğidir.
/// <para>Adlar salt GÖSTERİM — sunucu kaydetmede yok sayar; doğruluk kimliklerde durur.</para>
/// </summary>
public class ProductCategoryChannelAttributeMappingDto
{
    public Guid ProductCategoryAttributeId { get; set; }

    /// <summary>Çekirdek nitelik adı — salt gösterim ("Ayar").</summary>
    public string AttributeName { get; set; } = string.Empty;

    [StringLength(ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength)]
    public string? ChannelAttributeExternalId { get; set; }

    /// <summary>Kanal niteliğinin adı — salt gösterim ("Maden Ayarı").</summary>
    [StringLength(ProductCategoryChannelMappingConsts.ChannelAttributeNameMaxLength)]
    public string? ChannelAttributeName { get; set; }

    /// <summary>Bu niteliğin DEĞER eşleştirmeleri — çekirdek değer ("22K") → kanal değeri ("22 Ayar").
    /// Nitelikle BİRLİKTE okunur/kaydedilir: değer eşleştirmesi niteliğinden bağımsız anlamlı değildir.</summary>
    public List<ProductCategoryChannelAttributeValueMappingDto> ValueMappings { get; set; } = new();

    public override string ToString()
    {
        return AttributeName + " → " + (ChannelAttributeName ?? ChannelAttributeExternalId ?? "-");
    }
}

/// <summary>
/// Bir ürün için ÇÖZÜLMÜŞ kanal bağlamı — ürünün kategorisi (ya da en yakın atası) üzerinden.
/// Ürün formu ve fiyatlama bunu okur; kanal ürünü hiç oluşturulmamış olsa bile komisyon bilinir.
/// </summary>
public class ProductChannelResolutionDto
{
    public SalesChannelType Channel { get; set; }

    /// <summary>Eşleştirmeyi SAĞLAYAN kategori — ürünün kendi kategorisi ya da bir ATASI (kalıtım).</summary>
    public Guid? SourceCategoryId { get; set; }

    public string? SourceCategoryName { get; set; }

    /// <summary>Eşleştirme devralındı mı (ürünün kendi kategorisinden değil, bir üstünden).</summary>
    public bool IsInherited { get; set; }

    public string? ChannelCategoryExternalId { get; set; }

    public string? ChannelCategoryName { get; set; }

    /// <summary>Efektif GrossUp komisyon oranı — reçeteye bu oran girer. <c>null</c> = çözülemedi.</summary>
    public decimal? EffectiveCommissionRate { get; set; }
}

/// <summary>
/// Çekirdek nitelik DEĞERİ ↔ kanal değeri eşleştirmesi ("22K" → N11'in "22 Ayar" değeri).
///
/// <para>Pazaryerleri çoğu nitelikte kendi değer listelerinden KİMLİK bekler; ada göre gönderim tutmaz.
/// Kanal değeri seçilmemiş satır eşleştirme değildir ve kaydedilmez.</para>
/// </summary>
public class ProductCategoryChannelAttributeValueMappingDto
{
    public Guid ProductCategoryAttributeValueId { get; set; }

    /// <summary>Çekirdek değer metni — salt gösterim ("22K").</summary>
    public string ValueText { get; set; } = string.Empty;

    [StringLength(ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength)]
    public string? ChannelValueExternalId { get; set; }

    /// <summary>Kanal değerinin adı — salt gösterim ("22 Ayar").</summary>
    [StringLength(ProductCategoryChannelMappingConsts.ChannelAttributeNameMaxLength)]
    public string? ChannelValueName { get; set; }

    public override string ToString()
    {
        return ValueText + " → " + (ChannelValueName ?? ChannelValueExternalId ?? "-");
    }
}
