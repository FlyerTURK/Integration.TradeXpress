using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete şablonunu bir ÜRÜNÜN varyantlarına uygular — Hakan'ın "şablon devraldığı emtiaların ÜZERİNE işleyecek"
/// dediği adım.
///
/// <para><b>Değişmezler (hepsi bilinçli):</b></para>
/// <list type="number">
/// <item><b>Emtia satırlarına DOKUNULMAZ.</b> Muadillikten gelen (ya da kullanıcının elle girdiği) satırlar
/// reçetenin TABANIDIR; şablon onların üstüne ekler.</item>
/// <item><b>Şablon satırları EN SONA.</b> Hizmet satırları "üstümdeki her şeyin toplamı" üzerinden hesaplar
/// (<c>AllAbove</c>); tabanın üstünde durmazlarsa maliyet eksik çıkar.</item>
/// <item><b>Yalnız KENDİ satırlarını tazeler.</b> Yeniden uygulama <c>Origin=Template</c> satırlarını düşürüp
/// yeniden kurar; kullanıcı satırları (<c>Manual</c>) ve muadil satırları (<c>Substitution</c>) korunur.</item>
/// <item><b>Ürünle kalıcı bağ KURMAZ.</b> Şablon bir kaynaktır — sonradan şablonda yapılan değişiklik ona
/// "bağlı" ürünleri habersiz değiştirmez; kullanıcı yeniden uygulamayı açıkça ister.</item>
/// </list>
/// </summary>
public class RecipeTemplateApplier : ITransientDependency
{
    private const string ProductVariantEntityName = "Product";

    private readonly IRepository<RecipeTemplate, Guid> _templateRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public RecipeTemplateApplier(
        IRepository<RecipeTemplate, Guid> templateRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _templateRepository = templateRepository;
        _recipeLineRepository = recipeLineRepository;
        _variantRepository = variantRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>
    /// Şablonu ürünün TÜM varyantlarına uygular; etkilenen varyant sayısını döndürür.
    /// </summary>
    public virtual async Task<int> ApplyToProductAsync(Product product, Guid templateId)
    {
        var template = await _templateRepository.FindAsync(templateId);
        if (template is null || template.CompanyId != product.CompanyId)
        {
            // Başka şirketin şablonu id gönderilerek uygulanamaz (sahiplik sınırı).
            throw new BusinessException("TradeXpress:RecipeTemplate:NotFound");
        }

        var variantIds = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductVariantEntityName && v.EntityId == product.Id)
                .Select(v => v.Id));

        foreach (var variantId in variantIds)
        {
            await ApplyToVariantAsync(product.CompanyId, variantId, template);
        }

        return variantIds.Count;
    }

    /// <summary>
    /// Tek varyanta uygular: önce önceki şablon satırlarını düşürür, sonra korunan satırların ARDINA
    /// şablonu serer ve tüm satırları 0..n-1 yeniden numaralar.
    /// </summary>
    public virtual async Task ApplyToVariantAsync(Guid companyId, Guid variantId, RecipeTemplate template)
    {
        // (1) Önceki şablon satırlarını düşür — idempotent: aynı şablon iki kez uygulanınca satırlar KATLANMAZ.
        await _recipeLineRepository.DeleteAsync(
            l => l.ProductVariantId == variantId && l.Origin == RecipeLineOrigin.Template, autoSave: true);

        // (2) Korunanları (muadil + kullanıcı) sırala ve 0..k-1 yeniden numarala — şablon bunların ARDINA gelecek.
        var preserved = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == variantId)
                .OrderBy(l => l.LineOrder));

        for (var i = 0; i < preserved.Count; i++)
        {
            if (preserved[i].LineOrder != i)
            {
                preserved[i].SetOrder(i);
                await _recipeLineRepository.UpdateAsync(preserved[i], autoSave: true);
            }
        }

        // (3) Şablon satırlarını kendi sıralarını koruyarak ekle.
        var order = preserved.Count;
        foreach (var templateLine in template.Lines.OrderBy(l => l.LineOrder))
        {
            var line = BuildRecipeLine(companyId, variantId, templateLine, order++);
            await _recipeLineRepository.InsertAsync(line, autoSave: true);
        }
    }

    /// <summary>Şablon satırını reçete satırına çevirir — alan setleri AYNI olduğundan düz kopya.</summary>
    private static ProductVariantRecipeLine BuildRecipeLine(
        Guid companyId,
        Guid variantId,
        RecipeTemplateLine source,
        int lineOrder)
    {
        var line = new ProductVariantRecipeLine(companyId, variantId, source.ComponentType, lineOrder);

        if (source.ComponentType == RecipeComponentType.CatalogCommodity && source.CommodityProcessType is { } family)
        {
            line.SetCatalogCommodity(
                family,
                source.CommodityId,
                source.CommodityVariantId,
                source.Quantity,
                source.Amount,
                source.Factor,
                source.ValuationUnitId,
                source.PaymentType,
                source.PayFactor,
                source.PayUnitId);
        }
        else
        {
            // Hizmet satırı — taban DAİMA AllAbove (şablon satırı seçili-satır referansı taşıyamaz: o kimlikler
            // ürüne uygulandığında geçersizdir; gerekçe RecipeTemplateLine sınıf özetinde).
            line.SetService(
                source.CommodityId,
                RecipeDerivedBaseMode.AllAbove,
                source.DerivedOperation ?? RecipeDerivedOperation.Percent,
                source.DerivedOperand,
                source.PayUnitId);
            line.SetSideCostKind(source.SideCostKind);
        }

        line.SetDescription(source.Description);
        line.SetOrigin(RecipeLineOrigin.Template);
        return line;
    }
}
