using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Çekirdek kategori ↔ kanal kategorisi eşleştirmesi ve KOMİSYON çözümü.
///
/// <para><b>Sınanan asıl kural KALITIM:</b> kullanıcı "Takı" düzeyinde bir kez eşleştirir, altındaki onlarca
/// kategori otomatik çözülür; kendi eşleştirmesi olan ise onu ezer (en dar tanım kazanır). Bu davranış Hakan'ın
/// "her satış kanalında kategori seçme zahmetinden kurtulalım" hedefinin ta kendisidir.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductCategoryChannelMappingTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IProductCategoryAppService _appService;
    private readonly ProductCategoryChannelResolver _resolver;
    private readonly IRepository<N11Category, Guid> _n11Categories;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public ProductCategoryChannelMappingTests()
    {
        _appService = GetRequiredService<IProductCategoryAppService>();
        _resolver = GetRequiredService<ProductCategoryChannelResolver>();
        _n11Categories = GetRequiredService<IRepository<N11Category, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Mapping_is_upserted_not_duplicated_per_channel()
    {
        // Kanal başına TEK satır: ikinci kayıt yeni satır açsaydı "hangisi geçerli" belirsizliği doğar ve
        // komisyon çözümü rastgele olurdu.
        await InCompanyAsync(async () =>
        {
            var category = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Takı" });

            await SaveMappingAsync(category.Id, "1001", "Takı > Yüzük");
            await SaveMappingAsync(category.Id, "2002", "Takı > Bileklik");

            var mappings = await _appService.GetChannelMappingsAsync(category.Id);

            mappings.ShouldHaveSingleItem().ChannelCategoryExternalId.ShouldBe("2002");
        });
    }

    [Fact]
    public async Task A_child_inherits_the_nearest_ancestor_mapping()
    {
        await InCompanyAsync(async () =>
        {
            var root = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Takı" });
            var child = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Yüzük", ParentId = root.Id });
            var grandChild = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Alyans", ParentId = child.Id });

            await SaveMappingAsync(root.Id, "1001", "Takı");

            var resolution = await _appService.ResolveChannelAsync(grandChild.Id, SalesChannelType.TrN11);

            resolution.ChannelCategoryExternalId.ShouldBe("1001");
            resolution.SourceCategoryId.ShouldBe(root.Id);
            resolution.IsInherited.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task The_closest_mapping_wins_over_an_ancestor()
    {
        // En DAR tanım kazanır: alt kategori kendi eşleştirmesini tanımlarsa üstünki devreye girmez.
        await InCompanyAsync(async () =>
        {
            var root = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Takı" });
            var child = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Yüzük", ParentId = root.Id });

            await SaveMappingAsync(root.Id, "1001", "Takı");
            await SaveMappingAsync(child.Id, "1002", "Yüzük");

            var resolution = await _appService.ResolveChannelAsync(child.Id, SalesChannelType.TrN11);

            resolution.ChannelCategoryExternalId.ShouldBe("1002");
            resolution.IsInherited.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Removing_a_mapping_falls_back_to_the_ancestor()
    {
        // Silme "eşleştirme yok" demek DEĞİL, "kendi tanımını kaldır" demektir — üstteki yeniden devreye girer.
        await InCompanyAsync(async () =>
        {
            var root = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Takı" });
            var child = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Yüzük", ParentId = root.Id });

            await SaveMappingAsync(root.Id, "1001", "Takı");
            await SaveMappingAsync(child.Id, "1002", "Yüzük");
            await _appService.DeleteChannelMappingAsync(child.Id, SalesChannelType.TrN11);

            var resolution = await _appService.ResolveChannelAsync(child.Id, SalesChannelType.TrN11);

            resolution.ChannelCategoryExternalId.ShouldBe("1001");
            resolution.IsInherited.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Unmapped_category_resolves_to_nothing_instead_of_failing()
    {
        // FAIL-SOFT: eşleştirme olmadan da ürün kaydedilebilmeli ve fiyat hesaplanabilmeli.
        await InCompanyAsync(async () =>
        {
            var category = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Eşleşmemiş" });

            var resolution = await _appService.ResolveChannelAsync(category.Id, SalesChannelType.TrN11);

            resolution.ChannelCategoryExternalId.ShouldBeNull();
            resolution.EffectiveCommissionRate.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Commission_is_resolved_from_the_mapped_channel_category()
    {
        // Zincirin bütünü: çekirdek kategori → eşleştirme → kanal taksonomisi → efektif oran.
        // Efektif oran = komisyon + (pazarlama + pazaryeri) × 1,20 → 19 + (1 + 0,67) × 1,20 = 21,004.
        var externalId = "TEST-" + SimpleGuidGenerator.Instance.Create().ToString("N")[..8];

        await SeedHostCategoryAsync(externalId, commission: 19m, marketing: 1m, marketplace: 0.67m);

        await InCompanyAsync(async () =>
        {
            var category = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Komisyonlu" });
            await SaveMappingAsync(category.Id, externalId, "Test Kategori");

            var resolution = await _appService.ResolveChannelAsync(category.Id, SalesChannelType.TrN11);

            resolution.EffectiveCommissionRate.ShouldNotBeNull();
            resolution.EffectiveCommissionRate!.Value.ShouldBe(21.004m, tolerance: 0.001m);
        });
    }

    [Fact]
    public async Task Commission_is_null_when_the_mapped_channel_category_disappeared()
    {
        // Kanal taksonomisi yeniden senkronlandığında eşleştirme çözümlenemez hâle gelebilir — eşleştirme
        // SİLİNMEZ (kullanıcı yeniden seçsin), oran sessizce boş döner ve komisyon satırı üretilmez.
        await InCompanyAsync(async () =>
        {
            var category = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Kayıp Kategori" });
            await SaveMappingAsync(category.Id, "OLMAYAN-ID", "Silinmiş");

            var resolution = await _appService.ResolveChannelAsync(category.Id, SalesChannelType.TrN11);

            resolution.ChannelCategoryExternalId.ShouldBe("OLMAYAN-ID");   // eşleştirme duruyor
            resolution.EffectiveCommissionRate.ShouldBeNull();             // oran çözülemedi
        });
    }

    [Fact]
    public async Task Other_channels_have_no_category_commission()
    {
        // Trendyol/Etsy taksonomilerinde kategori bazlı komisyon YOK → çağıran kanal varsayılanına düşer.
        await InCompanyAsync(async () =>
        {
            var category = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Trendyol Kategorisi" });

            var rate = await _resolver.ResolveCommissionRateAsync(
                _companyContext.CompanyId!.Value, category.Id, SalesChannelType.TrTrendyol, channelDefaultRate: 15m);

            rate.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Picker_flags_a_category_with_no_mapping_anywhere_in_its_chain()
    {
        // Ürün formu bu bayrakla UYARIR. Eşleştirmesiz kategoriye bağlanan ürün pazaryerine gidemez ve
        // komisyonu çözülemez — hata vermediği için kullanıcı bunu ancak fiyat yanlış çıkınca fark ederdi.
        await InCompanyAsync(async () =>
        {
            var orphan = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Eşleşmesiz Kök" });

            var row = (await _appService.GetPickerListAsync()).Single(c => c.Id == orphan.Id);

            row.HasChannelMapping.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Picker_treats_an_inherited_mapping_as_present()
    {
        // Bayrak ETKİN eşleştirmeyi gösterir, "kendi satırı"nı değil: "Takı" düzeyindeki tek eşleştirme
        // altındaki tüm kategorileri kapsar. Kendi satırına baksaydı doğru kurulmuş her alt kategori
        // uyarı alır, uyarı gürültüye dönüşür ve gerçek eksikler görülmezdi.
        await InCompanyAsync(async () =>
        {
            var root = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Devralan Kök" });
            var child = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Devralan Alt", ParentId = root.Id });

            await SaveMappingAsync(root.Id, "1001", "Takı");

            var rows = await _appService.GetPickerListAsync();

            rows.Single(c => c.Id == root.Id).HasChannelMapping.ShouldBeTrue();
            rows.Single(c => c.Id == child.Id).HasChannelMapping.ShouldBeTrue();
        });
    }

    private Task<ProductCategoryChannelMappingDto> SaveMappingAsync(Guid categoryId, string externalId, string name)
    {
        return _appService.SaveChannelMappingAsync(categoryId, new ProductCategoryChannelMappingSaveDto
        {
            Channel = SalesChannelType.TrN11,
            ChannelCategoryExternalId = externalId,
            ChannelCategoryName = name,
        });
    }

    /// <summary>N11 taksonomisi HOST-GLOBAL (IMultiTenant değil) — tenant değiştirmeden yazılır.</summary>
    private async Task SeedHostCategoryAsync(string externalId, decimal commission, decimal marketing, decimal marketplace)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var category = new N11Category(externalId, null, "Test Kategori", isLeaf: true, lastModifiedExternal: null);
            category.SetCommission(commission, marketing, marketplace, payoutDays: 24);
            await _n11Categories.InsertAsync(category, autoSave: true);
        });
    }

    private async Task InCompanyAsync(Func<Task> body)
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;
            try
            {
                await body();
            }
            finally
            {
                _companyContext.CompanyId = null;
            }
        }
    }
}
