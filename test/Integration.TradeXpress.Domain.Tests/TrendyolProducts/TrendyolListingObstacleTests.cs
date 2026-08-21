using System;
using Integration.TradeXpress.SalesChannelProducts;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// PAZARYERİ ENGEL BEYANININ kanal kaydına taşınması ve tek cevaba indirgenmesi.
///
/// <para><b>Sabitlenen boşluk:</b> <c>blacklisted</c>/<c>locked</c>/<c>archived</c>/<c>rejected</c> alanları
/// Trendyol yanıtında HEP vardı ve hiç okunmuyordu. Karalisteye alınmış bir kalem bizde "onaylı + satışta"
/// görünüyor, gönderim karşı tarafta reddediliyor ve sebebi hiçbir ekranda yer almıyordu. Canlı ölçüm bunun
/// teorik olmadığını gösterdi — tek bir grubun 19 kaleminin TAMAMI karalistedeydi, dördü ayrıca kilitli.</para>
/// </summary>
public class TrendyolListingObstacleTests
{
    private const string Barcode = "BR-1";

    [Fact]
    public void The_marketplace_obstacle_declaration_is_carried_onto_the_sku()
    {
        var product = NewProduct();

        product.UpsertImportedSku(Guid.NewGuid(), Barcode, "STK-1", 987L, new TrendyolRemoteListingState(
            Blacklisted: true,
            BlacklistReason: "Orijinallik şüphesi",
            Locked: true,
            LockReason: "UNSUPPLIED_PRODUCT",
            HasActiveCampaign: true,
            ProductUrl: "https://www.trendyol.com/x-p-1",
            UpdatedAtUtc: new DateTime(2026, 2, 9, 10, 0, 0, DateTimeKind.Utc)));

        var sku = product.Skus[0];
        sku.RemoteBlacklisted.ShouldBe(true);
        sku.RemoteBlacklistReason.ShouldBe("Orijinallik şüphesi");
        sku.RemoteLocked.ShouldBe(true);
        sku.RemoteHasActiveCampaign.ShouldBe(true);
        sku.RemoteProductUrl.ShouldBe("https://www.trendyol.com/x-p-1");
        sku.RemoteUpdatedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void The_heaviest_obstacle_wins_when_a_sku_carries_more_than_one()
    {
        // Canlı örnekte dört kalem hem karalistede HEM kilitliydi. İki gerekçeyi birden yazmak eylemi
        // bulanıklaştırır; önce ÇÖZÜLMESİ GEREKEN söylenir — karaliste belge süreci ister, kilit tedarik
        // sorunudur ve biri çözülmeden diğeri anlamsızdır.
        var product = NewProduct();
        product.UpsertImportedSku(Guid.NewGuid(), Barcode, "STK-1", null, new TrendyolRemoteListingState(
            Blacklisted: true, BlacklistReason: "Belge eksik", Locked: true, LockReason: "UNSUPPLIED_PRODUCT"));

        TrendyolListingObstacleResolver.Resolve(product.Skus[0]).ShouldBe(ChannelListingObstacle.Blacklisted);
        TrendyolListingObstacleResolver.ResolveReason(product.Skus[0]).ShouldBe("Belge eksik");
    }

    [Fact]
    public void A_record_is_not_obstacle_free_just_because_one_of_its_skus_is_clean()
    {
        // Tek kalemi engelli kayıt "engelsiz" sayılamaz: o kalem satılamıyorsa kullanıcının haberi olmalıdır.
        var product = NewProduct();
        product.UpsertImportedSku(Guid.NewGuid(), "BR-CLEAN", "STK-C", null, new TrendyolRemoteListingState(Quantity: 5));
        product.UpsertImportedSku(Guid.NewGuid(), "BR-LOCKED", "STK-L", null, new TrendyolRemoteListingState(Locked: true));

        TrendyolListingObstacleResolver.Resolve(product.Skus).ShouldBe(ChannelListingObstacle.Locked);
    }

    [Fact]
    public void An_unreported_flag_preserves_what_was_already_known()
    {
        // Kimlik-only çağrılar (yeniden-bağlama) daha önce okunmuş gerçek değerleri SESSİZCE silmemeli.
        // Bu, adet/fiyat alanlarında zaten kanıtlanmış bir kuraldı; engel bayrakları da aynı sözleşmede.
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.UpsertImportedSku(variantId, Barcode, "STK-1", null, new TrendyolRemoteListingState(Blacklisted: true, BlacklistReason: "Belge eksik"));

        product.UpsertImportedSku(variantId, Barcode, "STK-1", null);

        product.Skus[0].RemoteBlacklisted.ShouldBe(true);
        product.Skus[0].RemoteBlacklistReason.ShouldBe("Belge eksik");
    }

    [Fact]
    public void A_lifted_obstacle_clears_its_reason_instead_of_leaving_stale_text_on_screen()
    {
        // Bayrak açıkça KAPALI bildirildiyse gerekçe de düşer. Kalkmış bir karalistenin gerekçesini ekranda
        // bırakmak, çözülmüş bir sorunu yaşıyor göstermek olurdu.
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.UpsertImportedSku(variantId, Barcode, "STK-1", null, new TrendyolRemoteListingState(Blacklisted: true, BlacklistReason: "Belge eksik"));

        product.UpsertImportedSku(variantId, Barcode, "STK-1", null, new TrendyolRemoteListingState(Blacklisted: false));

        product.Skus[0].RemoteBlacklisted.ShouldBe(false);
        product.Skus[0].RemoteBlacklistReason.ShouldBeNull();
        TrendyolListingObstacleResolver.Resolve(product.Skus[0]).ShouldBe(ChannelListingObstacle.None);
    }

    [Fact]
    public void A_sku_the_marketplace_said_nothing_about_reports_no_obstacle()
    {
        // null = "bildirilmedi". Bunu ayrı bir duruma çevirmek bugün hiçbir kararı değiştirmezdi; ama
        // "bildirilmedi"yi ENGEL saymak, hiç import edilmemiş her kaydı engelli gösterirdi.
        var product = NewProduct();
        product.UpsertImportedSku(Guid.NewGuid(), Barcode, "STK-1", null);

        TrendyolListingObstacleResolver.Resolve(product.Skus[0]).ShouldBe(ChannelListingObstacle.None);
        TrendyolListingObstacleResolver.ResolveReason(product.Skus[0]).ShouldBeNull();
    }

    private static SalesChannelTrTrendyolProduct NewProduct()
    {
        return new SalesChannelTrTrendyolProduct(
            companyId: Guid.NewGuid(),
            salesChannelId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            productMainId: "URUN-1",
            sequenceNo: 1,
            categoryId: "411",
            brandId: "82");
    }
}
