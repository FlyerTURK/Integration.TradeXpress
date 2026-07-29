using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>Kategori liste sorgusu (company-owned). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class ProductCategoryListRequestDto : ListRequestDto
{
    /// <summary>Yalnız bu üstün ÇOCUKLARI. <c>null</c> = filtre yok (tüm ağaç düz liste hâlinde döner —
    /// ağaç bileşeni tüm düğümleri tek seferde ister).</summary>
    public Guid? ParentId { get; set; }
}

public class ProductCategoryListDto : EntityDto<Guid>, IListDto<Guid>, IHasIsActive
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Kökten bu kategoriye kadar okunabilir yol ("Takı › Yüzük › Alyans"). Düz listede hiyerarşiyi
    /// görünür kılar — sunucu doldurur (istemci ağacı yeniden kurmaz).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Kök 0 olmak üzere seviye — ağaç/girinti gösterimi ve gruplama için.</summary>
    public int Level { get; set; }

    /// <summary>Bu kategorinin ETKİN bir kanal eşleştirmesi var mı — kendi satırı ya da bir ATASININ satırı
    /// (eşleştirme ata zincirinden devralınır, bkz. <c>ProductCategoryChannelResolver</c>). Ürün formu bununla
    /// uyarır: eşleştirmesiz kategorideki ürün pazaryerine listelenemez ve komisyonu çözülemez.
    ///
    /// <para><b>Neden ETKİN, "kendi" değil:</b> "Takı" düzeyinde yapılan tek eşleştirme tüm alt kategorileri
    /// kapsar; kendi satırına bakan bir bayrak, doğru kurulmuş her alt kategoriyi yanlışlıkla uyarırdı ve
    /// uyarı gürültüye dönüşüp gerçek eksikler görülmezdi.</para></summary>
    public bool HasChannelMapping { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

public class ProductCategoryGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    [Required]
    [StringLength(ProductCategoryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Üst kategori — boş/null = KÖK (ana kategori; ana kategorilerde üst seçilmez).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Kökten bu kategoriye kadar okunabilir yol ("Takı › Yüzük › Alyans") — salt gösterim.</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(ProductCategoryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>
    /// Kategorinin ETKİN nitelikleri — üst kategorilerden DEVRALINANLAR + kendi tanımladıkları, TEK liste
    /// (2026-07-28 Hakan kararı: "üst kategorinin attribute listesi attributes gridinde silinemez olarak
    /// otomatik eklenmeli"). Devralınan satırlar <see cref="ProductCategoryAttributeDto.IsInherited"/> ile
    /// işaretlidir; UI onları silinemez/adı değiştirilemez gösterir, sunucu da kaydetmede yok sayar.
    ///
    /// <para>Devralınan bir niteliğe KENDİ değerini eklemek serbesttir: kaydetmede o kategoride aynı adlı bir
    /// nitelik açılır ve yalnız kendi değerleri ona yazılır (kalıtım birleştirmesi ikisini yine tek nitelik
    /// olarak gösterir). Kullanıcı bu ayrıntıyı görmez — "bu kategoride Ayar'a 22K ekledim" der.</para>
    /// </summary>
    public List<ProductCategoryAttributeDto> Attributes { get; set; } = new();
}

public class ProductCategoryCreateDto : ICreateDto
{
    [Required]
    [StringLength(ProductCategoryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(ProductCategoryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<ProductCategoryAttributeDto> Attributes { get; set; } = new();
}

public class ProductCategoryUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(ProductCategoryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(ProductCategoryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<ProductCategoryAttributeDto> Attributes { get; set; } = new();
}

/// <summary>
/// Kategori niteliği. <see cref="Id"/> KALICI kimliktir ve geri gönderilmesi ZORUNLUDUR: sunucu güncellemede
/// listeyi baştan kurmaz, MERGE eder (Id'si gelen satır güncellenir, gelmeyen silinir, boş Id yeni satırdır).
/// Sebep: bu kimliğe pazaryeri nitelik eşleştirmesi asılacak — her kaydetmede yeni Id üretmek eşleştirmeleri koparırdı.
/// <see cref="ClientKey"/> yalnız in-memory drill satır kimliğidir (persist EDİLMEZ; yeni satırların Id'si boş olduğundan gerekir).
/// </summary>
public class ProductCategoryAttributeDto
{
    public Guid Id { get; set; }

    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(EntityVariantConsts.AttributeNameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public ProductCategoryAttributeKind Kind { get; set; } = ProductCategoryAttributeKind.Specification;

    public int DisplayOrder { get; set; }

    /// <summary>Bu nitelik bir ÜST kategoriden mi geliyor. <c>true</c> ise burada silinemez ve adı/cinsi
    /// değiştirilemez — sahibi kategoride düzenlenir. Sunucu kaydetmede bu satırın kendisini yok sayar;
    /// yalnız altına eklenmiş KENDİ değerleri kalıcılaşır.</summary>
    public bool IsInherited { get; set; }

    /// <summary>Niteliği tanımlayan kategori (devralınanlarda üst kategorinin adı) — kullanıcı düzenlemek için
    /// nereye gitmesi gerektiğini görsün.</summary>
    public string? SourceCategoryName { get; set; }

    public List<ProductCategoryAttributeValueDto> Values { get; set; } = new();

    public override string ToString()
    {
        return Name;
    }
}

/// <summary>Nitelik değeri. <see cref="Id"/> semantiği nitelikle aynıdır (merge anahtarı + kanal değer eşleştirmesinin hedefi).</summary>
public class ProductCategoryAttributeValueDto
{
    public Guid Id { get; set; }

    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(EntityVariantConsts.AttributeValueMaxLength, MinimumLength = 1)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    /// <summary>Bu değer bir ÜST kategoriden mi geliyor. <c>true</c> ise burada salt-okunurdur (silinemez,
    /// düzenlenemez); kullanıcı kendi değerlerini aynı niteliğin altına serbestçe ekler.</summary>
    public bool IsInherited { get; set; }

    /// <summary>Değeri tanımlayan kategori (devralınanlarda üst kategorinin adı).</summary>
    public string? SourceCategoryName { get; set; }

    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
/// Kalıtım önizleme girdisi — üst kategori değiştiği anda (kaydetmeden) grid'in ne göstereceğini sormak için.
/// </summary>
public class ProductCategoryInheritancePreviewDto
{
    /// <summary>Seçilen üst kategori; <c>null</c>/boş = kök (devralınan nitelik olmaz).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Düzenlenmekte olan kategorinin KENDİ nitelikleri (formdaki hâliyle). Devralınan satırlar
    /// gönderilse de sunucu onları yok sayar — üst zinciri kendisi çözer.</summary>
    public List<ProductCategoryAttributeDto> OwnAttributes { get; set; } = new();
}

/// <summary>Kalıtım çözülmüş ETKİN nitelik (salt-okunur görünüm) — hangi kategoriden geldiği taşınır ki UI
/// devralınanı ayırt edebilsin ve kullanıcı düzenlemek için doğru kategoriye gidebilsin.</summary>
public class ProductCategoryEffectiveAttributeDto
{
    public Guid AttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProductCategoryAttributeKind Kind { get; set; }
    public int DisplayOrder { get; set; }

    public Guid SourceCategoryId { get; set; }
    public string SourceCategoryName { get; set; } = string.Empty;

    /// <summary><c>true</c> = üst kategoriden devralındı (burada salt-okunur).</summary>
    public bool IsInherited { get; set; }

    public List<ProductCategoryEffectiveAttributeValueDto> Values { get; set; } = new();

    public override string ToString()
    {
        return Name;
    }
}

public class ProductCategoryEffectiveAttributeValueDto
{
    public Guid ValueId { get; set; }
    public string Value { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public Guid SourceCategoryId { get; set; }
    public string SourceCategoryName { get; set; } = string.Empty;
    public bool IsInherited { get; set; }

    public override string ToString()
    {
        return Value;
    }
}
