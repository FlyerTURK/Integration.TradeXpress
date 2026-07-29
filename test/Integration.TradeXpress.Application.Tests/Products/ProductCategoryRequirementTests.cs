using System;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün kategorisi ZORUNLU — public <see cref="IProductAppService"/> yüzeyinden (client kapısı güven sınırı
/// değildir; kural sunucuda durur).
///
/// <para><b>Neden kural:</b> kanal kategorisi, kanal nitelikleri ve komisyon oranı ürüne kategorisi üzerinden
/// çözülüyor. Kategorisiz ürün pazaryerine listelenemez ve fiyatı KOMİSYONSUZ — yani eksik — hesaplanır;
/// hiçbir hata vermediği için bu sessizce yanlış fiyata yol açar. KIRMIZIYSA kapı delik demektir.</para>
///
/// <para>Kategorinin KANAL EŞLEŞTİRMESİ burada aranmaz — o engellemeyen bir uyarıdır (gerekçe:
/// <c>ProductAppService.ApplyProductCategoryAsync</c>).</para>
/// </summary>
public abstract class ProductCategoryRequirementTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IProductAppService _productAppService;
    private readonly ICurrentCompany _currentCompany;

    protected ProductCategoryRequirementTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Create_without_a_category_is_rejected()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => _productAppService.CreateAsync(
                new ProductCreateDto
                {
                    Code = "TSTCATREQ1",
                    Name = "Kategorisiz Ürün",
                }));

            exception.Code.ShouldBe("TradeXpress:Product:ProductCategoryRequired");
        }
    }

    [Fact]
    public async Task Update_cannot_clear_the_category_of_an_existing_product()
    {
        // Güncelleme yolu ayrıca sınanır: yalnız Create korunsaydı, kaydedilmiş bir ürünün kategorisi
        // düzenleme sırasında boşaltılabilir ve ürün kurala aykırı hâle gelirdi.
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var categoryId = await CreateTestProductCategoryAsync();
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTCATREQ2",
                Name = "Kategorili Ürün",
                ProductCategoryId = categoryId,
            });

            var exception = await Should.ThrowAsync<BusinessException>(() => _productAppService.UpdateAsync(
                created.Id,
                new ProductUpdateDto
                {
                    Code = created.Code,
                    Name = created.Name,
                    IsActive = created.IsActive,
                    ProductCategoryId = null,
                }));

            exception.Code.ShouldBe("TradeXpress:Product:ProductCategoryRequired");
        }
    }

    [Fact]
    public async Task A_category_from_another_company_is_rejected()
    {
        // Şirket sınırı: başka şirketin kategorisi kabul edilseydi ürün hiçbir ekranda görünmeyen bir
        // kategoriye asılı kalır, kanal/komisyon çözümü de sessizce boş dönerdi.
        Guid foreignCategoryId;
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            foreignCategoryId = await CreateTestProductCategoryAsync("Yabancı Kategori");
        }

        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => _productAppService.CreateAsync(
                new ProductCreateDto
                {
                    Code = "TSTCATREQ3",
                    Name = "Yabancı Kategorili Ürün",
                    ProductCategoryId = foreignCategoryId,
                }));

            exception.Code.ShouldBe("TradeXpress:Product:ProductCategoryNotFound");
        }
    }
}
