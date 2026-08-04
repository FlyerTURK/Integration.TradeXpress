using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.ProductCategories;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.ProductCategories;

/// <summary>
/// Kategori listesinde YOL kolonuyla arama.
///
/// <para><b>Neden özel bir yol gerekti:</b> <c>Path</c> bir entity kolonu DEĞİL — sorgudan sonra ağaç
/// yürünerek hesaplanıyor. Grid'in filtre satırı bu kolonu da sunucuya gönderiyor ama SQL'e çevrilemediği için
/// eleme hiç uygulanmıyordu: kullanıcı yazıyor, liste değişmiyordu (2026-08-04 Hakan bulgusu — Ad kolonu
/// kaldırılınca aranabilir tek metin kolonu da gitmişti).</para>
///
/// <para>Çözüm: yol filtreleri istekten ayrılıp hesaplamadan SONRA bellekte uygulanıyor ve sayfalama da
/// elenmiş küme üzerinden yapılıyor. Bu testler hem elemenin çalıştığını hem SAYFALAMANIN tutarlı kaldığını
/// kilitler — filtreden önce sayfalasaydık "1. sayfada 3 sonuç, 2. sayfada 0" gibi bir liste çıkardı.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductCategoryPathFilterTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IProductCategoryAppService _appService;
    private readonly ICurrentCompany _currentCompany;

    public ProductCategoryPathFilterTests()
    {
        _appService = GetRequiredService<IProductCategoryAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Path_filter_matches_any_segment_not_just_the_leaf()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            await SeedTreeAsync();

            // "Yüzük" ARA SEGMENT — ada bakan bir filtre bunu bulamazdı (yaprak "Alyans").
            var result = await FilterByPathAsync("Yüzük");

            result.Items.Select(i => i.Name).ShouldContain("Alyans");
            result.Items.ShouldAllBe(i => i.Path.Contains("Yüzük", StringComparison.Ordinal));
        }
    }

    /// <summary>Kullanıcı Türkçe karakterleri yazmayabilir (klavye, alışkanlık, mobil). ASCII karşılığıyla
    /// yazılan terim de bulmalı; aksi hâlde arama kullanıcıyı sürekli yanıltır.
    ///
    /// <para><b>TÜM özel harfler kapsanır</b> (2026-08-04 Hakan): ç · ğ · ı · İ · ö · ş · ü. Tek tek yazıldı,
    /// çünkü katlama tablosundan bir harfin düşmesi ancak o harfi arayan kullanıcıda görünür — ve sessizce
    /// "sonuç yok" olarak görünür.</para></summary>
    [Theory]
    [InlineData("taki", "Takı")]                // ı → i
    [InlineData("Yuzuk", "Yüzük")]              // ü → u
    [InlineData("yuzuk", "Yüzük")]              // + küçük harf
    [InlineData("YUZUK", "Yüzük")]              // + büyük harf
    [InlineData("orgu", "Örgü Bilezik")]        // ö → o  ve  ü → u
    [InlineData("ORGU", "Örgü Bilezik")]
    [InlineData("cubuk", "Çubuk Küpe")]         // ç → c
    [InlineData("CUBUK", "Çubuk Küpe")]
    [InlineData("dugun", "Düğün Seti")]         // ğ → g
    [InlineData("sahmeran", "Şahmeran")]        // ş → s
    [InlineData("inci", "İnci Kolye")]          // İ → i
    [InlineData("kupe", "Çubuk Küpe")]          // kelime ORTASINDA ü
    public async Task Path_filter_folds_every_turkish_character(string term, string beklenen)
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            await SeedTreeAsync();

            var result = await FilterByPathAsync(term);

            // Yalnız "boş değil" yetmez: DOĞRU kategoriyi bulduğunu da doğrula.
            result.Items.Select(i => i.Name).ShouldContain(beklenen);
        }
    }

    [Fact]
    public async Task Total_count_reflects_the_filtered_set_so_the_pager_is_honest()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            await SeedTreeAsync();

            var filtered = await FilterByPathAsync("Alyans");
            var unfiltered = await _appService.GetListAsync(new ProductCategoryListRequestDto());

            filtered.TotalCount.ShouldBeLessThan(unfiltered.TotalCount);
            filtered.TotalCount.ShouldBe(filtered.Items.Count);
        }
    }

    [Fact]
    public async Task Non_matching_filter_returns_empty_rather_than_everything()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            await SeedTreeAsync();

            // Eleme SESSİZCE ATLANIRSA tüm liste dönerdi — bu testin yakaladığı ASIL hata buydu.
            var result = await FilterByPathAsync("boyle-bir-kategori-yok");

            result.Items.ShouldBeEmpty();
            result.TotalCount.ShouldBe(0);
        }
    }

    private Task<Volo.Abp.Application.Dtos.PagedResultDto<ProductCategoryListDto>> FilterByPathAsync(string term)
    {
        return _appService.GetListAsync(new ProductCategoryListRequestDto
        {
            Filters = new List<FilterField>
            {
                new() { Field = nameof(ProductCategoryListDto.Path), Operator = ListFilterOperator.Contains, Value = term },
            },
        });
    }

    /// <summary>Takı › Yüzük › Alyans  +  Takı › Kolye  +  Saat (ayrı kök)  +  TÜM Türkçe özel harflerini
    /// içeren gerçekçi kuyum adları (ç ğ ı İ ö ş ü) — katlama testinin veri tabanı.</summary>
    private async Task SeedTreeAsync()
    {
        var taki = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Takı" });          // ı
        var yuzuk = await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Yüzük", ParentId = taki.Id });   // ü
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Alyans", ParentId = yuzuk.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Kolye", ParentId = taki.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Saat" });

        // Türkçe özel harflerin HEPSİ temsil edilsin: ö ü (Örgü) · ç (Çubuk) · ğ (Düğün) · ş (Şahmeran) · İ (İnci).
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Örgü Bilezik", ParentId = taki.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Çubuk Küpe", ParentId = taki.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Düğün Seti", ParentId = taki.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "Şahmeran", ParentId = taki.Id });
        await _appService.CreateAsync(new ProductCategoryCreateDto { Name = "İnci Kolye", ParentId = taki.Id });
    }
}
