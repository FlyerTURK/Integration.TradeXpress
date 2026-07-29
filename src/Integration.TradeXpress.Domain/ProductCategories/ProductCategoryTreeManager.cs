using Volo.Abp.Domain.Services;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori AĞACININ bütünlüğünü koruyan ve KALITIMI çözen domain servisi. Entity tek başına yalnız "kendi
/// ebeveynim olamam" diyebilir; döngü tespiti ve kalıtım ATA ZİNCİRİNİ görmeyi, yani repository'yi gerektirir.
///
/// <para><b>Derinlik tavanı YOK</b> (2026-07-27 Hakan kararı): hiyerarşi serbest. Sonsuz yürüyüşü tavan değil
/// döngü tespiti keser — ziyaret edilen düğümler işaretlenir, düğüm sayısı sonlu olduğundan yürüyüş kesin biter.</para>
/// </summary>
public class ProductCategoryTreeManager : DomainService
{
    private readonly IRepository<ProductCategory, Guid> _repository;

    public ProductCategoryTreeManager(IRepository<ProductCategory, Guid> repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Verilen ebeveyn ataması geçerli mi — kaydetmeden ÖNCE çağrılır.
    /// Sırayla: ebeveyn var mı · AYNI ŞİRKETTE mi · döngü kuruyor mu.
    /// </summary>
    /// <param name="categoryId">Taşınan kategori; YENİ kayıtta <see cref="Guid.Empty"/> (henüz kimliği yok).</param>
    public virtual async Task EnsureParentIsValidAsync(Guid companyId, Guid categoryId, Guid? parentId)
    {
        if (parentId is not { } parent)
        {
            return;   // kök (ana kategori) — doğrulanacak zincir yok
        }

        if (categoryId != Guid.Empty && parent == categoryId)
        {
            throw new BusinessException("TradeXpress:ProductCategory:CannotBeOwnParent");
        }

        var chain = await LoadAncestorChainAsync(companyId, parent);

        // Ata zincirinde kendisi geçiyorsa taşıma bir DÖNGÜ kurar (A'yı kendi torununun altına almak gibi).
        if (categoryId != Guid.Empty && chain.Any(c => c.Id == categoryId))
        {
            throw new BusinessException("TradeXpress:ProductCategory:CircularParent");
        }
    }

    /// <summary>Kökten verilen kategoriye kadar olan zincir (kök ilk, kategori son) — "yol" gösterimi ve
    /// nitelik kalıtımı bunu kullanır.</summary>
    public virtual async Task<List<ProductCategory>> GetPathAsync(Guid companyId, Guid categoryId)
    {
        var chain = await LoadAncestorChainAsync(companyId, categoryId);
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Kategorinin ETKİN niteliklerini çözer: kökten aşağı inerek atalarınkiler devralınır, kategorinin kendi
    /// nitelikleri en sonda uygulanır (2026-07-27 Hakan kararı: "üst kategorinin attribute ve value'larını alt
    /// kategoriler inherit alsın").
    ///
    /// <para><b>Birleştirme kuralı — EKLEMELİ (union), ezme değil:</b> aynı adlı nitelik alt seviyede yeniden
    /// tanımlanırsa değerleri üsttekilerin ÜSTÜNE eklenir ("Ayar: 14K,18K" üstte + "22K" altta → 14K,18K,22K).
    /// Ezme seçilseydi alt kategoriye tek bir değer eklemek için üsttekilerin tamamını tekrar yazmak gerekirdi;
    /// birleştirme hem daha az şaşırtır hem de üstteki değere asılı kanal eşleştirmesini koparmaz. Nitelik CİNSİ
    /// (<see cref="ProductCategoryAttributeKind"/>) ise en dar tanım kazanır — alt seviye ne dediyse o.</para>
    ///
    /// <para>Ad karşılaştırması BÜYÜK/küçük harf duyarsızdır ("Ayar" ile "AYAR" aynı niteliktir); değer metni
    /// case KORUR ("14K" bozulmaz) ama tekilleştirme yine duyarsızdır.</para>
    /// </summary>
    public virtual async Task<List<ProductCategoryEffectiveAttribute>> GetEffectiveAttributesAsync(
        Guid companyId,
        Guid categoryId)
    {
        var path = await GetPathAsync(companyId, categoryId);
        return MergeAttributes(path, categoryId);
    }

    /// <summary>
    /// Kökten yaprağa sıralı zincirden etkin nitelik listesini üretir. Repository'ye dokunmaz — zincir elde
    /// olduğunda (ör. tek sorguda yüklenmiş ağaç) tekrar sorgu atmadan çağrılabilsin diye ayrı ve <c>static</c>.
    /// </summary>
    public static List<ProductCategoryEffectiveAttribute> MergeAttributes(
        IReadOnlyList<ProductCategory> pathFromRoot,
        Guid ownCategoryId)
    {
        var merged = new List<ProductCategoryEffectiveAttribute>();
        var byName = new Dictionary<string, ProductCategoryEffectiveAttribute>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in pathFromRoot)
        {
            var isOwn = category.Id == ownCategoryId;

            foreach (var attribute in category.Attributes.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name))
            {
                if (!byName.TryGetValue(attribute.Name, out var effective))
                {
                    effective = new ProductCategoryEffectiveAttribute(attribute, category, isInherited: !isOwn);
                    byName[attribute.Name] = effective;
                    merged.Add(effective);
                }
                else
                {
                    // Aynı ad daha aşağıda yeniden tanımlanmış: cins ve sıra en DAR tanımdan, değerler EKLENİR.
                    effective.Redefine(attribute, category, isInherited: !isOwn);
                }

                foreach (var value in attribute.Values.OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value))
                {
                    effective.AddValue(value, category, isInherited: !isOwn);
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Bir düğümün KENDİSİ + tüm torunlarının kimliklerini toplar (aşağı doğru yürüyüş).
    ///
    /// <para>Kullanımı: "üst kategori" seçeneklerini üretirken bu küme DIŞLANIR — bir kategori kendi alt
    /// ağacındaki bir düğümü üst seçemez, bu döngü kurardı. Kullanıcıya seçtirip sonra hata vermek yerine
    /// seçeneği hiç göstermemek doğrusudur.</para>
    ///
    /// <para>Repository'ye dokunmaz ve <c>static</c>'tir: çağıran ağacı TEK sorguda okuyup buraya verir
    /// (düğüm başına sorgu N+1 üretirdi) ve kural DB'siz sınanabilir. Ziyaret işareti, bozuk veride oluşmuş
    /// bir döngüde yürüyüşün sonsuza gitmesini engeller.</para>
    /// </summary>
    public static HashSet<Guid> CollectSubtreeIds(IEnumerable<(Guid Id, Guid? ParentId)> nodes, Guid rootId)
    {
        var subtree = new HashSet<Guid> { rootId };

        // Çocuk araması için tek geçişte indeks: aksi hâlde her seviye tüm listeyi tarardı.
        var childrenByParent = nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                // Add false dönerse bu düğüm zaten görüldü (bozuk veride döngü) → tekrar kuyruğa alma.
                if (subtree.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return subtree;
    }

    /// <summary>
    /// Kategoriden köke doğru zincir (kategori ilk, kök son). Ziyaret edilen düğümleri işaretler: bozuk veride
    /// (elle düzenlenmiş DB) döngü oluşsa bile yürüyüş sonsuza gitmez, olduğu yerde kesilir — guard'ın kendisi
    /// burada çökerse kullanıcı hiçbir kategoriyi açamazdı.
    /// </summary>
    private async Task<List<ProductCategory>> LoadAncestorChainAsync(Guid companyId, Guid startId)
    {
        var chain = new List<ProductCategory>();
        var visited = new HashSet<Guid>();
        var current = startId;

        while (current != Guid.Empty && visited.Add(current))
        {
            var node = await _repository.FindAsync(x => x.Id == current && x.CompanyId == companyId);
            if (node is null)
            {
                throw new BusinessException("TradeXpress:ProductCategory:ParentNotFound");
            }

            chain.Add(node);
            current = node.ParentId ?? Guid.Empty;
        }

        return chain;
    }
}
