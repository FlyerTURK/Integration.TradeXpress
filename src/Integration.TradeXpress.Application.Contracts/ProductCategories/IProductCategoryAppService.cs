using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.ProductCategories;

public interface IProductCategoryAppService : ICrudAppService<
    ProductCategoryGetDto,
    ProductCategoryListDto,
    Guid,
    ProductCategoryListRequestDto,
    ProductCategoryCreateDto,
    ProductCategoryUpdateDto>
{
    /// <summary>Combo/ağaç picker — aktif kategoriler, yol sıralı.</summary>
    Task<List<ProductCategoryListDto>> GetPickerListAsync();

    /// <summary>"Üst kategori" seçenekleri: <paramref name="excludeId"/>'nin kendisi ve TÜM ALT AĞACI düşülmüş
    /// hâli (kendi torununu üst seçmek döngü kurardı). Yeni kayıtta <c>null</c> geçilir.</summary>
    Task<List<ProductCategoryListDto>> GetParentOptionsAsync(Guid? excludeId);

    /// <summary>Kalıtım çözülmüş ETKİN nitelikler (atalarınkiler + kendisininkiler birleşik). Ürün formu ve
    /// pazaryeri ön-doldurması bunu okur — tek tek kategori zinciri yürümez.</summary>
    Task<List<ProductCategoryEffectiveAttributeDto>> GetEffectiveAttributesAsync(Guid id);

    /// <summary>
    /// Kaydetmeden ÖNCE kalıtımı önizler: verilen üst kategorinin (ve onun TÜM atalarının) nitelikleri,
    /// formdaki kendi nitelikleriyle birleştirilmiş hâlde döner. Kullanıcı üst kategoriyi seçer seçmez grid
    /// devralınanları göstersin diye vardır.
    ///
    /// <para>Birleştirme SUNUCUDA yapılır — kural tek yerde kalsın (<c>ProductCategoryTreeManager.MergeAttributes</c>).
    /// İstemcide tekrarlansaydı iki uygulama zamanla ayrışır ve önizleme ile kayıt sonucu farklı olurdu.</para>
    /// </summary>
    Task<List<ProductCategoryAttributeDto>> PreviewInheritanceAsync(ProductCategoryInheritancePreviewDto input);

    /// <summary>Kategorinin KENDİ kanal eşleştirmeleri (devralınanlar dahil değil) + her birinin çözülmüş
    /// komisyon oranı.</summary>
    Task<List<ProductCategoryChannelMappingDto>> GetChannelMappingsAsync(Guid id);

    /// <summary>Kanal eşleştirmesini kurar ya da değiştirir (kanal başına TEK satır — upsert).</summary>
    Task<ProductCategoryChannelMappingDto> SaveChannelMappingAsync(Guid id, ProductCategoryChannelMappingSaveDto input);

    /// <summary>Kanal eşleştirmesini kaldırır. Kaldırıldığında kategori ATASININ eşleştirmesini devralmaya döner.</summary>
    Task DeleteChannelMappingAsync(Guid id, SalesChannelType channel);

    /// <summary>Verilen kategori için bir kanalın ÇÖZÜLMÜŞ bağlamı (kalıtım dahil) — hangi kategoriden geldiği
    /// ve efektif komisyon oranı ile. Ürün formu "bu ürün N11'de hangi kategoriye gidecek, komisyonu ne" sorusunu
    /// buradan yanıtlar.</summary>
    Task<ProductChannelResolutionDto> ResolveChannelAsync(Guid id, SalesChannelType channel);
}
