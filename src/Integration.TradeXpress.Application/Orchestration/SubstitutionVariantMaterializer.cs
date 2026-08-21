using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// MUADİL VARYANT MATERYALİZASYONU (ADR-PRODUCT-ORCHESTRATION; 2026-07-25 Hakan kararları:
/// "Uygula butonu olmasın, otomatik o anda oluşturulsun" + Tek/Çoklu alt-modu).
/// O anki stoğa göre kombinasyonları hesaplar (<see cref="ISubstitutionCalculationAppService"/>) ve:
/// <list type="bullet">
///   <item><b>Single:</b> yalnız Rank 1 kombinasyon ANA varyantın reçetesi olur (statükonun sunucu-taraf,
///   otomatik hâli — eski istemci "Uygula" yolunun yerini alır).</item>
///   <item><b>Multi:</b> Rank sırasıyla en fazla <see cref="ProductConsts.SubstitutionMaterializedVariantMax"/>
///   başarılı kombinasyon AYRI varyant olur: Rank 1 = ana, diğerleri müşteriye seçenek.</item>
/// </list>
/// <para><b>Kimlik kararlılığı:</b> varyant, kombinasyon BİLEŞİMİNDEN türetilen deterministik kodla eşlenir —
/// yeniden üretimde aynı bileşim aynı varyant kaydında kalır (Id sabit → kanal StockItem bağları kopmaz);
/// bileşimi stoktan düşenler silinir, yenileri eklenir (EntityVariantSynchronizer'ın key-diff deseni).</para>
/// <para><b>Synchronizer'a dokunulmaz:</b> materyalize varyantlar nitelik-değer bağı TAŞIMAZ (link-less) →
/// synchronizer'ın 0-nitelik dalı (RemoveLinkedVariantsAsync yalnız BAĞLI varyantları siler) bunları YAŞATIR.</para>
/// <para><b>Kur yoksa (RatesMissing):</b> ürün kaydı DÜŞÜRÜLMEZ — materyalizasyon atlanır ve UYARI loglanır
/// (varyantlar bayat kalır; sonraki stok tetiği/kayıt tazeler). Sessiz yutma değil: yalnız bu kod, logla.</para>
/// </summary>
public class SubstitutionVariantMaterializer : ITransientDependency
{
    private readonly ISubstitutionCalculationAppService _calculation;
    private readonly SubstitutionChannelPlanProvider _planContextProvider;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;   // yalnız OKUMA — gösterim birimi
    private readonly IRepository<RecipeTemplate, Guid> _recipeTemplateRepository;   // yalnız OKUMA
    private readonly RecipeTemplateApplier _recipeTemplateApplier;
    private readonly EntityVariantManager _variantManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ILogger<SubstitutionVariantMaterializer> _logger;

    private const string ProductEntityName = "Product";
    private const string SubstitutionErrorPrefix = "TradeXpress:Substitution:";

    public SubstitutionVariantMaterializer(
        ISubstitutionCalculationAppService calculation,
        SubstitutionChannelPlanProvider planContextProvider,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<SubstitutionGroup, Guid> groupRepository,
        IRepository<RecipeTemplate, Guid> recipeTemplateRepository,
        RecipeTemplateApplier recipeTemplateApplier,
        EntityVariantManager variantManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ILogger<SubstitutionVariantMaterializer> logger)
    {
        _calculation = calculation;
        _planContextProvider = planContextProvider;
        _variantRepository = variantRepository;
        _recipeLineRepository = recipeLineRepository;
        _groupRepository = groupRepository;
        _recipeTemplateRepository = recipeTemplateRepository;
        _recipeTemplateApplier = recipeTemplateApplier;
        _variantManager = variantManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _logger = logger;
    }

    /// <summary>Ürünün muadil varyantlarını O ANKİ stoğa göre yeniden üretir. Substitution modunda değilse no-op.
    /// Çağıran doğru company bağlamını kurmakla yükümlü (stok okuması ICurrentCompany ister).</summary>
    public virtual async Task MaterializeAsync(Product product)
    {
        if (product.VariantMode != ProductVariantMode.Substitution
            || product.SubstitutionGroupId is not { } groupId
            || product.SubstitutionTargetQuantity is not { } target)
        {
            return;
        }

        SubstitutionCalculationResultDto result;
        try
        {
            result = await _calculation.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId    = groupId,
                TargetQuantity         = target,
                ToleranceTypeOverride  = product.SubstitutionToleranceType,
                ToleranceValueOverride = product.SubstitutionToleranceValue,
                OverrideVariantIds     = product.SubstitutionOverrideVariantIds.ToList(),
            });
        }
        catch (BusinessException ex) when (ex.Code?.StartsWith(SubstitutionErrorPrefix, StringComparison.Ordinal) == true)
        {
            // Üretim EN-İYİ-ÇABA: hesap ŞU AN koşamıyor diye (kur eksik / grup kalemi yok / stok yok) ürün
            // KAYDI DÜŞÜRÜLMEZ — konfigürasyonun kendi doğrulaması SetSubstitutionConfig'te zaten yapıldı.
            // Varyantlar bayat kalır; sonraki kayıt/stok tetiği tazeler. Yalnız muadil-hesap kodları yakalanır
            // (genel BusinessException DEĞİL — beklenmeyen hata yükselmeye devam eder; §2 sahte-baypas yasak).
            _logger.LogWarning(
                "Muadil materyalizasyonu atlandı ({Code}): Product={ProductCode}. Varyantlar bayat kaldı; "
                + "sonraki kayıt/stok tetiği tazeler.", ex.Code, product.Code);
            return;
        }

        try
        {
            var selected = SubstitutionVariantSelection.Select(result.Trials, product.SubstitutionVariantMode);
            var context = await LoadContextAsync(selected);

            if (product.SubstitutionVariantMode == SubstitutionVariantMode.Multi)
            {
                await MaterializeMultiAsync(product, selected, context);
            }
            else
            {
                await MaterializeSingleAsync(product, selected, context);
            }
        }
        catch (BusinessException ex) when (ex.Code?.StartsWith(SubstitutionErrorPrefix, StringComparison.Ordinal) == true)
        {
            // Guard yalniz CalculateAsync'i sarmiyordu (inceleme bulgusu #12): baglam yukleme / recete kurma da
            // ayni aileden firlatabilir (MetalNotFound) — urun kaydi yine dusurulmez, loglanir.
            _logger.LogWarning(
                "Muadil materyalizasyonu atlandi ({Code}): Product={ProductCode}.", ex.Code, product.Code);
        }
    }


    private async Task<SubstitutionChannelPlanContext> LoadContextAsync(List<SubstitutionTrialDto> trials)
    {
        var lines = trials.SelectMany(t => t.Lines).ToList();
        return await _planContextProvider.LoadPlanContextAsync(
            lines.Select(l => l.MetalId).Distinct().ToList(),
            lines.Where(l => l.VariantId != null).Select(l => l.VariantId!.Value).Distinct().ToList());
    }

    // ── SINGLE: tek ana varyant kalır; Rank 1 kombinasyon reçetesi olur (yoksa reçete BOŞALTILMAZ —
    //    stok yokken kayıtlı reçeteyi silmek üretim bilgisini yok ederdi; stok=0 zaten kanala 0 gönderir). ──
    private async Task MaterializeSingleAsync(
        Product product, List<SubstitutionTrialDto> selected, SubstitutionChannelPlanContext context)
    {
        var main = await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);

        if (selected.Count == 0)
        {
            main.SetStock(0);
            await _variantRepository.UpdateAsync(main, autoSave: true);
            return;
        }

        var best = selected[0];
        await ReplaceRecipeLinesIfChangedAsync(product.CompanyId, main.Id, best, context);
        main.SetStock(best.PackageCount);
        await _variantRepository.UpdateAsync(main, autoSave: true);

        // Coklu->Tek gecisi (inceleme bulgusu #6): eski materyalize kombinasyon varyantlari Tek modda artik
        // satilmamali — ana disindaki canli varyantlar soft-silinir (Multi'ye donuste ayni kodla dirilirler).
        var others = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id && v.Id != main.Id));
        foreach (var other in others)
        {
            // YALNIZ muadil satirlari silinir: varyant ayni kodla DIRILEBILIR (asagidaki Multi dali dirilti
            // yapiyor) ve o an kullanicinin elle ekledigi / sablondan gelen satirlar geri gelmeliydi. Origin
            // filtresi olmadan bunlar kalici olarak olur ve maliyet sessizce eksik hesaplanirdi.
            await _recipeLineRepository.DeleteAsync(
                l => l.ProductVariantId == other.Id && l.Origin == RecipeLineOrigin.Substitution, autoSave: true);
            await _variantRepository.DeleteAsync(other, autoSave: true);
        }
    }

    // ── MULTI: kombinasyon-kod diff'i — ayni bilesim ayni varyantta kalir (Id sabit), dusen pasiflesir/silinir. ──
    private async Task MaterializeMultiAsync(
        Product product, List<SubstitutionTrialDto> selected, SubstitutionChannelPlanContext context)
    {
        // SOFT-DELETE FARKINDALI okuma — amac KIMLIK KORUMA (inceleme bulgusu #16): ayni kod soft-silinmisse
        // DIRILTILIR (Id korunur, kanal SKU baglari geri gelir); yeni satir acilmaz.
        //
        // ⚠ Bu baypas KALICIDIR ve indeksten BAGIMSIZDIR. Eski gerekcesi "(TenantId, EntityName, EntityId, Code)
        // indeksi IsDeleted filtresi tasimaz, yeniden INSERT unique ihlali verir" idi; o gerekce 2026-08-07'de
        // GECERSIZ kaldi (indekse "IsDeleted = 0" eklendi) ama davranis DEGISMEZ: filtreyi acip yeni satir acmak
        // teknik olarak calisirdi ama varyant Id'sini degistirir ve kanal SKU baglarini KOPARIRDI.
        List<EntityVariant> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _asyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
        }

        var existingByCode = existing.ToDictionary(v => v.Code, StringComparer.OrdinalIgnoreCase);

        // HIC basarili kombinasyon yoksa (stok tukendi) SILME YOK (inceleme bulgusu #3): kimlikler + receteler
        // KORUNUR, yalniz stoklar 0'a cekilir — stok donunce AYNI varyantlar (ayni Id) yeniden satisa acilir.
        // Eski davranis tum varyantlari silip kanal SKU baglarini kopariyordu.
        if (selected.Count == 0)
        {
            foreach (var variant in existing.Where(v => !v.IsDeleted && v.StockQuantity != 0))
            {
                variant.SetStock(0);
                await _variantRepository.UpdateAsync(variant, autoSave: true);
            }

            await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId, product.Code, product.Name);
            return;
        }

        var targetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EntityVariant? mainVariant = null;

        // Varyant adı koda YALNIZ ayırt ediyorsa girer: bir madenin kapsamında tek varyant varsa
        // ".ANAVARYANT" her kombinasyonda tekrarlanan, hiçbir şeyi ayırmayan gürültüdür
        // ("G1.0GR995.ANAVARYANTX3+G2.5GR995.ANAVARYANTX2" → "G1.0GR995X3+G2.5GR995X2").
        // Çoklu varyantta ise ŞART: aynı madenin iki varyantı farklı işçilik/maliyet taşır ve kodun
        // deterministik kimlik olması için ayrışmaları gerekir (2026-07-27 Hakan kararı).
        var multiVariantMetalIds = SubstitutionCombinationCodeBuilder.MultiVariantMetalIds(selected);

        // Gösterim birimi GRUPTAN gelir ("gr"/"kg"/"lt"): grubun tüm kalemleri aynı birimden ölçülür.
        // Grup okunamazsa varsayılana düşülür — birimsiz ad, sayının neyi ölçtüğünü belirsiz bırakırdı.
        var quantityUnit = await ResolveQuantityUnitAsync(product.SubstitutionGroupId);

        foreach (var trial in selected)
        {
            var code = SubstitutionCombinationCodeBuilder.Build(trial, multiVariantMetalIds);
            targetCodes.Add(code);

            if (!existingByCode.TryGetValue(code, out var variant))
            {
                variant = new EntityVariant(
                    companyId: product.CompanyId,
                    entityName: ProductEntityName,
                    entityId: product.Id,
                    code: code,
                    name: BuildCombinationName(product.Name, trial, quantityUnit),
                    isMain: false,
                    isActive: true);
                await _variantRepository.InsertAsync(variant, autoSave: true);
            }
            else
            {
                if (variant.IsDeleted)
                {
                    variant.IsDeleted = false;   // dirilt: ayni bilesim ayni Id'de yasamaya doner (unique ihlali yok)
                }

                variant.SetName(BuildCombinationName(product.Name, trial, quantityUnit));
                variant.SetActive(true);
            }

            variant.SetStock(trial.PackageCount);
            await _variantRepository.UpdateAsync(variant, autoSave: true);
            await ReplaceRecipeLinesIfChangedAsync(product.CompanyId, variant.Id, trial, context);
            await CopyTemplateLinesFromSiblingAsync(product, variant.Id);

            mainVariant ??= variant;   // selected Rank sirali geldi -> ilk = Rank 1 = ana aday
        }

        // Bilesimi hedefte olmayan CANLI varyantlar soft-silinir (dirilme yukarida; ANAVARYANT ilk Multi
        // gecisinde mesru olarak duser — yerine Rank 1 ana olur).
        foreach (var stale in existing.Where(v => !v.IsDeleted && !targetCodes.Contains(v.Code)))
        {
            // Origin filtresi ZORUNLU (yukaridaki tek-mod dalindaki gerekce): bilesimi dusen varyant ayni kodla
            // dirilebilir; kullanici/sablon satirlari o gun geri gelmeliydi.
            await _recipeLineRepository.DeleteAsync(
                l => l.ProductVariantId == stale.Id && l.Origin == RecipeLineOrigin.Substitution, autoSave: true);
            await _variantRepository.DeleteAsync(stale, autoSave: true);
        }

        if (mainVariant is not null)
        {
            // Rank 1 ana yapilir; manager tekil-ana degismezini korur (digerlerini indirger).
            await _variantManager.SetMainVariantAsync(mainVariant);
        }
    }

    /// <summary>Varyantın reçete satırlarını kombinasyondan YENİDEN kurar (kombinasyon reçetenin SAHİBİDİR —
    /// eski satırlar silinir, taze eklenir). Alan kurulumu SubstitutionChannelPlanProvider'ın kanonik
    /// <c>SubstitutionChannelPlanProvider.BuildRecipeLineDtos</c>'uyla AYNI matematik (hedef DTO değil ENTITY
    /// olduğundan yeniden kullanılamadı; sapma olursa referans orası).</summary>
    private async Task ReplaceRecipeLinesIfChangedAsync(
        Guid companyId, Guid variantId, SubstitutionTrialDto trial, SubstitutionChannelPlanContext context)
    {
        // DEGISMEMIS receteyi silip yeniden yazma (inceleme bulgusu #8): 15-dk repricing turu her muadil urune
        // job bastigindan kosulsuz sil+yaz her turda satir Id'lerini degistirirdi (audit gurultusu + bayat referans).
        // Imza da silme de YALNIZ muadillikten uretilen satirlari kapsar (Origin=Substitution). Onceden ikisi
        // ayrisiyordu: imza metal satirlarini karsilastiriyor ama silme TUM satirlari kapsiyordu — kullanicinin
        // elle ekledigi hizmet satiri (iscilik/paketleme/kargo) her yeniden hesaplamada SESSIZCE kayboluyordu.
        var existingLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == variantId && l.Origin == RecipeLineOrigin.Substitution)
                .OrderBy(l => l.LineOrder)
                .Select(l => new { l.CommodityId, l.CommodityVariantId, l.Quantity }));
        var existingSignature = string.Join("|",
            existingLines.Select(l => string.Concat(l.CommodityId, ":", l.CommodityVariantId, ":", l.Quantity)));
        var targetSignature = string.Join("|",
            trial.Lines.Select(l => string.Concat(l.MetalId, ":", l.VariantId, ":", (decimal)l.Count)));
        if (existingSignature == targetSignature)
        {
            return;
        }

        await _recipeLineRepository.DeleteAsync(
            l => l.ProductVariantId == variantId && l.Origin == RecipeLineOrigin.Substitution, autoSave: true);

        // Korunan satirlar (kullanici + sablon) muadil satirlarinin ARDINA kaydirilir: muadil emtialari recetenin
        // TABANIDIR ve turev satirlar ("ustumdekilerin toplami") onlari gorebilmelidir. Numaralandirma once
        // muadil 0..n-1, sonra korunanlar n.. seklinde yeniden kurulur.
        var preserved = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == variantId)
                .OrderBy(l => l.LineOrder));

        for (var i = 0; i < preserved.Count; i++)
        {
            var shifted = trial.Lines.Count + i;
            if (preserved[i].LineOrder != shifted)
            {
                preserved[i].SetOrder(shifted);
                await _recipeLineRepository.UpdateAsync(preserved[i], autoSave: true);
            }
        }

        for (var i = 0; i < trial.Lines.Count; i++)
        {
            var trialLine = trial.Lines[i];
            if (!context.MetalById.TryGetValue(trialLine.MetalId, out var metal))
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotFound");
            }

            var labor = trialLine.VariantId is { } variantRef
                ? context.LaborByVariantId.GetValueOrDefault(variantRef)
                : context.MainLaborByMetalId.GetValueOrDefault(trialLine.MetalId);

            var line = new ProductVariantRecipeLine(
                companyId,
                variantId,
                RecipeComponentType.CatalogCommodity,
                lineOrder: i);
            line.SetCatalogCommodity(
                ProcessType.Metal,
                metal.Id,
                trialLine.VariantId,
                quantity: trialLine.Count,
                amount: trialLine.Count * metal.StableQuantity,
                factor: metal.Factor,
                valuationUnitId: metal.FollowingUnitId,
                paymentType: ProcessPaymentType.Normal,
                payFactor: labor?.EntryLabor ?? 0m,
                payUnitId: labor?.EntryLaborUnitId);
            // Kaynak işareti: sonraki yenilemede yalnız BU satırlar silinsin (kullanıcı/şablon satırları kalsın).
            line.SetOrigin(RecipeLineOrigin.Substitution);
            await _recipeLineRepository.InsertAsync(line, autoSave: true);
        }
    }

    /// <summary>
    /// Varyant görünen adı — "Ürün — 1×5 + 3×1 = Toplam 4 parça, 8,0gr" (kod deterministik kimlik, ad okunabilirlik).
    ///
    /// <para><b>Biçim (2026-07-28 Hakan):</b> ADET×GRAMAJ okunuşu kullanıcının kombinasyonu sesli söyleyişiyle
    /// aynı ("bir tane beşlik, üç tane birlik"). Önceki biçim maden KODU×adet idi ("G5.0×1") — hem ters okunuyor
    /// hem gramajı kodun içinden çıkarmayı gerektiriyordu.</para>
    ///
    /// <para>Sondaki toplam ÖZET: kaç parçadan oluştuğu ve toplam gramaj, kombinasyonun hedefi tutturup
    /// tutturmadığını tek bakışta gösterir — satırları toplamak zorunda kalmadan.</para>
    ///
    /// <para>Varyantlı emtiada parça ayrıca varyant koduyla nitelenir ("1×5 (SARI)"): aynı gramajın iki farklı
    /// varyantı farklı işçilik/maliyet taşır, ayrışmazsa iki kombinasyon aynı ada düşer.</para>
    /// </summary>
    private static string BuildCombinationName(string productName, SubstitutionTrialDto trial, string quantityUnit)
    {
        var lines = trial.Lines
            .OrderByDescending(l => l.PieceWeight)
            .ThenBy(l => l.MetalCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var composition = string.Join(" + ", lines.Select(l =>
        {
            var body = $"{l.Count}×{FormatWeight(l.PieceWeight)} {quantityUnit}";
            return string.IsNullOrWhiteSpace(l.VariantCode) ? body : $"{body} ({l.VariantCode})";
        }));

        var pieceCount = lines.Sum(l => l.Count);
        var totalWeight = lines.Sum(l => l.Count * l.PieceWeight);
        var summary = string.Format(
            CultureInfo.CurrentCulture,
            "{0} = Toplam {1} parça, {2} {3}",
            composition, pieceCount, FormatWeight(totalWeight), quantityUnit);

        var name = $"{productName} — {summary}";
        return name.Length <= EntityVariantConsts.VariantNameMaxLength
            ? name
            : name[..EntityVariantConsts.VariantNameMaxLength];
    }

    /// <summary>
    /// Ürüne KAYITLI reçete şablonunu tek bir varyanta serer — kopyalanacak kardeş kalmadığında kullanılır.
    ///
    /// <para>Şablon yoksa ya da silinmişse SESSİZCE geçilir: şablon zorunlu değil ve eksikliği muadil
    /// materyalizasyonunu durdurmamalı (varyantlar doğsun, kullanıcı şablonu sonra bağlasın).</para>
    /// </summary>
    private async Task ApplyProductTemplateAsync(Product product, Guid targetVariantId)
    {
        if (product.RecipeTemplateId is not { } templateId || templateId == Guid.Empty)
        {
            return;
        }

        var template = await _recipeTemplateRepository.FindAsync(templateId);
        if (template is null)
        {
            return;
        }

        await _recipeTemplateApplier.ApplyToVariantAsync(product.CompanyId, targetVariantId, template);
    }

    /// <summary>Grubun gösterim birimi — grup yoksa/okunamazsa varsayılan ("gr"). Salt gösterim olduğundan
    /// okuma başarısızlığı materyalizasyonu DURDURMAZ.</summary>
    private async Task<string> ResolveQuantityUnitAsync(Guid? substitutionGroupId)
    {
        if (substitutionGroupId is not { } groupId || groupId == Guid.Empty)
        {
            return SubstitutionGroupConsts.DefaultQuantityUnit;
        }

        var group = await _groupRepository.FindAsync(groupId);
        return string.IsNullOrWhiteSpace(group?.QuantityUnit)
            ? SubstitutionGroupConsts.DefaultQuantityUnit
            : group.QuantityUnit;
    }

    /// <summary>Gramaj gösterimi — gereksiz sıfır kuyruğu atılır (5,00 → 5; 2,50 → 2,5): kombinasyon adı
    /// okunabilirlik içindir, ondalık gürültüsü uzun adı daha da uzatır.</summary>
    private static string FormatWeight(decimal weight)
    {
        return weight.ToString("0.###", CultureInfo.CurrentCulture);
    }
    /// <summary>
    /// YENİ doğan kombinasyon varyantına, ürünün BAŞKA bir varyantındaki reçete şablonu satırlarını çoğaltır.
    ///
    /// <para><b>Neden gerekli:</b> muadil ürünlerde varyant kümesi stoğa göre sürekli değişir. Kullanıcı şablonu
    /// uyguladıktan sonra doğan her yeni kombinasyon, şablon satırları olmadan (yani paketleme/kargo/sigorta
    /// maliyeti EKSİK) fiyatlanırdı ve bunu fark etmesi imkânsızdı.</para>
    ///
    /// <para><b>Neden şablona BAĞ kurmuyoruz:</b> "şablon bir kaynaktır, ürünle kalıcı bağ kurmaz" kararı
    /// (şablondaki sonraki değişiklik yüzlerce ürünü habersiz değiştirmesin). Bu yüzden şablon yeniden
    /// okunmaz — ürünün KENDİ mevcut satırları çoğaltılır.</para>
    ///
    /// <para><b>⚠ DÜZENLENMİŞ satır KAYNAK olarak taşınmaz</b> (2026-08-20 sahiplenme kuralının sonucu):
    /// kullanıcı şablondan gelen bir satırı düzenlediyse o satır artık
    /// <see cref="RecipeLineOrigin.TemplateEdited"/>'dır ve KOPYALAMA sorgusuna girmez — yeni kombinasyon
    /// şablonun DOKUNULMAMIŞ hâlini alır (hiç dokunulmamış satır kalmadıysa aşağıdaki dal ürünün kayıtlı
    /// şablonunu yeniden uygular). Düzenleme varyanta özgü bir karardır; onu her yeni kombinasyona sessizce
    /// taşımak da en az taşımamak kadar sürpriz olurdu — karar bilinçli olarak "şablon tabanı taşınır, kişisel
    /// düzeltme taşınmaz" yönünde. Bu paragraf eskiden "düzenlenmiş hâli taşınır" diyordu; kural değişti,
    /// doküman GERÇEĞE uyduruldu.</para>
    ///
    /// <para>Varyantta zaten ŞABLON SOYLU satır varsa (dokunulmamış ya da düzenlenmiş — dirilmiş varyant)
    /// DOKUNULMAZ: çoğaltma yalnız boşluğu doldurur, üstüne yazmaz.</para>
    /// </summary>
    private async Task CopyTemplateLinesFromSiblingAsync(Product product, Guid targetVariantId)
    {
        // Nöbetçi ŞABLON SOYUNUN TAMAMINA bakar (dokunulmamış + kullanıcı düzenlemesi): yalnız Template'e
        // baksaydı, tek satırlı bir şablonda kullanıcı o satırı düzelttiği anda varyant "şablonsuz" sayılır ve
        // üstüne İKİNCİ bir şablon seti serilirdi — paketleme/kargo/komisyon sessizce iki kez fiyatlanır.
        // Bu yol OTOMATİKTİR (her materyalizasyon turunda mevcut varyantlar için de koşar), yani hata
        // kullanıcının hiçbir tıklaması olmadan büyürdü.
        var alreadyHasTemplateLines = await _asyncExecuter.AnyAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == targetVariantId
                    && (l.Origin == RecipeLineOrigin.Template || l.Origin == RecipeLineOrigin.TemplateEdited)));
        if (alreadyHasTemplateLines)
        {
            return;
        }

        var siblingVariantIds = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id && v.Id != targetVariantId)
                .Select(v => v.Id));
        if (siblingVariantIds.Count == 0)
        {
            await ApplyProductTemplateAsync(product, targetVariantId);
            return;
        }

        var sourceLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => siblingVariantIds.Contains(l.ProductVariantId) && l.Origin == RecipeLineOrigin.Template)
                .OrderBy(l => l.ProductVariantId).ThenBy(l => l.LineOrder));
        if (sourceLines.Count == 0)
        {
            // Hiçbir kardeşte şablon satırı YOK → ürüne kayıtlı ŞABLONDAN kur. Bu dal olmadan, muadil hedefi
            // değiştiğinde (8gr → 10gr) tüm kombinasyonlar yeniden doğuyor, kopyalanacak kardeş kalmıyor ve
            // paketleme/kargo/sigorta satırları sessizce düşüyordu — fiyat eksik çıkıyor, kullanıcı göremiyordu
            // (2026-07-28 Hakan: "muadil miktarını değiştirdim, reçete şablonunun etkisini göremedim").
            await ApplyProductTemplateAsync(product, targetVariantId);
            return;
        }

        // TEK bir kardeşin satır kümesi kopyalanır (hepsini birleştirmek satırları katlardı).
        var templateVariantId = sourceLines[0].ProductVariantId;

        var nextOrder = await _asyncExecuter.CountAsync(
            (await _recipeLineRepository.GetQueryableAsync()).Where(l => l.ProductVariantId == targetVariantId));

        foreach (var source in sourceLines.Where(l => l.ProductVariantId == templateVariantId))
        {
            var copy = new ProductVariantRecipeLine(
                product.CompanyId, targetVariantId, source.ComponentType, nextOrder++);

            if (source.ComponentType == RecipeComponentType.CatalogCommodity && source.CommodityProcessType is { } family)
            {
                copy.SetCatalogCommodity(
                    family, source.CommodityId, source.CommodityVariantId, source.Quantity, source.Amount,
                    source.Factor, source.ValuationUnitId, source.PaymentType, source.PayFactor, source.PayUnitId);
            }
            else
            {
                // Taban DAİMA AllAbove: SelectedLines kaynak Id'leri kardeş varyanta ait olduğundan burada geçersizdir.
                copy.SetService(
                    source.CommodityId, RecipeDerivedBaseMode.AllAbove,
                    source.DerivedOperation ?? RecipeDerivedOperation.Percent, source.DerivedOperand, source.PayUnitId);
                copy.SetSideCostKind(source.SideCostKind);
            }

            copy.SetDescription(source.Description);
            copy.SetOrigin(RecipeLineOrigin.Template);

            // Soy kimliği KLONA DA taşınır (2026-08-21): taşınmazsa klon "kimliksiz" doğar — çoğalma üretmez ama
            // kullanıcı klonu düzenlerse o varyantta legacy çoğalma yolu (kimliksiz eşleme) geri gelirdi.
            copy.SetTemplateSource(source.SourceTemplateLineId);
            await _recipeLineRepository.InsertAsync(copy, autoSave: true);
        }
    }
}
