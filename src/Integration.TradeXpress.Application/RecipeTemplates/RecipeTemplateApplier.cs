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
/// <item><b>Yalnız KENDİ DOKUNULMAMIŞ satırlarını tazeler.</b> Kullanıcı satırları (<c>Manual</c>), muadil
/// satırları (<c>Substitution</c>) ve kullanıcının sahiplendiği şablon satırları (<c>TemplateEdited</c> —
/// sahiplenme kayıt anında, <c>ProductRecipeLineWriter</c>) korunur; onların değeri EZİLMEZ.</item>
/// <item><b>Eşleme SIRASI: önce <c>SourceTemplateLineId</c>, yoksa eski davranış</b> (2026-08-21 Hakan onaylı
/// çoğalma düzeltmesi). Şablondan inen her satır artık kendisini doğuran şablon satırının kimliğini taşır:
/// dokunulmamış <c>Template</c> satırı kimliği üzerinden YERİNDE tazelenir (satır Id'si sabit kalır —
/// kullanıcının SelectedLines türev referansları kopmaz), kullanıcının sahiplendiği <c>TemplateEdited</c>
/// satırın şablon karşılığı YENİDEN KURULMAZ (kullanıcının sürümü o satırın yerine geçer) — böylece yeniden
/// uygulamada satır sayısı sabittir. <b>Kimliksiz (özellik öncesi) eski satırda eski davranış geçerlidir:</b>
/// dokunulmamış olan düşürülüp yeniden kurulur; düzenlenmiş olanın şablon karşılığı tanınamadığından yeniden
/// kurulur ve o kalem iki kez görünür. Bu SESSİZ değildir: sayısı
/// <see cref="RecipeTemplateApplyOutcome.PreservedEditedLineCount"/> ile geri döner ve kullanıcıya
/// söylenir; hangisinin kalacağına o karar verir.</item>
/// <item><b>Ürünle kalıcı bağ KURMAZ.</b> Şablon bir kaynaktır — sonradan şablonda yapılan değişiklik ona
/// "bağlı" ürünleri habersiz değiştirmez; kullanıcı yeniden uygulamayı açıkça ister.
/// <c>SourceTemplateLineId</c> bu kuralı bozmaz: canlı bir bağ değil, yalnız yeniden uygulamanın eşleme
/// anahtarıdır.</item>
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
    /// Şablonu ürünün TÜM varyantlarına uygular; etkilenen varyant sayısını ve HÂLÂ iki kez görünen kalem
    /// sayısını (yalnız kimliksiz — özellik öncesi — düzenlenmiş satırlar) döndürür
    /// (<see cref="RecipeTemplateApplyOutcome"/>).
    /// </summary>
    public virtual async Task<RecipeTemplateApplyOutcome> ApplyToProductAsync(Product product, Guid templateId)
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

        var preservedEditedLines = 0;
        foreach (var variantId in variantIds)
        {
            preservedEditedLines += await ApplyToVariantAsync(product.CompanyId, variantId, template);
        }

        return new RecipeTemplateApplyOutcome(variantIds.Count, preservedEditedLines);
    }

    /// <summary>
    /// Tek varyanta uygular: şablondan inmiş satırları <c>SourceTemplateLineId</c> ile eşler (dokunulmamış →
    /// yerinde tazele · sahiplenilmiş → atla · kimliksiz/artık şablonda olmayan → düşür), korunan satırların
    /// ARDINA eksik şablon satırlarını serer ve tüm satırları 0..n-1 yeniden numaralar. Dönüş: bu varyantta
    /// HÂLÂ iki kez görünen kalem sayısı — yalnız kimliksiz (özellik öncesi) <c>TemplateEdited</c> satırları;
    /// çağıran kullanıcıyı uyarabilsin diye.
    /// </summary>
    public virtual async Task<int> ApplyToVariantAsync(Guid companyId, Guid variantId, RecipeTemplate template)
    {
        // Şablonun persist edilmiş satır kimlikleri — eşleme anahtarı. Persist edilmemiş satır (Id boş)
        // kimlik taşıyamaz; onun ürünü daima "yeni kur" yolundan geçer.
        var templateLineIds = template.Lines
            .Where(l => l.Id != Guid.Empty)
            .Select(l => l.Id)
            .ToHashSet();

        var existing = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == variantId)
                .OrderBy(l => l.LineOrder));

        // (1) Eşleme — ÖNCE SourceTemplateLineId; yoksa (özellik öncesi kimliksiz satır) eski davranış:
        //     · Template + kimliği şablonda hâlâ VAR → satır YAŞAR, (3)'te YERİNDE tazelenir (satır Id'si sabit
        //       kalır; kullanıcının SelectedLines türev referansları kopmaz).
        //     · Template + kimliksiz YA DA kimliği şablondan SİLİNMİŞ → düşer (eski davranış: idempotentlik ve
        //       "şablondan silinen satır üründen de düşer" buradan gelir).
        //     · Diğer origin'ler (Manual/Substitution/TemplateEdited) → korunur; değerleri EZİLMEZ.
        //     Aynı kimliğe İKİNCİ dokunulmamış şablon satırı bir çoğalma kalıntısıdır → düşer (TryAdd ilkini tutar).
        var refreshableBySource = new Dictionary<Guid, ProductVariantRecipeLine>();
        var preserved = new List<ProductVariantRecipeLine>();
        foreach (var line in existing)
        {
            if (line.Origin != RecipeLineOrigin.Template)
            {
                preserved.Add(line);
                continue;
            }

            if (line.SourceTemplateLineId is { } sourceId
                && templateLineIds.Contains(sourceId)
                && refreshableBySource.TryAdd(sourceId, line))
            {
                continue;
            }

            await _recipeLineRepository.DeleteAsync(line, autoSave: true);
        }

        // (2) Korunanları 0..k-1 yeniden numarala — şablon bölümü bunların ARDINA gelecek.
        var order = 0;
        foreach (var line in preserved)
        {
            if (line.LineOrder != order)
            {
                line.SetOrder(order);
                await _recipeLineRepository.UpdateAsync(line, autoSave: true);
            }

            order++;
        }

        // Kullanıcının SAHİPLENDİĞİ (TemplateEdited) satırların şablon kimlikleri: bunların şablon karşılığı
        // YENİDEN KURULMAZ — kullanıcının sürümü şablon satırının yerine geçer (satır çoğalması düzeltmesi;
        // değeri de yukarıda korunmuştu — sahiplik kuralı).
        var ownedSourceIds = preserved
            .Where(l => l.Origin == RecipeLineOrigin.TemplateEdited
                && l.SourceTemplateLineId is { } sourceId
                && templateLineIds.Contains(sourceId))
            .Select(l => l.SourceTemplateLineId!.Value)
            .ToHashSet();

        // (3) Şablon satırları kendi sıralarıyla: sahiplenilmiş → atla; eşleşen → yerinde tazele; kalan → yeni kur.
        foreach (var templateLine in template.Lines.OrderBy(l => l.LineOrder))
        {
            if (templateLine.Id != Guid.Empty && ownedSourceIds.Contains(templateLine.Id))
            {
                continue;
            }

            if (refreshableBySource.Remove(templateLine.Id, out var match))
            {
                if (match.ComponentType == templateLine.ComponentType)
                {
                    match.SetOrder(order++);
                    ApplyTemplateFields(match, templateLine);
                    await _recipeLineRepository.UpdateAsync(match, autoSave: true);
                    continue;
                }

                // Teorik savunma: şablon tarafında tür değişimi YENİ satır kimliği üretir
                // (RecipeTemplateLineMerger düşür + yeniden kur), yani kimliği eşleşen çiftin türü normalde hep
                // aynıdır. Yine de ayrışırsa melez satır bırakmak yerine temiz kurulur (ComponentType reçete
                // satırında set-once — yerinde tür değiştirilemez).
                await _recipeLineRepository.DeleteAsync(match, autoSave: true);
            }

            var line = BuildRecipeLine(companyId, variantId, templateLine, order++);
            await _recipeLineRepository.InsertAsync(line, autoSave: true);
        }

        // (4) Eşlenebilir olup şablon turunda tüketilmeyenler: kimliği sahiplenilmiş kümede olduğu için atlanan
        // dokunulmamış kopyalar (özellik öncesi bir çoğalmanın kalıntısı) — kullanıcının sürümü zaten yaşıyor,
        // kopya düşer.
        foreach (var stale in refreshableBySource.Values)
        {
            await _recipeLineRepository.DeleteAsync(stale, autoSave: true);
        }

        // Dönüş: HÂLÂ iki kez görünebilen kalem sayısı = yalnız KİMLİKSİZ (özellik öncesi) düzenlenmiş satırlar.
        // Kimliği eşleşen düzenlenmiş satırın şablon karşılığı artık kurulmadığı için o kalem TEK görünür ve
        // uyarıya girmez — çoğalmayan satırı saymak yanlış alarm olurdu (UI bu sayıyı "kalem iki kez görünüyor"
        // uyarısı olarak gösterir). Kimliği şablondan silinmiş düzenlenmiş satırın karşılığı da kurulmaz.
        return preserved.Count(
            l => l.Origin == RecipeLineOrigin.TemplateEdited && l.SourceTemplateLineId is null);
    }

    /// <summary>Şablon satırını YENİ reçete satırına çevirir — alan setleri AYNI olduğundan düz kopya.</summary>
    private static ProductVariantRecipeLine BuildRecipeLine(
        Guid companyId,
        Guid variantId,
        RecipeTemplateLine source,
        int lineOrder)
    {
        var line = new ProductVariantRecipeLine(companyId, variantId, source.ComponentType, lineOrder);
        ApplyTemplateFields(line, source);
        line.SetOrigin(RecipeLineOrigin.Template);
        return line;
    }

    /// <summary>Şablon satırının alanlarını reçete satırına uygular — yeni kurulumda da yerinde tazelemede de
    /// AYNI kopya (iki yol ayrışsaydı yeni bir alan birinden sessizce düşerdi). Soy kimliği de burada damgalanır:
    /// eşlemenin anahtarı, alan kopyasının ayrılmaz parçasıdır (persist edilmemiş şablon satırında setter null'a
    /// normalize eder → eski davranış).</summary>
    private static void ApplyTemplateFields(ProductVariantRecipeLine line, RecipeTemplateLine source)
    {
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
        line.SetTemplateSource(source.Id);
    }
}
