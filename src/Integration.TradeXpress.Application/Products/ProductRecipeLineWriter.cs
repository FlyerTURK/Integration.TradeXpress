using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;
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
///
/// <para><b>Giriş guard'ları:</b> katalog emtiası satırı 0 adet + 0 miktarla YAZILMAZ (2026-08-19;
/// <see cref="RecipeLineQuantityGate"/> → <see cref="RecipeLineQuantityRule"/>) ve EMTİASIZ da YAZILMAZ
/// (2026-08-21; <see cref="RecipeLineCommodityGate"/> → <see cref="RecipeLineCommodityRule"/>). İkisi de aynı
/// sınıf hatayı kapatır: satır kabul edilir ama hiçbir şeyi temsil etmez. Denetim tüm satırlar için herhangi bir
/// yazımdan önce yapılır.</para>
///
/// <para><b>SAHİPLENME (2026-08-20 Hakan kuralı: "mevcuttaki kayıtlar değer değişince kolayca silinmesin …
/// şablon varyantlarda kullanıcı değişikliğine müsait olsun"):</b> kullanıcı şablondan gelmiş bir satırı
/// düzenlerse satır ARTIK ONUNDUR — <see cref="RecipeLineOrigin.Template"/> →
/// <see cref="RecipeLineOrigin.TemplateEdited"/>. Böylece şablon ikinci kez uygulandığında
/// (<c>RecipeTemplateApplier</c> yalnız dokunulmamış <c>Template</c> satırlarını düşürür) düzenleme SESSİZCE
/// yok olmaz. Kural burada yaşar çünkü "kullanıcı bu satıra dokundu mu" bilgisini yalnız kayıt yolu görebilir;
/// şablon uygulayıcısı olayı çoktan kaçırmış olur.</para>
/// </summary>
public class ProductRecipeLineWriter : ITransientDependency
{
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly ChannelRecipeRefresher _channelRecipeRefresher;

    public ProductRecipeLineWriter(
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        ChannelRecipeRefresher channelRecipeRefresher)
    {
        _recipeLineRepository = recipeLineRepository;
        _channelRecipeRefresher = channelRecipeRefresher;
    }

    /// <summary>Reçete grafını (varyant-scope; Id + IsDeleted diff, Account/SubAccount deseni) persist eder.
    /// Bileşen türü set-once (toolbar tip belirler); LineOrder korunur. Company + varyant Id (jenerik
    /// <c>EntityVariant.Id</c>) çağırandan gelir. Kayıt sonrası devralınmış kanal kopyaları tazelenir
    /// (<see cref="ChannelRecipeRefresher"/> — devir kararı KAYIT-ÖNCESİ core'a karşı verilir; kayıt-öncesi
    /// durum ENTITY LİSTESİ olarak DEĞİL, değer-tipi imza snapshot'ı olarak alınır: aynı UoW'daki entity
    /// referansları yerinde güncellemeyle mutasyona uğrar ve "eski" liste yeni değerleri gösterirdi).</summary>
    public virtual async Task SaveAsync(Guid companyId, Guid variantId, List<ProductRecipeLineGraphDto> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return;
        }

        // RecipeLineQuantityGate (2026-08-19 Hakan kuralı: "ana emtialar 0 adet veya miktar olarak girilmemeli"):
        // katalog emtiası satırında adet ya da miktardan en az biri pozitif olmalı. TÜM satırlar HİÇBİR yazım
        // (silme dahil) olmadan ÖNCE denetlenir — fail-fast, kısmi yazım yok. Silinmek üzere gelen satır
        // denetlenmez (zaten gidiyor); hizmet satırı kapsam dışıdır (kural kendi içinde atlar). Satır numarası
        // kullanıcının gördüğü sırayla (LineOrder'a göre dizilmiş pozisyon) raporlanır.
        //
        // RecipeLineCommodityGate (2026-08-21 ölçümü): katalog emtiası satırı EMTİASIZ kaydedilebiliyordu —
        // ne kayıtta, ne satışa hazırlık doğrulamasında, ne push'ta hata çıkıyordu. Satır sessizce yanlış cevap
        // üretiyordu: ProductRecipeCostCalculator katalog kaydını bulamadığı için satırı maliyete katmıyor,
        // RecipeCommodityIndex satırı hiçbir emtiaya bağlayamadığı için stok tetiği o ürünü hiç uyandırmıyordu.
        // Guard BURADA, çünkü ProductVariantRecipeLine yazımının TEK kapısı burasıdır: ürün formu
        // (ProductAppService.SaveProductVariantDetailAsync) ve sihirbazın sınıflandırma adımı
        // (ProductCommodityProvisioner) bu metottan geçer, dolayısıyla ikisi de aynı kuralı görür. Çağıranlardan
        // birine koysaydık diğeri delik kalırdı; entity setter'ına koysaydık kurala uymayan satırı DÜZELTEN
        // yollar (muadil materyalizasyonu, şablon kopyası) da vurulurdu.
        //
        // KAPSAM YALNIZ CatalogCommodity: hizmet satırında CommodityId etiket referansıdır ve meşru şekilde boş
        // kalır (canlıda 3 N11 + 593 Trendyol kanal hizmet satırı emtiasız duruyor). Kural kendi içinde atlar.
        var candidates = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            RecipeLineQuantityGate.EnsureSatisfied(
                candidates[i].ComponentType,
                candidates[i].Quantity,
                candidates[i].Amount,
                i,
                candidates[i].CommodityProcessType);

            RecipeLineCommodityGate.EnsureSatisfied(
                candidates[i].ComponentType,
                candidates[i].CommodityId,
                i,
                candidates[i].CommodityProcessType);
        }

        var coreSignaturesBeforeSave = ChannelRecipeInheritance.SnapshotOf(
            await _recipeLineRepository.GetListAsync(l => l.ProductVariantId == variantId));

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

                // SAHİPLENME KIYASININ "ÖNCE" TARAFI — mutasyondan ÖNCE, DEĞER olarak alınır. Entity referansı
                // tutmak İŞE YARAMAZDI: EF kimlik haritası aynı satırı aynı instance'la döndürür, ApplyFields
                // onu yerinde değiştirir ve "eski" taraf sessizce yeni değerleri gösterirdi (CLAUDE.md §6;
                // ChannelRecipeInheritance.SnapshotOf ile aynı ders).
                var userFieldsBeforeSave = CaptureUserFields(entity);

                entity.SetOrder(l.LineOrder);
                ApplyFields(entity, l);
                TakeOwnershipIfEdited(entity, userFieldsBeforeSave);
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

        // Devralınmış kanal kopyalarını yeni bileşimle hizala (bileşim imzası değişmediyse refresher
        // kendi içinde kısa devre yapar — kanal sorgusuna inilmez).
        var coreLinesAfterSave = await _recipeLineRepository.GetListAsync(l => l.ProductVariantId == variantId);
        await _channelRecipeRefresher.RefreshAsync(variantId, coreSignaturesBeforeSave, coreLinesAfterSave);
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

    /// <summary>Şablondan gelmiş satır DÜZENLENDİYSE kullanıcıya devredilir
    /// (<see cref="RecipeLineOrigin.Template"/> → <see cref="RecipeLineOrigin.TemplateEdited"/>). Kıyas
    /// entity'nin kayıt-öncesi/sonrası DEĞER snapshot'ları arasındadır; istemciden gelen alanlarla değil —
    /// DTO ile entity arasındaki temsil farkları (ör. nullable olmayan <c>ManualAmount</c>) her kaydetmede
    /// sahte "değişti" üretirdi.
    /// <para><b>Neden <c>Manual</c> DEĞİL:</b> satırın şablon SOYU korunmalı — onu "şablon satırı olduğu için"
    /// koruyan/tanıyan yollar (muadil denemesini uygulama, muadil önizlemesi, materyalizasyon nöbetçisi) soyu
    /// silinmiş satırı tanıyamaz ve sırasıyla siler / ekranda düşürür / üstüne ikinci şablon seti serer
    /// (gerekçe: <see cref="RecipeLineOrigin.TemplateEdited"/>).</para>
    /// <para>Yalnız <see cref="RecipeLineOrigin.Template"/> kapsamdadır: <c>Manual</c> ve <c>TemplateEdited</c>
    /// zaten kullanıcınındır, <c>Substitution</c> ise kombinasyondan TÜRETİLİR ve sahiplenirse muadil
    /// materyalizasyonu satırı bir daha güncelleyemez.</para></summary>
    private static void TakeOwnershipIfEdited(ProductVariantRecipeLine entity, RecipeLineUserFields fieldsBeforeSave)
    {
        if (entity.Origin != RecipeLineOrigin.Template)
        {
            return;
        }

        if (CaptureUserFields(entity) == fieldsBeforeSave)
        {
            return;
        }

        entity.SetOrigin(RecipeLineOrigin.TemplateEdited);
    }

    /// <summary>Satırın KULLANICI ALANLARININ donuk snapshot'ı (değer tipi → entity mutasyona uğrasa da değişmez).
    /// <para><b>LineOrder BİLİNÇLİ olarak dışarıdadır:</b> sıra her kaydetmede 0..n-1 yeniden numaralanır
    /// (silinen bir üst satır alttakilerin sırasını kaydırır) — sıranın değişmesi kullanıcının "bu satır artık
    /// benim" niyetini göstermez; koysaydık ilgisiz bir silme, dokunulmamış şablon satırlarını sahiplendirirdi.
    /// <c>SideCostKind</c> da dışarıdadır: onu kullanıcı değil composer/şablon yazar.</para></summary>
    private static RecipeLineUserFields CaptureUserFields(ProductVariantRecipeLine entity)
    {
        return new RecipeLineUserFields(
            entity.CommodityProcessType,
            entity.CommodityId,
            entity.CommodityVariantId,
            entity.Quantity,
            entity.Amount,
            entity.Factor,
            entity.ValuationUnitId,
            entity.PaymentType,
            entity.PayFactor,
            entity.PayUnitId,
            entity.ManualAmount,
            entity.ManualUnitId,
            entity.Description,
            entity.DerivedBaseMode,
            entity.DerivedOperation,
            entity.DerivedOperand);
    }

    /// <summary>Sahiplenme kıyasına giren alanlar — record struct olduğu için değer eşitliği HAZIR gelir
    /// (decimal karşılaştırması ölçekten bağımsızdır: 2,0 ile 2,00 aynı sayılır, sahte sahiplenme üretmez).</summary>
    private readonly record struct RecipeLineUserFields(
        ProcessType? CommodityProcessType,
        Guid? CommodityId,
        Guid? CommodityVariantId,
        decimal Quantity,
        decimal Amount,
        decimal Factor,
        Guid? ValuationUnitId,
        ProcessPaymentType PaymentType,
        decimal PayFactor,
        Guid? PayUnitId,
        decimal? ManualAmount,
        Guid? ManualUnitId,
        string? Description,
        RecipeDerivedBaseMode? DerivedBaseMode,
        RecipeDerivedOperation? DerivedOperation,
        decimal DerivedOperand);
}
