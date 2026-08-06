using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Integration.TradeXpress.Orders;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// REZERVASYONDA ZAMAN AŞIMI YOKTUR — mekanik yasak (2026-08-05 Hakan kararı: <i>"sipariş siparıştir"</i>).
///
/// <para><b>Neden test:</b> "eski rezervasyonları temizle" fikri kendiliğinden makul görünür ve bir gün
/// birileri iyi niyetle bir <c>ExpireOldReservationsAsync</c> yazar. O gün, kullanıcının hiç görmediği bir
/// zamanlayıcı müşteriye ayrılmış madeni serbest bırakır ve aynı mal ikinci kez satılır. Kural yorumda kalırsa
/// unutulur; burada KIRMIZI yanar.</para>
///
/// <para>İstisna yolu: bu testi gevşetmek DEĞİL — böyle bir davranış gerçekten isteniyorsa önce kural
/// (CLAUDE.md §6) değişir, sonra test.</para>
/// </summary>
public class OrderReservationConventionTests
{
    private static readonly string[] BannedFragments =
    {
        "Expire", "Expiry", "Expiration", "Timeout", "TimedOut", "AutoRelease", "AutoCancel",
    };

    /// <summary>Rezervasyon ve bağ tiplerinde zaman-aşımı çağrıştıran ÜYE adı bulunamaz.</summary>
    [Fact]
    public void Reservation_types_must_not_declare_any_expiry_or_timeout_member()
    {
        var types = new[] { typeof(OrderReservation), typeof(OrderFulfillmentLink) };

        var violations = new List<string>();
        foreach (var type in types)
        {
            var members = type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                            | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name);

            violations.AddRange(
                from name in members
                from fragment in BannedFragments
                where name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                select $"{type.Name}.{name} (yasak parça: {fragment})");
        }

        violations.ShouldBeEmpty(
            "Rezervasyonda ZAMAN AŞIMI kavramı yoktur — 'sipariş siparıştir'. Otomatik serbest bırakma, "
            + "kullanıcının görmediği bir anda müşteriye ayrılmış malı yeniden satılabilir yapar. "
            + "Serbest bırakma DAİMA insan kararıdır (OrderCancellationDecision).");
    }

    /// <summary>İki eksen AYRI kalır: iptal kararı enum'unda stok durumu, stok enum'unda iptal kararı YOKTUR.
    /// <para>Birleştirmek "iptal talebi geldi → maden serbest" kestirmesine kapı açardı; oysa mal kesilmiş
    /// olabilir ve bunu yalnız kullanıcı bilir.</para></summary>
    [Fact]
    public void Stock_axis_and_cancellation_axis_stay_separate()
    {
        var stockNames = Enum.GetNames<OrderReservationStatus>();
        var cancelNames = Enum.GetNames<OrderCancellationDecision>();

        stockNames.ShouldNotContain("Cancelled");
        stockNames.ShouldNotContain("PendingCancellation");
        cancelNames.ShouldNotContain("Reserved");
        cancelNames.ShouldNotContain("Released");
    }

    /// <summary>Fiyat farkı NULLABLE kalmalı: <b>null = beyan edilmedi</b>, <b>0 = "fark yok" beyanı</b>.
    /// Non-nullable yapmak, hiç sorulmamış bir soruya sistemin "hayır" cevabı uydurması olurdu.</summary>
    [Fact]
    public void Price_difference_must_stay_nullable_to_separate_undeclared_from_zero()
    {
        var property = typeof(OrderFulfillmentLink).GetProperty(nameof(OrderFulfillmentLink.PriceDifference));

        property.ShouldNotBeNull();
        Nullable.GetUnderlyingType(property!.PropertyType).ShouldBe(typeof(decimal));
    }
}
