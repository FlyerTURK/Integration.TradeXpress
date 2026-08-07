using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Komisyon oranı verisinin ve setter'ının saf birim testleri (DB/DI YOK).
///
/// <para><b>Neden ayrı bir ağ:</b> oran tablosu elle bakımı yapılan bir veri kümesidir ve bozulma biçimi
/// SESSİZDİR — "21.75" yerine "2175" yazmak derlenir, testler yeşil kalır, yalnız fiyat saçmalar. Aralık
/// iddiası bu sınıf hatayı kırmızıya çevirir. Kalıtım DAVRANIŞI ayrı yerde çivilenir:
/// <c>TrendyolCommissionResolverTests</c>.</para>
/// </summary>
public class TrendyolCommissionSeedRateTests
{
    /// <summary>Her seed oranı (0, 100) aralığında olmalı — bu bir YÜZDE, çarpan ya da tutar değil.</summary>
    [Fact]
    public void Every_seeded_rate_is_a_plausible_percentage()
    {
        TrendyolCommissionSeedRates.ByRootExternalId.ShouldNotBeEmpty();

        foreach (var (externalId, rate) in TrendyolCommissionSeedRates.ByRootExternalId)
        {
            rate.ShouldBeGreaterThan(0m, $"Kategori {externalId} sıfır/negatif oran taşıyor — sıfır 'komisyon yok' demektir.");
            rate.ShouldBeLessThan(100m, $"Kategori {externalId} oranı %100'ü aşıyor — büyük ihtimalle ondalık kaymış.");
        }
    }

    /// <summary>Anahtarlar Trendyol id'leridir (ad DEĞİL): boş/whitespace anahtar sync eşleşmesini sessizce
    /// kaçırırdı.</summary>
    [Fact]
    public void Every_seed_key_is_a_non_empty_external_id()
    {
        TrendyolCommissionSeedRates.ByRootExternalId.Keys
            .ShouldAllBe(k => !string.IsNullOrWhiteSpace(k));
    }

    /// <summary><c>null</c> = "bu seviyede TANIMLI DEĞİL, üstten miras al"; <c>0</c> = "komisyon yok" BEYANI.
    /// İkisi aynı şey değildir ve setter bu ayrımı korumalıdır — <c>null</c>'ı 0'a normalize eden bir "iyileştirme"
    /// kalıtımı tümden kapatırdı.</summary>
    [Fact]
    public void Null_rate_means_inherit_and_is_not_normalized_to_zero()
    {
        var category = new TrendyolCategory("1070", null, "Kozmetik & Kişisel Bakım", isLeaf: false);

        category.SetCommissionRate(17.50m);
        category.CommissionRate.ShouldBe(17.50m);

        category.SetCommissionRate(null);
        category.CommissionRate.ShouldBeNull();

        category.SetCommissionRate(0m);
        category.CommissionRate.ShouldBe(0m);   // sıfır MEŞRU bir beyandır (komisyonsuz anlaşma) — null'a çevrilmez
    }

    /// <summary>Negatif oran anlamsızdır; sessizce kabul edilip fiyatı ŞİŞİRMEK yerine reddedilir.</summary>
    [Fact]
    public void Negative_rate_is_rejected()
    {
        var category = new TrendyolCategory("1070", null, "Kozmetik & Kişisel Bakım", isLeaf: false);

        Should.Throw<BusinessException>(() => category.SetCommissionRate(-1m));
    }
}
