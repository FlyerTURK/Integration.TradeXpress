using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol SKU'sunun BEKLEYEN-İÇERİK yaşam döngüsü ve İMPORT-EKSEN semantiği — entity seviyesi ağ (bağımsız
/// denetim bulgusu: bu davranışlar yalnız uygulama zincirinin içinde dolaylı geçiyordu).
/// ① <c>RecordPendingSkuPush</c> içerik üçlüsünü (başlık/eksen/görsel) yazar; ② <c>PromotePendingSkuPushes</c>
/// fiyat/adet'i terfi eder ve içeriği TEMİZLER (içerik terfi edilmez — LastSent* yalnız dirty-check tabanıdır);
/// ③ <c>ClearPendingSkuPushes</c> üçünü de siler; ④ <c>UpsertImportedSku</c> eksen değerlerinde ÜÇ durumludur:
/// null = bildirilmedi (mevcut korunur), boş liste = "eksen yok" beyanı (temizler), dolu = yeni set;
/// ⑤ <c>ReconcileSkus</c> adayın varianter imzasını snapshot'a yazar, boş imzalı aday mevcut snapshot'ı EZMEZ.
/// </summary>
public class TrendyolSkuPendingContentAndAxisTests
{
    private const string Barcode = "BR-1";

    [Fact]
    public void Pending_content_is_recorded_promoted_for_numbers_only_and_cleared()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.UpsertImportedSku(variantId, Barcode, "STK-1", null);

        product.RecordPendingSkuPush(Barcode, 5, 120m, 100m,
            title: "Deri Kılıf", optionsText: "Renk=Kırmızı", mediaIdsCsv: "aaaa,bbbb");
        var sku = product.Skus[0];
        sku.PendingSentTitle.ShouldBe("Deri Kılıf");
        sku.PendingSentOptions.ShouldBe("Renk=Kırmızı");
        sku.PendingSentMediaIds.ShouldBe("aaaa,bbbb");

        product.PromotePendingSkuPushes();
        sku.LastSentQuantity.ShouldBe(5);
        sku.LastSentListPrice.ShouldBe(120m);
        sku.LastSentSalePrice.ShouldBe(100m);
        sku.PendingSentTitle.ShouldBeNull();      // içerik terfi ETMEZ, temizlenir
        sku.PendingSentOptions.ShouldBeNull();
        sku.PendingSentMediaIds.ShouldBeNull();
        sku.PendingSentQuantity.ShouldBeNull();

        product.RecordPendingSkuPush(Barcode, 7, null, null, title: "X", optionsText: "Y", mediaIdsCsv: "z");
        product.ClearPendingSkuPushes();
        sku.PendingSentQuantity.ShouldBeNull();
        sku.PendingSentTitle.ShouldBeNull();
        sku.PendingSentOptions.ShouldBeNull();
        sku.PendingSentMediaIds.ShouldBeNull();
        sku.LastSentQuantity.ShouldBe(5);         // temizlik LastSent*'e DOKUNMAZ
    }

    [Fact]
    public void Imported_axis_values_are_three_state()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        var red = new SalesChannelTrTrendyolProductSkuRemoteAxisValue(47, 686234, "Kırmızı", "Renk");

        product.UpsertImportedSku(variantId, Barcode, "STK-1", null,
            new TrendyolRemoteListingState(AxisValues: new[] { red }));
        product.Skus[0].RemoteVariantAttributes.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);

        // null = bildirilmedi → mevcut KORUNUR (kimlik-only yeniden-bağlama çağrısı fotoğrafı silmesin).
        product.UpsertImportedSku(variantId, Barcode, "STK-1", null, new TrendyolRemoteListingState(Quantity: 3));
        product.Skus[0].RemoteVariantAttributes.ShouldHaveSingleItem();

        // boş liste = "eksen yok" BEYANI → temizler (grup tekilleşince bayat "Renk" push'a gitmesin).
        product.UpsertImportedSku(variantId, Barcode, "STK-1", null,
            new TrendyolRemoteListingState(AxisValues: Array.Empty<SalesChannelTrTrendyolProductSkuRemoteAxisValue>()));
        product.Skus[0].RemoteVariantAttributes.ShouldBeEmpty();
    }

    [Fact]
    public void Reconcile_freezes_the_varianter_signature_and_an_empty_candidate_signature_keeps_the_known_one()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        var signature = new List<SalesChannelTrTrendyolProductSkuAttribute> { new(47, 686234) };

        var first = product.ReconcileSkus(new[] { new TrendyolSkuPushCandidate(variantId, "VAR-1", signature) });
        first[variantId].AttributeSnapshot.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);

        // Boş imzalı ikinci reconcile (önizleme/imza üretilmemiş) bilinen imzayı SİLMEZ.
        product.ReconcileSkus(new[] { new TrendyolSkuPushCandidate(variantId, "VAR-1", Array.Empty<SalesChannelTrTrendyolProductSkuAttribute>()) });
        product.Skus[0].AttributeSnapshot.ShouldHaveSingleItem();

        // İmza yeniden-bağlamanın 3. aşaması artık ÇALIŞIR: varyant kimliği ve kodu değişmiş ama aynı
        // "Renk=Kırmızı" imzasını taşıyan aday, dondurulmuş barkodlu satıra bağlanır (yeni satır AÇILMAZ).
        var regenerated = Guid.NewGuid();
        var rebound = product.ReconcileSkus(new[] { new TrendyolSkuPushCandidate(regenerated, "VAR-1-YENI", signature) });
        product.Skus.Count.ShouldBe(1);
        rebound[regenerated].Barcode.ShouldBe(product.Skus[0].Barcode);
        product.Skus[0].ProductVariantId.ShouldBe(regenerated);
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
