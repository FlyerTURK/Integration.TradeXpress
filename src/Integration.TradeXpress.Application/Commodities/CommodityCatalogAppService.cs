using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.Framework.Base.Dtos;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Commodities;

/// <summary>
/// EMTİA kataloğu CRUD ara tabanı — yedi ailenin (Metal · Scrap · Future · Jewelry · Stone · Good · Service)
/// ortak atası. Tek işi <b>reçete kullanım guard'ını</b> tek yerden zorlamak.
///
/// <para><b>Neden Framework'e değil BURAYA:</b> <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// <c>Integration.Framework</c>'te yaşıyor ve emtia OLMAYAN dört katalog da onu paylaşıyor
/// (Parity · Cash · Country · SpecialCode) — guard'ı oraya koymak onları da kirletirdi. Ayrıca
/// <see cref="ProcessType"/> Framework'ün bilmemesi gereken bir TradeXpress kavramıdır. Bu ara taban,
/// guard'ın 7 servis dosyasına KOPYALANMASINI da önler (§4: en merkezi yerleşim).</para>
///
/// <para><b>Bağımlılık property injection ile:</b> guard'ı ctor'a eklemek yedi türevin ctor imzasını
/// değiştirmeyi gerektirirdi (yedi dosyada değişiklik, sıfır kazanç). ABP'nin <c>LazyServiceProvider</c>'ı bu iş için
/// vardır ve kod tabanında yerleşik desendir (ör. <c>GoodReportAppService</c>).</para>
///
/// <para><b>Kapsam (2026-08-05 Hakan kararı):</b> SİLME sert bloktur — reçetede kullanılan emtia silinemez,
/// kullanıcı önce reçeteleri temizler. PASİFLEŞTİRME serbesttir ve kullanan varyantları <c>Suspended</c>'a
/// düşürür; o dal ürün/varyant statüsüyle birlikte gelir (statü olmadan ürünü koyacak yer yok).</para>
/// </summary>
public abstract class CommodityCatalogAppService<TEntity, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    : HostCatalogCrudAppService<TEntity, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    where TEntity : class, IEntity<Guid>, IMultiTenant
    where TGetDto : class
    where TListDto : class
    where TListRequest : ListRequestDto
    where TCreateInput : class
    where TUpdateInput : class
{
    protected CommodityCatalogAppService(IRepository<TEntity, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    /// <summary>Bu kataloğun emtia ailesi — kullanım sorgusunun filtre anahtarı.
    /// <b>ZORUNLU:</b> <c>CommodityId</c> FK'sız snapshot olduğu için aynı Guid farklı ailede çakışabilir;
    /// aile olmadan sorgu yanlış ürünleri bloklardı.</summary>
    protected abstract ProcessType Family { get; }

    /// <summary>Entity'nin AKTİFLİK değeri — pasifleştirme geçişini tespit etmek için.
    /// <para>Neden abstract: emtia entity'leri ortak bir <c>IsActive</c> ARAYÜZÜ uygulamıyor (yalnız
    /// property'leri var), dolayısıyla taban onu tipli okuyamaz. Türev tek satırla verir; yansıma yerine
    /// derleme-zamanı garanti — yeni bir aile eklenirse derlemez, sessizce atlanmaz.</para></summary>
    protected abstract bool IsActiveOf(TEntity entity);

    /// <summary>Reçete kullanım endeksi — lazy (bkz. sınıf doc'u: ctor imzalarını korumak için).</summary>
    protected RecipeCommodityIndex CommodityIndex
    {
        get { return LazyServiceProvider.LazyGetRequiredService<RecipeCommodityIndex>(); }
    }

    /// <summary>
    /// EMTİA → ÜRÜN köprüsünün ortak servisi (CommodityToProductProjector) — lazy (aynı gerekçe: yedi türevin ctor imzasını değiştirmemek).
    ///
    /// <para>Türevler <c>ProjectToProductAsync</c>'lerini bunun üzerinden kurar; şekli
    /// <c>CommodityProjectionShapes.Of(<see cref="Family"/>)</c> verir — aile bilgisi burada ZATEN var,
    /// ikinci kez beyan edilirse ikisi ayrışabilir (connascence).</para>
    /// </summary>
    protected CommodityToProductProjector CommodityToProduct
    {
        get { return LazyServiceProvider.LazyGetRequiredService<CommodityToProductProjector>(); }
    }

    /// <summary>
    /// SİLME GUARD'I — reçetede kullanılan emtia silinemez.
    ///
    /// <para><b>⚠ Neden <c>BeforeDeleteAsync</c> DEĞİL (2026-08-05'te test yakaladı):</b> ilk denemede guard
    /// oraya konmuştu — ama Good/Metal/Jewelry/Stone o hook'u override ediyor ve <b>hiçbiri
    /// <c>base.BeforeDeleteAsync</c> çağırmıyor</b>. Guard sessizce baypas oluyordu; yalnız override etmeyen
    /// üç ailede (Scrap/Future/Service) çalışıyordu. Genişletme noktasının İÇİNE konan guard, türevin onu
    /// çağırmayı hatırlamasına bağımlıdır — bu, sessiz baypas yoludur.</para>
    ///
    /// <para><b>Çözüm:</b> guard genişletme noktasının ÜSTÜNE konur. Türevlerin hiçbiri <c>DeleteAsync</c>'i
    /// override etmiyor (konvansiyon testi: <c>CommodityGuardConventionTests</c> bunu KİLİTLER), dolayısıyla buradan
    /// atlanamaz. Türev temizliği <c>BeforeDeleteAsync</c>'te olduğu gibi çalışmaya devam eder —
    /// <c>base.DeleteAsync</c> onu zaten çağırır.</para>
    ///
    /// <para><b>Şablon kullanımı BLOKLAMAZ</b> (Hakan kararı): reçete şablonu bir taslaktır, canlı satış
    /// değildir; kullanılmayan bir şablon yüzünden emtia silinememesi orantısız olurdu ve şablon zaten
    /// uygulanırken hata verir. Canlı kullanım (ürün reçetesi + kanal reçeteleri) ise sert blok.</para>
    ///
    /// <para><b>Policy önce:</b> yetkisiz kullanıcı "kullanımda" mesajı almamalı (bilgi sızdırır) → guard'dan
    /// ÖNCE <c>CheckDeletePolicyAsync</c>. <c>base.DeleteAsync</c> onu ikinci kez çağırır; idempotent ve ucuz,
    /// karşılığında sıralama garantisi net kalır.</para>
    /// </summary>
    public override async Task DeleteAsync(Guid id)
    {
        await CheckDeletePolicyAsync();
        await EnsureNotUsedInRecipesAsync(id);
        await base.DeleteAsync(id);
    }

    /// <summary>
    /// PASİFLEŞTİRME → KADEMELİ ASKIYA ALMA (2026-08-05 Hakan kararı).
    ///
    /// <para>Silme sert bloktur, ama <b>pasifleştirme serbesttir</b> — "yumuşak emeklilik" yolu. Karşılığında
    /// o emtiayı kullanan varyantlar <see cref="ProductSaleStatus.Suspended"/>'a düşer ve satıştan çıkar;
    /// ürünün TÜM varyantları düştüyse ürün de. Böylece pasif emtia asla satışa sunulmuş olmaz.</para>
    ///
    /// <para><b>⚠ Tespit ANI kritik:</b> taban <c>UpdateAsync</c> mapping'i bu hook'tan SONRA yapar, yani
    /// entity hâlâ ESKİ <c>IsActive</c>'i taşır; yeni değer input DTO'sundadır. Aktif→pasif geçişi ancak
    /// ikisini karşılaştırarak anlaşılır — tek başına entity'ye bakmak "hep pasif" sanısı verirdi.</para>
    ///
    /// <para>Yalnız aktif→pasif GEÇİŞİNDE koşar: zaten pasif bir kaydın her güncellemesinde varyant taramak
    /// gereksiz yük olurdu ve kullanıcının elle Ready yaptığı varyantı tekrar askıya alırdı.</para>
    /// </summary>
    protected override async Task BeforeUpdateAsync(TEntity entity, TUpdateInput input)
    {
        await base.BeforeUpdateAsync(entity, input);

        if (!IsDeactivationTransition(entity, input))
        {
            return;
        }

        await SuspendUsingVariantsAsync(entity.Id);
    }

    /// <summary>Kayıt AKTİF iken input PASİF getiriyor mu.
    /// <para><b>⚠ Entity tarafı arayüzle okunamaz</b> (2026-08-05'te test yakaladı): <c>IHasIsActive</c>
    /// DTO-tarafı bir arayüzdür, emtia entity'leri onu uygulamaz — yalnız düz bir <c>IsActive</c> property'leri
    /// vardır. <c>entity is IHasIsActive</c> DAİMA false dönüyordu, yani geçiş hiç tespit edilmiyor ve
    /// askıya alma sessizce hiç koşmuyordu. Bu yüzden entity tarafı <see cref="IsActiveOf"/> ile türevden
    /// AÇIKÇA istenir; yansıma yok, sessiz başarısızlık yok.</para></summary>
    private bool IsDeactivationTransition(TEntity entity, TUpdateInput input)
    {
        return IsActiveOf(entity)
               && input is IHasIsActive incoming
               && !incoming.IsActive;
    }

    /// <summary>Emtiayı kullanan varyantları askıya alır; ürünün tüm varyantları düştüyse ürünü de.</summary>
    private async Task SuspendUsingVariantsAsync(Guid commodityId)
    {
        var usage = await CommodityIndex.FindUsageAsync(Family, new[] { commodityId });

        var variantIds = usage
            .Where(u => u.Kind == CommodityUsageKind.ProductRecipe)
            .SelectMany(u => u.VariantIds)
            .Distinct()
            .ToList();

        if (variantIds.Count == 0)
        {
            return;
        }

        await LazyServiceProvider
            .LazyGetRequiredService<ProductSaleSuspender>()
            .SuspendVariantsAsync(variantIds);
    }

    /// <summary>Canlı reçete kullanımı varsa fırlatır; kullanıcıya HANGİ kayıtlar olduğunu söyler
    /// (salt "kullanımda" mesajı çıkmaz sokaktır — nereyi temizleyeceğini bilmeli).</summary>
    protected virtual async Task EnsureNotUsedInRecipesAsync(Guid commodityId)
    {
        var usage = await CommodityIndex.FindUsageAsync(Family, new[] { commodityId });

        var blocking = usage.Where(u => u.BlocksDeletion).ToList();
        if (blocking.Count == 0)
        {
            return;
        }

        throw new BusinessException("TradeXpress:Commodity:InUseByRecipes")
            .WithData("Count", blocking.Count)
            .WithData("Owners", string.Join(", ", blocking.Select(DescribeOwner).Take(OwnerPreviewCount)));
    }

    /// <summary>Uyarı metnindeki tek kayıt gösterimi — kanal kullanımında ad kanalın adıdır.</summary>
    private static string DescribeOwner(CommodityUsage usage)
    {
        var code = string.IsNullOrWhiteSpace(usage.OwnerCode) ? "?" : usage.OwnerCode;
        return usage.Kind == CommodityUsageKind.ChannelRecipe
            ? $"{usage.OwnerName}: {code}"
            : code;
    }

    /// <summary>Mesaja sığdırılan kayıt sayısı — tamamı UI'daki tıklanabilir listede gösterilir.</summary>
    private const int OwnerPreviewCount = 5;
}
