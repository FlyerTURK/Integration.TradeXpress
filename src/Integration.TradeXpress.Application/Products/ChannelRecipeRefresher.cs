using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÇEKİRDEK REÇETE DEĞİŞİNCE DEVRALINMIŞ KANAL KOPYALARINI TAZELER — <see cref="ChannelRecipeInheritance"/>
/// karar çekirdeğinin tek üretim tüketicisi (2026-08-11 Hakan tasarımının yayılım ayağı; 2026-08-14'te kuruldu).
///
/// <para><b>Neden gerekli:</b> kanal reçetesi "klon-sonra-ayrış" yaşar — kullanıcı kanal formunu kaydedene dek
/// canlı klondur ve çekirdeği KENDİLİĞİNDEN izler; ama bir kez persist olunca DONAR. Kullanıcı bileşime hiç
/// dokunmadan kaydetmişse (persist edilmiş ama devralınmış kopya), çekirdek değişikliği o kanala bir daha
/// ulaşamazdı: push fiyatlaması yalnız persist edilmiş satırları okuduğundan kanal ESKİ bileşimle fiyatlanmaya
/// devam ederdi — hatasız, logsuz, yalnız yanlış fiyat.</para>
///
/// <para><b>Devir kararı KAYIT-ÖNCESİ çekirdeğe karşı verilir:</b> kalıcı bayrak yok (veri kendisi konuşur),
/// dolayısıyla "devralınmış mı" sorusu ancak kanal kopyasının ESKİ çekirdekle karşılaştırılmasıyla cevaplanır —
/// yeni çekirdekle karşılaştırmak, az önce değişmiş çekirdeği izleyen her kopyayı "override" sanıp sonsuza dek
/// dondururdu. Çağıran (<see cref="ProductRecipeLineWriter"/>) bu yüzden kayıt öncesi satırları verir.</para>
///
/// <para><b>Yan maliyetler taşınır, değiştirilmez:</b> karşılaştırma da tazeleme de yalnız
/// <c>SideCostKind == null</c> satırlar üzerinde çalışır (kanal gideri kanalın malıdır — sınıf kararı
/// <see cref="ChannelRecipeInheritance"/>'ta). Yan-maliyet satırları hep <c>AllAbove</c> türevidir
/// (<c>SideCostRecipeComposer.BuildLine</c>), satır-id referansı taşımaz — altlarındaki satırların değişmesi
/// referans kırmaz; yalnız sıra numaraları yeni bileşimin arkasına kaydırılır.</para>
///
/// <para><b>Etsy bilinçli kapsam dışı</b> (2026-08-08 Hakan kararı: yeni çapraz kanal kuralları Etsy'ye
/// uygulanmaz; kanal sıraya alındığında biriken kurallar topluca uygulanacak).</para>
/// </summary>
public class ChannelRecipeRefresher : ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11HeaderRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11LineRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _trendyolHeaderRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _trendyolLineRepository;

    public ChannelRecipeRefresher(
        IRepository<SalesChannelTrN11ProductStockItem, Guid> n11HeaderRepository,
        IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> n11LineRepository,
        IRepository<SalesChannelTrTrendyolProductStockItem, Guid> trendyolHeaderRepository,
        IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> trendyolLineRepository)
    {
        _n11HeaderRepository = n11HeaderRepository;
        _n11LineRepository = n11LineRepository;
        _trendyolHeaderRepository = trendyolHeaderRepository;
        _trendyolLineRepository = trendyolLineRepository;
    }

    /// <summary>Varyantın çekirdek reçetesi <paramref name="oldCoreSignatures"/> fotoğrafından
    /// <paramref name="newCoreLines"/>'a değişti — devralınmış (persist edilmiş ama bileşimi eski çekirdekle
    /// örtüşen) kanal kopyalarını yeni çekirdekle değiştirir. Override edilmiş kopyalara ve hiç persist edilmemiş
    /// (canlı klon) başlıklara DOKUNMAZ.
    ///
    /// <para><b>Eski taraf İMZA FOTOĞRAFIDIR, entity listesi DEĞİL:</b> tek UoW'da eski entity referansları
    /// yerinde güncellemeyle mutasyona uğrar ve "eski" liste yeni değerleri gösterir — kıyas hep "aynı" der,
    /// tazeleme hiç çalışmaz (bu ilk sürümde yaşandı; ağı <c>ChannelRecipeRefreshTests</c>'in tek-UoW vakası).</para></summary>
    public virtual async Task RefreshAsync(
        Guid variantId,
        IReadOnlyList<RecipeCommoditySignature> oldCoreSignatures,
        IReadOnlyList<ProductVariantRecipeLine> newCoreLines)
    {
        // Bileşim imzası değişmediyse (sıra/açıklama düzenlemesi gibi) tazelenecek bir şey yok — kanal
        // sorgularına hiç inilmez.
        if (ChannelRecipeInheritance.IsInherited(oldCoreSignatures, newCoreLines))
        {
            return;
        }

        await RefreshN11Async(variantId, oldCoreSignatures, newCoreLines);
        await RefreshTrendyolAsync(variantId, oldCoreSignatures, newCoreLines);
    }

    // N11 ve Trendyol gövdeleri bilinçli ikiz — kanal reçete entity'leri ortak taban paylaşmaz (CLAUDE.md §6
    // ChannelPushGuard gerekçesi) ve iki tip arasında generic köprü kurmak ctor/anchor farklarını
    // delegelerle taşıyan daha kırılgan bir yapı üretirdi.

    private async Task RefreshN11Async(
        Guid variantId,
        IReadOnlyList<RecipeCommoditySignature> oldCoreSignatures,
        IReadOnlyList<ProductVariantRecipeLine> newCoreLines)
    {
        var headers = await _n11HeaderRepository.GetListAsync(h => h.ProductVariantId == variantId);
        foreach (var header in headers)
        {
            var channelLines = await _n11LineRepository.GetListAsync(l => l.StockItemId == header.Id);
            if (!ShouldRefresh(oldCoreSignatures, channelLines))
            {
                continue;
            }

            var sideCosts = channelLines
                .Where(l => l.SideCostKind is not null)
                .OrderBy(l => l.LineOrder)
                .ToList();
            foreach (var stale in channelLines.Where(l => l.SideCostKind is null))
            {
                await _n11LineRepository.DeleteAsync(stale, autoSave: true);
            }

            var order = 0;
            var cloneByCoreId = new Dictionary<Guid, SalesChannelTrN11ProductStockItemRecipeLine>();
            var clones = new List<(ProductVariantRecipeLine Core, SalesChannelTrN11ProductStockItemRecipeLine Clone)>();
            foreach (var core in InheritableCoreLines(newCoreLines))
            {
                var clone = new SalesChannelTrN11ProductStockItemRecipeLine(
                    header.CompanyId, header.SalesChannelTrN11ProductId, header.Id, core.ComponentType, order++);
                ApplyCoreFields(core, clone.SetCatalogCommodity, clone.SetService, clone.SetDescription);
                await _n11LineRepository.InsertAsync(clone, autoSave: true);
                cloneByCoreId[core.Id] = clone;
                clones.Add((core, clone));
            }

            foreach (var (core, clone) in clones)
            {
                if (RemapDerivedSources(core, id => cloneByCoreId.TryGetValue(id, out var c) ? c.Id : null) is { } csv)
                {
                    clone.SetDerivedSources(csv);
                    await _n11LineRepository.UpdateAsync(clone, autoSave: true);
                }
            }

            foreach (var sideCost in sideCosts)
            {
                sideCost.SetOrder(order++);
                await _n11LineRepository.UpdateAsync(sideCost, autoSave: true);
            }
        }
    }

    private async Task RefreshTrendyolAsync(
        Guid variantId,
        IReadOnlyList<RecipeCommoditySignature> oldCoreSignatures,
        IReadOnlyList<ProductVariantRecipeLine> newCoreLines)
    {
        var headers = await _trendyolHeaderRepository.GetListAsync(h => h.ProductVariantId == variantId);
        foreach (var header in headers)
        {
            var channelLines = await _trendyolLineRepository.GetListAsync(l => l.StockItemId == header.Id);
            if (!ShouldRefresh(oldCoreSignatures, channelLines))
            {
                continue;
            }

            var sideCosts = channelLines
                .Where(l => l.SideCostKind is not null)
                .OrderBy(l => l.LineOrder)
                .ToList();
            foreach (var stale in channelLines.Where(l => l.SideCostKind is null))
            {
                await _trendyolLineRepository.DeleteAsync(stale, autoSave: true);
            }

            var order = 0;
            var cloneByCoreId = new Dictionary<Guid, SalesChannelTrTrendyolProductStockItemRecipeLine>();
            var clones = new List<(ProductVariantRecipeLine Core, SalesChannelTrTrendyolProductStockItemRecipeLine Clone)>();
            foreach (var core in InheritableCoreLines(newCoreLines))
            {
                var clone = new SalesChannelTrTrendyolProductStockItemRecipeLine(
                    header.CompanyId, header.SalesChannelTrTrendyolProductId, header.Id, core.ComponentType, order++);
                ApplyCoreFields(core, clone.SetCatalogCommodity, clone.SetService, clone.SetDescription);
                await _trendyolLineRepository.InsertAsync(clone, autoSave: true);
                cloneByCoreId[core.Id] = clone;
                clones.Add((core, clone));
            }

            foreach (var (core, clone) in clones)
            {
                if (RemapDerivedSources(core, id => cloneByCoreId.TryGetValue(id, out var c) ? c.Id : null) is { } csv)
                {
                    clone.SetDerivedSources(csv);
                    await _trendyolLineRepository.UpdateAsync(clone, autoSave: true);
                }
            }

            foreach (var sideCost in sideCosts)
            {
                sideCost.SetOrder(order++);
                await _trendyolLineRepository.UpdateAsync(sideCost, autoSave: true);
            }
        }
    }

    /// <summary>Başlık tazelenmeli mi: persist edilmiş satırı olmayan başlık canlı klondur (kendiliğinden
    /// devralır — dokunma); satırı olup bileşimi ESKİ çekirdekle örtüşmeyen başlık override'dır (dokunma).</summary>
    private static bool ShouldRefresh(
        IReadOnlyList<RecipeCommoditySignature> oldCoreSignatures, IReadOnlyList<IRecipeCommodityLine> channelLines)
    {
        if (channelLines.Count == 0)
        {
            return false;
        }

        return ChannelRecipeInheritance.IsInherited(oldCoreSignatures, channelLines);
    }

    /// <summary>Yeni çekirdeğin devralınabilir satırları, LineOrder sırasıyla.</summary>
    private static IEnumerable<ProductVariantRecipeLine> InheritableCoreLines(IReadOnlyList<ProductVariantRecipeLine> newCoreLines)
    {
        return ChannelRecipeInheritance.InheritableLines(newCoreLines)
            .Cast<ProductVariantRecipeLine>()
            .OrderBy(l => l.LineOrder);
    }

    /// <summary>Çekirdek satırın alanlarını kanal klonuna uygular — <c>ProductRecipeLineWriter.ApplyFields</c>'in
    /// entity-kaynaklı ikizi (aynı iki dal: katalog-emtia / hizmet).</summary>
    private static void ApplyCoreFields(
        ProductVariantRecipeLine core,
        Action<ProcessType, Guid?, Guid?, decimal, decimal, decimal, Guid?, ProcessPaymentType, decimal, Guid?> setCatalogCommodity,
        Action<Guid?, RecipeDerivedBaseMode, RecipeDerivedOperation, decimal, Guid?> setService,
        Action<string?> setDescription)
    {
        if (core.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            setCatalogCommodity(
                core.CommodityProcessType.GetValueOrDefault(),
                core.CommodityId,
                core.CommodityVariantId,
                core.Quantity,
                core.Amount,
                core.Factor,
                core.ValuationUnitId,
                core.PaymentType,
                core.PayFactor,
                core.PayUnitId);
        }
        else
        {
            setService(
                core.CommodityId,
                core.DerivedBaseMode.GetValueOrDefault(RecipeDerivedBaseMode.AllAbove),
                core.DerivedOperation.GetValueOrDefault(RecipeDerivedOperation.Percent),
                core.DerivedOperand,
                core.PayUnitId);
        }

        setDescription(core.Description);
    }

    /// <summary>Çekirdek SelectedLines türev satırının kaynak Id CSV'sini klon Id'lerine çevirir; satır türev
    /// değilse ya da hiçbir kaynak klonlanmadıysa (kaynak yan-maliyet çekirdek satırıydı — klon kapsamı dışı)
    /// <c>null</c> döner ve klon kaynaksız bırakılır (SetDerivedSources fail-fast'ine girilmez).</summary>
    private static string? RemapDerivedSources(ProductVariantRecipeLine core, Func<Guid, Guid?> mapCoreLineId)
    {
        if (core.ComponentType != RecipeComponentType.Service
            || core.DerivedBaseMode != RecipeDerivedBaseMode.SelectedLines
            || string.IsNullOrWhiteSpace(core.DerivedSourceLineIds))
        {
            return null;
        }

        var mapped = core.DerivedSourceLineIds
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(raw => Guid.TryParse(raw, out var id) ? mapCoreLineId(id) : null)
            .Where(id => id is not null)
            .Select(id => id!.Value.ToString())
            .ToList();

        return mapped.Count > 0 ? string.Join('|', mapped) : null;
    }
}
