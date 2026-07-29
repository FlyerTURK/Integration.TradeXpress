using System;
using Integration.Framework;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// <see cref="ProductCategory"/> entity guard'ları (DB'siz). Ata zinciri gerektiren kurallar (döngü) burada
/// DEĞİL <see cref="ProductCategoryTreeManager"/>'dadır — entity yalnız kendi bildiğini doğrular.
/// </summary>
public class ProductCategoryTests
{
    private static readonly Guid CompanyId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Category_cannot_be_its_own_parent()
    {
        var category = new ProductCategory(CompanyId, "Takı");
        EntityHelper.TrySetId(category, () => Guid.NewGuid(), checkForDisableIdGenerationAttribute: false);

        Should.Throw<BusinessException>(() => category.SetParent(category.Id))
            .Code.ShouldBe("TradeXpress:ProductCategory:CannotBeOwnParent");
    }

    [Fact]
    public void Empty_parent_id_means_root()
    {
        // Combo "seçilmedi" durumunda Guid.Empty gönderebilir; bu var olmayan bir ebeveyne asılı öksüz kayıt
        // DEĞİL kök demektir.
        var category = new ProductCategory(CompanyId, "Takı", Guid.Empty);

        category.ParentId.ShouldBeNull();

        category.SetParent(Guid.Empty);
        category.ParentId.ShouldBeNull();
    }

    [Fact]
    public void Company_is_required()
    {
        // Sahiplik working company'den damgalanır; şirketsiz kategori kurulamaz (fail-closed).
        Should.Throw<RequiredPropertyException>(() => new ProductCategory(Guid.Empty, "Takı"));
    }

    [Fact]
    public void Name_is_normalized()
    {
        // Kategoride KOD YOK (2026-07-27 kararı) → kimlik addır; ad normalizasyonu bu yüzden tek dayanaktır:
        // kenar boşlukları kırpılır, çoklu boşluk teke iner, TitleCase uygulanır. Kardeş benzersizliği bu
        // normalize edilmiş ad üzerinden sınanır — "  yüzük " ile "Yüzük" aynı kategoridir.
        var category = new ProductCategory(CompanyId, "  takı   grubu ");

        category.Name.ShouldBe("Takı Grubu");
    }

    [Fact]
    public void ToString_returns_name()
    {
        // Kod kalkınca log/exception okunabilirliğinin dayanağı ad oldu (entity-conventions §ToString).
        new ProductCategory(CompanyId, "Takı").ToString().ShouldBe("Takı");
    }

    [Fact]
    public void New_category_is_active()
    {
        new ProductCategory(CompanyId, "Takı").IsActive.ShouldBeTrue();
    }
}
