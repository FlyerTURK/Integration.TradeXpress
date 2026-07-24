using System;
using System.Collections.Generic;
using Integration.TradeXpress.Substitutions;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="Product.VariantMode"/> + <see cref="Product.SetSubstitutionConfig"/> sözleşme testleri (Dilim-3).
/// Değişmezler: varsayılan mod MultiVariant (STATÜKO — mevcut ürün akışları davranış değiştirmez); muadil
/// konfigürasyonu yalnız Substitution modunda yaşar (mod dışında TEMİZLENİR); Substitution modunda grup zorunlu +
/// hedef &gt; 0 + tolerans çifti tutarlı (fail-fast); override kümesi Dilim-1 SetIncludedVariants sözleşmesiyle
/// normalize edilir (boş-Guid/duplike ayıklanır, kullanıcı sırası korunur; BOŞ = gruptan devral).
/// </summary>
public class ProductVariantModeTests
{
    [Fact]
    public void New_product_defaults_to_MultiVariant_with_empty_substitution_config()
    {
        var product = CreateProduct();

        product.VariantMode.ShouldBe(ProductVariantMode.MultiVariant);
        product.SubstitutionGroupId.ShouldBeNull();
        product.SubstitutionTargetQuantity.ShouldBeNull();
        product.SubstitutionToleranceType.ShouldBeNull();
        product.SubstitutionToleranceValue.ShouldBeNull();
        product.SubstitutionOverrideVariantIds.ShouldBeEmpty();
    }

    [Fact]
    public void SetSubstitutionConfig_outside_substitution_mode_clears_all_fields()
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);
        product.SetSubstitutionConfig(Guid.NewGuid(), 10m, ToleranceType.Amount, 0.5m, new[] { Guid.NewGuid() });

        // Mod Muadil'den çıkınca konfigürasyon bayat kalmamalı (tutarlılık tek mutator'da).
        product.SetVariantMode(ProductVariantMode.SingleVariant);
        product.SetSubstitutionConfig(Guid.NewGuid(), 10m, ToleranceType.Amount, 0.5m, new[] { Guid.NewGuid() });

        product.SubstitutionGroupId.ShouldBeNull();
        product.SubstitutionTargetQuantity.ShouldBeNull();
        product.SubstitutionToleranceType.ShouldBeNull();
        product.SubstitutionToleranceValue.ShouldBeNull();
        product.SubstitutionOverrideVariantIds.ShouldBeEmpty();
    }

    [Fact]
    public void Substitution_mode_requires_group()
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);

        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(null, 10m, null, null, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionGroupRequired");

        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(Guid.Empty, 10m, null, null, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionGroupRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    public void Substitution_mode_requires_positive_target_quantity(string? rawTarget)
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);
        decimal? target = rawTarget is null ? null : decimal.Parse(rawTarget);

        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(Guid.NewGuid(), target, null, null, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionTargetQuantityInvalid");
    }

    [Fact]
    public void Tolerance_type_and_value_must_be_paired_and_non_negative()
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);
        var groupId = Guid.NewGuid();

        // Tek başına tür ya da tek başına değer → tutarsız çift (fail-fast).
        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(groupId, 10m, ToleranceType.Amount, null, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionToleranceInvalid");
        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(groupId, 10m, null, 0.5m, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionToleranceInvalid");

        // Negatif değer → fail-fast (grup SetTolerance kuralıyla hizalı).
        Should.Throw<BusinessException>(() =>
                product.SetSubstitutionConfig(groupId, 10m, ToleranceType.Amount, -0.1m, null))
            .Code.ShouldBe("TradeXpress:Product:SubstitutionToleranceInvalid");
    }

    [Fact]
    public void Valid_substitution_config_is_stored_and_override_set_is_normalized()
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);
        var groupId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        product.SetSubstitutionConfig(
            groupId, 12.5m, ToleranceType.PerMille, 5m,
            new[] { first, Guid.Empty, second, first });   // boş-Guid + duplike ayıklanır, sıra korunur

        product.SubstitutionGroupId.ShouldBe(groupId);
        product.SubstitutionTargetQuantity.ShouldBe(12.5m);
        product.SubstitutionToleranceType.ShouldBe(ToleranceType.PerMille);
        product.SubstitutionToleranceValue.ShouldBe(5m);
        product.SubstitutionOverrideVariantIds.ShouldBe(new List<Guid> { first, second });
    }

    [Fact]
    public void Tolerance_pair_can_be_left_empty_to_inherit_group_policy()
    {
        var product = CreateProduct();
        product.SetVariantMode(ProductVariantMode.Substitution);

        // Tolerans çifti boş = grubun tolerans politikası (override yok) — geçerli konfigürasyon.
        product.SetSubstitutionConfig(Guid.NewGuid(), 10m, null, null, null);

        product.SubstitutionToleranceType.ShouldBeNull();
        product.SubstitutionToleranceValue.ShouldBeNull();
        product.SubstitutionOverrideVariantIds.ShouldBeEmpty();   // boş = gruptan devral
    }

    private static Product CreateProduct()
    {
        return new Product(Guid.NewGuid(), "TSTMODE", "Test Mod Ürünü");
    }
}
