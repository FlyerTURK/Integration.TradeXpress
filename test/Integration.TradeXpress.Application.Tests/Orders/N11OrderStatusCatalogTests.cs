using System;
using System.Globalization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="N11OrderStatusCatalog"/> — kalem/sipariş durum KODU → etiket (SOAP ref v4.6 GROUND TRUTH).
/// Kültüre göre tr/en; bilinmeyen kod ham değerle döner (fail-open — sipariş satırı asla boş etikete düşmez).</summary>
public class N11OrderStatusCatalogTests
{
    [Fact]
    public void Item_status_10_label_is_culture_aware()
    {
        WithCulture("tr", () => N11OrderStatusCatalog.ItemStatusLabel("10").ShouldBe("Tamamlandı"));
        WithCulture("en", () => N11OrderStatusCatalog.ItemStatusLabel("10").ShouldBe("Completed"));
    }

    [Fact]
    public void Item_status_11_matches_reference_text()
    {
        WithCulture("tr", () => N11OrderStatusCatalog.ItemStatusLabel("11").ShouldBe("İade İptal Değişim Talep Edildi"));
    }

    [Fact]
    public void Order_status_5_label_is_culture_aware()
    {
        WithCulture("tr", () => N11OrderStatusCatalog.OrderStatusLabel("5").ShouldBe("Tamamlandı"));
        WithCulture("en", () => N11OrderStatusCatalog.OrderStatusLabel("5").ShouldBe("Completed"));
    }

    [Fact]
    public void Payment_type_1_is_credit_card_culture_aware()
    {
        WithCulture("tr", () => N11OrderStatusCatalog.PaymentTypeLabel("1").ShouldBe("Kredi Kartı"));
        WithCulture("en", () => N11OrderStatusCatalog.PaymentTypeLabel("1").ShouldBe("Credit Card"));
    }

    [Fact]
    public void Payment_type_brand_names_are_language_agnostic()
    {
        // Marka/banka adları her iki dilde aynı.
        N11OrderStatusCatalog.PaymentTypeLabel("8").ShouldBe("MasterPass");
        N11OrderStatusCatalog.PaymentTypeLabel("10").ShouldBe("PAYCELL");
    }

    [Theory]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_code_returns_raw_value(string? raw)
    {
        N11OrderStatusCatalog.ItemStatusLabel(raw).ShouldBe(raw);
        N11OrderStatusCatalog.OrderStatusLabel(raw).ShouldBe(raw);
        N11OrderStatusCatalog.PaymentTypeLabel(raw).ShouldBe(raw);
    }

    private static void WithCulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
