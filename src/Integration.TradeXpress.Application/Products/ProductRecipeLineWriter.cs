using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN REÇETESİ YAZARI — varyant kapsamında reçete satırlarını persist eden TEK yol
/// (<c>ProductAppService.SaveProductVariantDetailAsync</c>'ten 2026-08-06'da çıkarıldı).
///
/// <para><b>Neden ayrı sınıf:</b> sihirbazın sınıflandırma adımı (<see cref="ProductCommodityProvisioner"/>)
/// da reçete satırı yazar. İkinci bir yazım yolu açmak, iki-geçişli ClientKey çözümü ve LineOrder yeniden
/// numaralamayı iki yerde yaşatırdı — ilk sapma sessiz olurdu (bir yol türev referansları çözer, diğeri
/// çözmez gibi).</para>
///
/// <para><b>Davranış aynen taşındı</b>, kural değişikliği YOK: silinenler → hayatta kalanlar LineOrder 0..n
/// yeniden numaralanır → 1. geçiş skaler alanları yazar → 2. geçiş türev satırların kaynak ClientKey'lerini
/// çözülmüş Id CSV'sine çevirir.</para>
/// </summary>
public class ProductRecipeLineWriter : ITransientDependency
{
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;

    public ProductRecipeLineWriter(IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository)
    {
        _recipeLineRepository = recipeLineRepository;
    }

    /// <summary>Reçete grafını (varyant-scope; Id + IsDeleted diff, Account/SubAccount deseni) persist eder.
    /// Bileşen türü set-once (toolbar tip belirler); LineOrder korunur. Company + varyant Id (jenerik
    /// <c>EntityVariant.Id</c>) çağırandan gelir.</summary>
    public virtual async Task SaveAsync(Guid companyId, Guid variantId, List<ProductRecipeLineGraphDto> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return;
        }

        foreach (var l in lines.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _recipeLineRepository.DeleteAsync(l.Id, autoSave: true);
        }

        // Kalanları client sırasında (LineOrder) sırala + 0..n-1 YENİDEN NUMARALA → benzersiz/deterministik pozisyon.
        // Türev satırın "yalnız üsttekiler" referans filtresi + calculator ordinal'i bu sıraya dayanır.
        var survivors = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder).ToList();
        for (var i = 0; i < survivors.Count; i++)
        {
            survivors[i].LineOrder = i;
        }

        RecipeCostPopulator.ValidateDerivedReferences(survivors);

        // 1. geçiş: TÜM satırları insert/update (skaler alanlar; türev SelectedLines kaynakları HARİÇ) →
        // ClientKey→Id (+ ClientKey→entity) sözlükleri (iki-geçişli ClientKey→Id save deseni).
        var idByClientKey = new Dictionary<Guid, Guid>();
        var entityByClientKey = new Dictionary<Guid, ProductVariantRecipeLine>();
        foreach (var l in survivors)
        {
            ProductVariantRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new ProductVariantRecipeLine(companyId, variantId, l.ComponentType, l.LineOrder);
                ApplyFields(entity, l);
                await _recipeLineRepository.InsertAsync(entity, autoSave: true);
                l.Id = entity.Id;
            }
            else
            {
                entity = await _recipeLineRepository.GetAsync(l.Id);
                entity.SetOrder(l.LineOrder);
                ApplyFields(entity, l);
                await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
            }

            idByClientKey[l.ClientKey] = l.Id;
            entityByClientKey[l.ClientKey] = entity;
        }

        // 2. geçiş: türev SelectedLines satırlarının kaynak ClientKey'lerini çözülmüş Id CSV'sine çevir + persist
        // (kaynak Id'ler artık 1. geçişten hazır). AllAbove satırlarının kaynağı yok (SetDerived null'a düşürdü).
        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            var csv = string.Join('|', l.DerivedSourceKeys.Select(k => idByClientKey[k].ToString()));
            var entity = entityByClientKey[l.ClientKey];
            entity.SetDerivedSources(csv);
            await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
        }
    }

    /// <summary>Graf düğümünün alanlarını reçete satırına uygular — bileşen türüne göre katalog-emtia ya da
    /// hizmet/manuel setter grubu. ComponentType set-once olduğundan burada DEĞİŞTİRİLMEZ (ctor'da atanır).</summary>
    private static void ApplyFields(ProductVariantRecipeLine entity, ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            entity.SetCatalogCommodity(
                l.CommodityProcessType.GetValueOrDefault(),
                l.CommodityId,
                l.CommodityVariantId,
                l.Quantity,
                l.Amount,
                l.Factor,
                l.ValuationUnitId,
                l.PaymentType,
                l.PayFactor,
                l.PayUnitId);
        }
        else
        {
            // Hizmet satırı: hizmet referansı (etiket) + türevsel bedel kuralı (taban modu + işlem + operand);
            // SelectedLines kaynakları AYRICA 2. geçişte SetDerivedSources ile (Id'ler o aşamada çözülür).
            entity.SetService(
                l.CommodityId,
                l.DerivedBaseMode.GetValueOrDefault(RecipeDerivedBaseMode.AllAbove),
                l.DerivedOperation.GetValueOrDefault(RecipeDerivedOperation.Percent),
                l.DerivedOperand,
                l.PayUnitId);
        }

        entity.SetDescription(l.Description);
    }
}
