using System;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// ANA VARYANT DEĞİŞMEZLERİ (2026-08-08 Hakan kuralları).
///
/// <para><b>① Ana varyant pasifleştirilemez.</b> Ana varyant sahibin (emtia/ürün) kimliğini taşır; kodu
/// pazaryerine SKU olarak gider. Pasifleştirilebilseydi sahip "aktif" görünürken kimlik satırı kapalı olurdu —
/// tutarsız ve sessiz bir hâl. Kaydı satıştan çekmenin doğru yolu SAHİBİ pasifleştirmektir.</para>
///
/// <para><b>② Ana yapmak aktifleştirir.</b> Burada fail-fast bilinçle SEÇİLMEDİ: <c>EnsureMainVariantAsync</c>
/// ana varyantı olmayan sahipte listedeki ilk varyantı terfi ettirir. Tüm varyantlar pasifse fırlatmak sahibi
/// ANA VARYANTSIZ bırakırdı — kimlik taşıyan satır hiç olmazdı. Pasif satırı aktifleştirmek bundan iyidir.</para>
/// </summary>
public class MainVariantInvariantTests
{
    private static EntityVariant NewVariant(bool isMain)
    {
        return new EntityVariant(
            companyId: SimpleGuidGenerator.Instance.Create(),
            entityName: "Metal",
            entityId: SimpleGuidGenerator.Instance.Create(),
            code: "G1.0 GR 995",
            name: "1.00gr 995 Gramaltın",
            isMain: isMain);
    }

    [Fact]
    public void Main_variant_cannot_be_deactivated()
    {
        var main = NewVariant(isMain: true);

        var ex = Should.Throw<BusinessException>(() => main.SetActive(false));

        ex.Code.ShouldBe("TradeXpress:EntityVariant:MainCannotBeDeactivated");
        main.IsActive.ShouldBeTrue();   // durum DEĞİŞMEDİ — yarım uygulanmış bir işlem kalmaz
    }

    /// <summary>Ana OLMAYAN varyant serbestçe pasifleştirilir — kural yalnız ana varyantı bağlar.</summary>
    [Fact]
    public void A_non_main_variant_can_still_be_deactivated()
    {
        var other = NewVariant(isMain: false);

        other.SetActive(false);

        other.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Promoting_a_passive_variant_to_main_activates_it()
    {
        var variant = NewVariant(isMain: false);
        variant.SetActive(false);

        variant.SetAsMain(true);

        variant.IsMain.ShouldBeTrue();
        variant.IsActive.ShouldBeTrue();
    }

    /// <summary>Ana bayrağı DÜŞÜNCE aktiflik zorlanmaz — o varyant artık sıradan bir varyanttır ve
    /// pasifleştirilebilir hâle gelir. (Aksi hâlde eski ana varyant sonsuza kadar kapatılamazdı.)</summary>
    [Fact]
    public void Demoting_a_variant_releases_the_activation_lock()
    {
        var variant = NewVariant(isMain: true);

        variant.SetAsMain(false);
        variant.SetActive(false);

        variant.IsActive.ShouldBeFalse();
    }
}
