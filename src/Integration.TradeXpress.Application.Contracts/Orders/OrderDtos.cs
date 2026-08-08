using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Orders;

/// <summary>Sipariş liste sorgusu (per-tenant, company-owned) — sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ). Merkezi <see cref="ListRequestDto"/> standardı: kanal/durum/tarih filtreleri
/// whitelist üzerinden <c>Filters</c> ile uygulanır.</summary>
public class OrderListRequestDto : ListRequestDto
{
}

/// <summary>Ortak sipariş paneli grid satırı — TÜM kanalların siparişleri (base sorgu). Kanal yalnız discriminator
/// (<see cref="ChannelType"/> + enrich edilmiş <see cref="SalesChannelCode"/>). Satırlar SNAPSHOT'tan çizilir.</summary>
public class OrderListDto : EntityDto<Guid>, IListDto<Guid>
{
    public Guid SalesChannelId { get; set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + filtre.</summary>
    public SalesChannelType ChannelType { get; set; }

    /// <summary>Kanal kodu — AppService'te enrich edilir (id-only referanstan; mapper doldurmaz).</summary>
    public string? SalesChannelCode { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus NeutralStatus { get; set; }
    public string? RemoteStatus { get; set; }
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public string? CargoProvider { get; set; }
    public string? CargoTrackingNumber { get; set; }
    public DateTime FetchedAt { get; set; }

    /// <summary>Siparişin KALEMLERİ — master-detail DETAIL grid'i (satırı genişletince). AppService enrich eder
    /// (Mapperly DEĞİL; ayrı repo + zengin detay). Her kalem ürün/adet/fiyat + komisyon/indirim/kargo/nitelik.</summary>
    public List<OrderItemListDto> Items { get; set; } = new();
}

/// <summary>Sipariş satırı görünümü — ürün-agnostik SNAPSHOT (yerel ürün olmasa da tam anlamlı).</summary>
public class OrderLineDto
{
    public Guid Id { get; set; }
    public string? RemoteLineId { get; set; }
    public string? Barcode { get; set; }
    public string? StockCode { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? RemoteLineStatus { get; set; }

    /// <summary>Yerel varyant bağı — id-only OPSİYONEL zenginleştirme (null = yerel eşleşme yok; NORMAL durum).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Türetilmiş görüntü bayrağı: yerel ürüne navigasyon sunulabilir mi (aksi halde "yerel ürün yok" ipucu).</summary>
    public bool HasLocalVariant => ProductVariantId.HasValue;
}

/// <summary>MELEZ sipariş-kalemi satırı — ortak sipariş panelinin ANA (standart) grid satırı: her satır bir orderItem,
/// ait olduğu siparişin başlığıyla (no/müşteri/tarih/kanal/tutar/durum) zenginleştirilir. Sıralama: order status →
/// item status → tarih (yeni→eski). <see cref="EntityDto{Guid}.Id"/> = OrderLine id (grid satır anahtarı). Line
/// alanları Mapperly ile OrderLine'dan; order alanları ikinci Mapperly + kanal kodu enrich ile.</summary>
public class OrderItemListDto : EntityDto<Guid>, IListDto<Guid>
{
    // ── Order başlığı (satır bağlamı) — AppService enrich/map eder ──
    public Guid OrderId { get; set; }
    public Guid SalesChannelId { get; set; }
    public SalesChannelType ChannelType { get; set; }
    public string? SalesChannelCode { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus NeutralStatus { get; set; }
    public string? RemoteStatus { get; set; }
    public string? CustomerName { get; set; }
    public decimal OrderTotalAmount { get; set; }
    public string? CargoProvider { get; set; }
    public string? CargoTrackingNumber { get; set; }

    // ── OrderItem (kalem) — Mapperly OrderLine'dan doldurur (Id = OrderLine.Id) ──
    public string? Barcode { get; set; }
    public string? StockCode { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? RemoteLineStatus { get; set; }
    public Guid? ProductVariantId { get; set; }

    /// <summary>Kalem ZENGİN detayı (getOrderDetail snapshot'ından; RemoteLineId eşleşmesiyle AppService enrich eder,
    /// Mapperly DEĞİL) — master-detail satırında bu KALEME özel "ufak detaylar". null = detay çekilmedi.</summary>
    public OrderItemDetailDto? ItemDetail { get; set; }
}

/// <summary>Bir sipariş KALEMİNİN master-detail satırında gösterilen ufak zengin detayı — getOrderDetail item
/// projeksiyonundan (o kaleme özel: komisyon/indirim/kargo/nitelik). Tam kalem detayı (barkod/tarihler/tutarlar)
/// popup'ta <see cref="OrderDetailItemDto"/> üzerinden gösterilir — burada TEKRARLANMAZ. Salt-okuma.</summary>
public class OrderItemDetailDto
{
    public string? SkuId { get; set; }
    public decimal? Commission { get; set; }

    /// <summary>N11 + mağaza indirimi toplamı.</summary>
    public decimal? DiscountTotal { get; set; }
    public string? ShipmentCompany { get; set; }

    /// <summary>Kargo yöntemi ham kodu: 1 Kargo · 2 Diğer.</summary>
    public int? ShipmentMethod { get; set; }

    /// <summary>Kalem nitelikleri düz metin (ör. "Renk: Sarı, Ayar: 14K").</summary>
    public string? Attributes { get; set; }
}

/// <summary>Alıcının girdiği özel metin (getOrderDetail item.customTextOptionValues) — seçenek adı + değeri.</summary>
public class OrderCustomTextDto
{
    /// <summary>Seçenek adı (ör. "mürekkep rengi", "yazılacak yazı").</summary>
    public string? Option { get; set; }

    /// <summary>Alıcının girdiği değer (çok satırlı olabilir).</summary>
    public string? Text { get; set; }
}

/// <summary>Sipariş EDİT formunun modeli (standart <c>EntityEditForm</c>/<c>CrudEditHost</c> sözleşmesi —
/// <see cref="IGetDto{TKey}"/>). Başlık/totals SALT-OKUMA referans (N11'den — <see cref="Detail"/>); Buyer/Fatura/
/// Teslimat adresi/Kargo EDİTABLE (değer = düzeltme varsa düzeltme, yoksa orijinal — <see cref="IOrderAppService.GetAsync"/>
/// projeksiyonu). Kaydedilen SADECE <c>OrderOperationalData</c>'ya yazılır; orijinal <see cref="Detail"/> HİÇBİR ZAMAN
/// değişmez (denetim kanıtı). Create/Delete YOK (Order yalnız senkronizasyondan gelir) — edit host bunları gizler.</summary>
public class OrderDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid SalesChannelId { get; set; }
    public SalesChannelType ChannelType { get; set; }
    public string? SalesChannelCode { get; set; }
    public string RemoteOrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus NeutralStatus { get; set; }
    public string? RemoteStatus { get; set; }
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? CurrencyUnitId { get; set; }
    public DateTime FetchedAt { get; set; }
    public List<OrderLineDto> Lines { get; set; } = new();

    /// <summary>Fatura/Teslimat adres picker'ının KİLİTLİ ülkesi (TR) — sipariş adresleri Türkiye kabul edilir.
    /// AppService, host coğrafya kataloğundan TR ülke id'sini çözer (Code=="TR"); adres alanları bu id'yi
    /// <c>AddressFields.FixedCountryId</c> olarak alır (ülke combo'su gizli, cascade İl'den başlar). TR kataloğu
    /// yoksa null (picker serbest-ülke moduna düşer). Sipariş entity'sinde YOK → Mapperly'de ignore, AppService doldurur.</summary>
    public Guid? CountryId { get; set; }

    /// <summary>Hâlâ N11'e Kabul/Red bildirilmemiş (Sipariş Fazı O2 — ActionStatus=Pending) kalem sayısı — yalnız
    /// N11 kanalında dolu (diğerlerinde 0). Edit formu toolbar'ındaki Kabul Et/Reddet butonlarının GÖRÜNÜRLÜĞÜ
    /// bunu okur: 0 ise (tüm kalemler zaten işlem görmüş) butonlar HİÇ gösterilmez.</summary>
    public int PendingLineCount { get; set; }

    /// <summary>ZENGİN detay (N11 getOrderDetail projeksiyonu, SALT-OKUMA referans) — tutar kırılımı/kalem
    /// komisyon+nitelik + orijinal Buyer/Adres (denetim amaçlı). null = detay henüz çekilmedi.</summary>
    public OrderDetailSnapshotDto? Detail { get; set; }

    // ── Editable (değer = düzeltme ?? orijinal; kaydedilen OrderOperationalData'ya gider) ──
    public string? CargoProvider { get; set; }
    public string? CargoTrackingNumber { get; set; }
    public OrderEditPartyDto Buyer { get; set; } = new();
    public OrderEditAddressDto BillingAddress { get; set; } = new();
    public OrderEditAddressDto ShippingAddress { get; set; } = new();

    // ── UI-ONLY sipariş-düzeyi toplu aksiyon girdisi (kaydedilmez — UpdateAsync YOK sayar; yalnız edit formu
    // toolbar'ındaki Kabul Et isteğini doldurmak için tutulur — OrderLineEditDto ActionInputNumberOfPackages ile
    // AYNI desen). Reddet'in gerekçesi burada YOK — tıklanınca UiService.PromptAsync ile SORULUR (önceden
    // doldurulan bir alana güvenilmez).
    public int ActionInputNumberOfPackages { get; set; } = 1;
}

/// <summary>Sipariş ZENGİN detay DTO'su (owned VO <c>OrderDetailSnapshot</c>'ın yansıması) — DETAY popup'ının kaynağı.
/// Alanlar VO ile BİREBİR (Mapperly nested auto-map). Salt-okuma.</summary>
public class OrderDetailSnapshotDto
{
    public OrderDetailPartyDto? Buyer { get; set; }
    public OrderDetailAddressDto? BillingAddress { get; set; }
    public OrderDetailAddressDto? ShippingAddress { get; set; }

    /// <summary>Fatura tipi ham kodu: 1 Bireysel · 2 Kurumsal.</summary>
    public int? InvoiceType { get; set; }
    public string? PaymentType { get; set; }
    public string? CitizenshipId { get; set; }
    public OrderDetailTotalsDto? Totals { get; set; }
    public List<OrderDetailItemDto> Items { get; set; } = new();
    public DateTime FetchedAt { get; set; }
}

/// <summary>Siparişi veren alıcı (PII snapshot).</summary>
public class OrderDetailPartyDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? TcId { get; set; }
    public string? TaxId { get; set; }
    public string? TaxOffice { get; set; }
}

/// <summary>Sipariş adresi (fatura/teslimat) — tolerant snapshot.</summary>
public class OrderDetailAddressDto
{
    public string? FullName { get; set; }
    public string? Line { get; set; }
    public string? Neighborhood { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? Gsm { get; set; }
    public string? TcId { get; set; }
    public string? TaxId { get; set; }
    public string? TaxOffice { get; set; }
}

/// <summary>Sipariş tutar kırılımı (N11 billingTemplate).</summary>
public class OrderDetailTotalsDto
{
    public decimal? OriginalPrice { get; set; }
    public decimal? DueAmount { get; set; }
    public decimal? SellerInvoiceAmount { get; set; }
    public decimal? TotalMallDiscountPrice { get; set; }
    public decimal? TotalSellerDiscount { get; set; }
    public decimal? TotalServiceItemOriginalPrice { get; set; }
}

/// <summary>Sipariş kalemi zengin detayı (komisyon/indirim/kargo/nitelik) — DETAY popup grid'inin kaynağı. TAM kargo
/// bilgisi (takip/kampanya) kalem→DETAIL genişletmede; özel metinler (customTextOptionValues) kimlik kolonunda.
/// Alanlar VO <c>OrderDetailItem</c> ile birebir (Mapperly nested auto-map).</summary>
public class OrderDetailItemDto
{
    public string? RemoteLineId { get; set; }
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductSellerCode { get; set; }
    public string? SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? Commission { get; set; }
    public decimal? DueAmount { get; set; }
    public decimal? MallDiscount { get; set; }
    public decimal? SellerDiscount { get; set; }
    public decimal? SellerInvoiceAmount { get; set; }
    public string? Status { get; set; }
    public DateTime? ApproveDate { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public string? ShipmentCompany { get; set; }

    /// <summary>Kargo yöntemi ham kodu: 1 Kargo · 2 Diğer.</summary>
    public int? ShipmentMethod { get; set; }
    public string? ShipmentCode { get; set; }

    /// <summary>Kargo firması N11 id'si (shipmentInfo.shipmentCompany.id).</summary>
    public string? ShipmentCompanyId { get; set; }

    /// <summary>Kargo firması kısa adı (ör. "YK").</summary>
    public string? ShipmentCompanyShortName { get; set; }

    /// <summary>Kargo takip numarası (shipmentInfo.trackingNumber) — kalem→DETAIL genişletmede gösterilir.</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>N11 kampanya numarası (shipmentInfo.campaignNumber).</summary>
    public string? CampaignNumber { get; set; }

    /// <summary>Kampanya numarası durumu (shipmentInfo.campaignNumberStatus).</summary>
    public string? CampaignNumberStatus { get; set; }

    public List<OrderDetailItemAttributeDto> Attributes { get; set; } = new();

    /// <summary>Alıcının girdiği ÖZEL METİNLER (customTextOptionValues) — kaşe/mühür metni, mürekkep rengi.
    /// Kimlik kolonunda (SellerCode/Title/Variant/CustomText) gösterilir. Boş liste = özel metin yok.</summary>
    public List<OrderCustomTextDto> CustomTexts { get; set; } = new();
}

/// <summary>Sipariş kalemi niteliği (ad/değer).</summary>
public class OrderDetailItemAttributeDto
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}

/// <summary>Editable alıcı bilgisi — <see cref="OrderDetailPartyDto"/> ile AYNI alan seti (düzeltme amaçlı).</summary>
public class OrderEditPartyDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? TcId { get; set; }
    public string? TaxId { get; set; }
    public string? TaxOffice { get; set; }
}

/// <summary>Editable adres — <see cref="OrderDetailAddressDto"/> alan setini <see cref="IAddressEditModel"/> ile
/// düzenlenebilir kılar (Fatura/Teslimat için TEK paylaşılan şekil; ortak <c>AddressFields</c> picker'ına bind eder).
/// <para>Adres kısmı (City/Line/District/Neighborhood/PostalCode + kodlar + UBL) <see cref="IAddressEditModel"/>'den;
/// geo-ref id'leri (<see cref="AdministrativeAreaId"/>/<see cref="LocalityId"/>) YALNIZ picker'ı ÖN-SEÇMEK için
/// geçicidir — order'a PERSIST EDİLMEZ (kalıcı taraf isim-tabanlı kalır; bkz. <c>OrderOperationalAddress</c>).
/// Alıcı-KİMLİK alanları (<see cref="FullName"/>/<see cref="Gsm"/>/<see cref="TcId"/>/<see cref="TaxId"/>/
/// <see cref="TaxOffice"/>) adres DEĞİL — ayrı taşınır (formda AddressFields'ten ayrı editörler).</para></summary>
public class OrderEditAddressDto : IAddressEditModel
{
    // ── Alıcı-kimlik (adres değil; AddressFields'ten ayrı editörler) ──
    public string? FullName { get; set; }
    public string? Gsm { get; set; }
    public string? TcId { get; set; }
    public string? TaxId { get; set; }
    public string? TaxOffice { get; set; }

    // ── Adres (IAddressEditModel — ortak AddressFields picker'ı doldurur) ──
    public string? Title { get; set; }
    public string City { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Neighborhood { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    /// <summary>Ülke ADI — salt görüntü (adres özetinde kod yerine "Türkiye"). Otoriter alan CountryCode'dur.</summary>
    public string? CountryName { get; set; }
    public string? CityCode { get; set; }
    public string? DistrictCode { get; set; }

    /// <summary>Picker ÖN-SEÇİMİ için geçici çekirdek il id'si (isim-eşleşmesinden) — order'a persist EDİLMEZ.</summary>
    public Guid? AdministrativeAreaId { get; set; }

    /// <summary>Picker ÖN-SEÇİMİ için geçici çekirdek ilçe id'si (isim-eşleşmesinden) — order'a persist EDİLMEZ.</summary>
    public Guid? LocalityId { get; set; }

    public string? AdministrativeAreaIsoCode { get; set; }
    public string? BuildingName { get; set; }
    public string? BuildingNumber { get; set; }
    public string? Room { get; set; }
    public string? Floor { get; set; }
    public string? Postbox { get; set; }
    public string? AdditionalStreetName { get; set; }
}

/// <summary>Sipariş kaleminin düzenleme satırı (<c>OrderItemsDrill</c> — DrillList tabanlı UI bileşeni — kaynağı).
/// Salt-okuma referans alanlar (N11'den — ne sipariş edildi, DEĞİŞTİRİLEMEZ) + editable alanlar (customText
/// düzeltmesi, manuel ürün versiyonu eşleştirmesi).</summary>
public class OrderLineEditDto
{
    /// <summary>Kanal satır kimliği — drill anahtarı (OrderLine.Id DEĞİL; resync'te KARARLI kalır).</summary>
    public string RemoteLineId { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    // ── Salt-okuma referans (N11 getOrderDetail'den — ne sipariş edildi) ──
    public string? ProductName { get; set; }
    public string? ProductSellerCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? Commission { get; set; }
    public decimal? DiscountTotal { get; set; }
    public string? Status { get; set; }
    public string? Attributes { get; set; }
    public string? ShipmentCompany { get; set; }
    public string? TrackingNumber { get; set; }

    /// <summary>Alıcının özel metinlerine (customText) operatör düzeltmesi — editable.</summary>
    public List<OrderLineCustomTextEditDto> CustomTexts { get; set; } = new();

    /// <summary>Manuel ürün versiyonu eşleştirmesi — editable (otomatik eşleşme de aynı alanı doldurur).</summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Eşleşme anındaki isim/görsel — salt-okuma (ProductVariantId değişince AppService yeniden hesaplar).</summary>
    public string? ProductSnapshotName { get; set; }
    public string? ProductSnapshotImageUrl { get; set; }
    public DateTime? MatchedAt { get; set; }

    // ── Sipariş Fazı O2 — state machine (N11'e YAZILAN aksiyonlar; salt-okuma referans, aksiyonlar ayrı uçlarla) ──

    /// <summary>Kalemin yerel işlem durumu — Pending→Accepted|Rejected→(Accepted'ten)Shipped.</summary>
    public OrderLineActionStatus ActionStatus { get; set; }

    /// <summary>Red gerekçesi — yalnız ActionStatus=Rejected iken dolu.</summary>
    public string? RejectReason { get; set; }

    /// <summary>Son aksiyonun N11'e bildirildiği an (UTC).</summary>
    public DateTime? ActionAt { get; set; }

    // ── UI-ONLY geçici aksiyon girdisi (kaydedilmez — SaveOrderLineEditAsync YOK sayar; yalnız Accept/Reject/Ship
    // isteklerini doldurmak için drill formunda tutulur) ──
    public int ActionInputNumberOfPackages { get; set; } = 1;
    public string? ActionInputRejectReason { get; set; }
    public string? ActionInputShipmentCompanyId { get; set; }
    public string? ActionInputTrackingNumber { get; set; }
    public string? ActionInputCampaignNumber { get; set; }
    public int ActionInputShipmentMethod { get; set; } = 1;
}

/// <summary>Bir kalemi N11'e KABUL olarak bildirme isteği (SOAP OrderItemAccept — GERÇEK, geri alınamaz).</summary>
public class OrderLineAcceptDto
{
    public Guid OrderId { get; set; }
    public string RemoteLineId { get; set; } = string.Empty;

    /// <summary>Kaç paket olarak gönderileceği (N11 zorunlu alanı) — varsayılan 1.</summary>
    public int NumberOfPackages { get; set; } = 1;
}

/// <summary>Bir kalemi N11'e RED olarak bildirme isteği (SOAP OrderItemReject — GERÇEK, geri alınamaz).</summary>
public class OrderLineRejectDto
{
    public Guid OrderId { get; set; }
    public string RemoteLineId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Bir kalemin kargo bilgisini N11'e bildirme isteği (SOAP MakeOrderItemShipment — GERÇEK, geri alınamaz).</summary>
public class OrderLineShipDto
{
    public Guid OrderId { get; set; }
    public string RemoteLineId { get; set; } = string.Empty;

    /// <summary>N11'in kendi kargo firması id'si (ör. "344"=Yurtiçi Kargo, "341"=Sürat Kargo).</summary>
    public string ShipmentCompanyId { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? CampaignNumber { get; set; }

    /// <summary>Kargo yöntemi ham kodu: 1 Kargo · 2 Diğer.</summary>
    public int ShipmentMethod { get; set; } = 1;
}

/// <summary>Siparişin TÜM bekleyen (Pending) kalemlerini N11'e TEK istekte KABUL olarak bildirme isteği (edit formu
/// toolbar'ındaki "Kabul Et" — SOAP OrderItemAccept, id-listesi TEK çağrı — GERÇEK, geri alınamaz).</summary>
public class OrderAcceptDto
{
    public Guid OrderId { get; set; }

    /// <summary>Kaç paket olarak gönderileceği (N11 zorunlu alanı, TÜM kalemler için TEK değer) — varsayılan 1.</summary>
    public int NumberOfPackages { get; set; } = 1;
}

/// <summary>Siparişin TÜM bekleyen (Pending) kalemlerini N11'e TEK istekte RED olarak bildirme isteği (edit formu
/// toolbar'ındaki "Reddet" — SOAP OrderItemReject, id-listesi TEK çağrı — GERÇEK, geri alınamaz).</summary>
public class OrderRejectDto
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Sipariş-düzeyi toplu aksiyonun (Kabul Et/Reddet) sonucu — kaç kalem etkilendi (0 = zaten bekleyen kalem
/// yoktu; UI'ya bilgilendirici mesaj için).</summary>
public class OrderBulkActionResultDto
{
    public int AffectedCount { get; set; }
}

/// <summary>Bir customText seçeneğine operatör düzeltmesi — <see cref="OrderCustomTextDto.Option"/> + orijinal metin
/// (salt-okuma referans) + düzeltme (editable; boş = düzeltme yok, orijinal geçerli).</summary>
public class OrderLineCustomTextEditDto
{
    public string Option { get; set; } = string.Empty;
    public string? OriginalText { get; set; }
    public string? CorrectedText { get; set; }
}

/// <summary>Sipariş çekim SONUÇ RAPORU — sessiz geçilmez: çekilen / yeni / güncellenen sipariş sayıları + uyarılar
/// (ör. TRY para birimi çözülemedi, anahtarsız uzak kayıt atlandı) ekranda gösterilir (komisyon import raporu deseni).</summary>
public class OrderFetchResultDto
{
    /// <summary>Pazaryerinden çekilen toplam sipariş (paket) sayısı.</summary>
    public int FetchedOrders { get; set; }

    /// <summary>Bu çekimde üretilen YENİ sipariş sayısı.</summary>
    public int NewOrders { get; set; }

    /// <summary>Mevcut olup GÜNCELLENEN sipariş sayısı (idempotent ikinci çekim).</summary>
    public int UpdatedOrders { get; set; }

    /// <summary>Çekilen toplam sipariş SATIRI sayısı.</summary>
    public int TotalLines { get; set; }

    /// <summary>DELTA turunda detayı TAZELENEN açık sipariş sayısı.
    /// <para>Ayrı sayılır: liste penceresine düşmeyen ama statüsü değişmiş olabilecek siparişler bu yoldan
    /// kontrol edilir — sayı 0 kalıyorsa açık sipariş yok ya da tazeleme çalışmıyor demektir.</para></summary>
    public int RefreshedOrders { get; set; }

    /// <summary>İşlenen kanal sayısı (toplu çekimde &gt; 1).</summary>
    public int ChannelsProcessed { get; set; }

    /// <summary>Çekim penceresinin BAŞLANGICI (UTC) — siparişler bu tarihten şimdiye kadar tarandı. Şeffaflık:
    /// pazaryerinin döndürdüğü tarih aralığı sessizce daraltılmaz; kullanıcı hangi geçmişin tarandığını görür.</summary>
    public DateTime? FetchedSinceUtc { get; set; }

    /// <summary>Uyarılar (LOKALİZE) — anahtarsız uzak kayıt atlandı / TRY çözülemedi vb. (sessiz geçilmez).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// NÖTR sipariş uygulaması — ortak sipariş paneli (tüm kanallar tek grid) + pazaryerinden SALT-OKUMA çekim (O0) +
/// YEREL operasyonel düzeltme katmanı (Sipariş Fazı O1: Buyer/adres/kargo/customText düzeltmesi + ürün versiyonu
/// bağı — <see cref="OrderOperationalData"/>/<see cref="OrderLineOperationalData"/>). FİŞ YOK, REZERVASYON YOK,
/// STOK HAREKETİ YOK, pazaryerine YAZMA YOK — düzeltmeler yalnız BİZİM tarafımızda yaşar, N11'e hiç gönderilmez.
/// Company-owned (sunucu <c>ICurrentCompany</c> zorlar).
/// </summary>
public interface IOrderAppService : IApplicationService
{
    /// <summary>Ortak sipariş listesi — MASTER-DETAIL: satır = SİPARİŞ (master), genişletince o siparişin KALEMLERİ
    /// (<see cref="OrderListDto.Items"/> = detail grid). Server-side sayfalı (ORDER düzeyinde). Sıralama: order status →
    /// tarih (yeni→eski). Standart grid (CrudLayout, master-detail).</summary>
    Task<PagedResultDto<OrderListDto>> GetListAsync(OrderListRequestDto input);

    /// <summary>Bir siparişin edit-hazır görünümü (başlık/totals referans + Buyer/Adres/Kargo editable — değer =
    /// düzeltme ?? orijinal). Standart <c>CrudEditHost</c> akışı bunu kullanır.</summary>
    Task<OrderDto> GetAsync(Guid id);

    /// <summary>Sipariş edit formunu kaydeder — SADECE OrderOperationalData'ya yazar (orijinal Order.Detail değişmez).
    /// Taze (güncel) OrderDto döner (ICommitCoordinator sözleşmesi).</summary>
    Task<OrderDto> UpdateAsync(Guid id, OrderDto input);

    /// <summary>Bir Trendyol kanalının siparişlerini pazaryerinden çeker (salt GET) → nötr Order'a idempotent upsert.
    /// İkinci çağrı dublike üretmez; durumu/satırları günceller.
    /// <para>⚠ Çekim zinciri REZERVASYONU tetikler (dolayısıyla fiş + stok) — eski "fiş/rezervasyon/stok'a hiç
    /// dokunmaz" vaadi Faz 7'den beri geçersizdir.</para></summary>
    Task<OrderFetchResultDto> FetchOrdersAsync(Guid salesChannelId);

    /// <summary>Şirketin TÜM bağlı kanallarının (Trendyol + N11) siparişlerini çeker (tek düğme; kanal-başına dolaşma
    /// yok) — sonuç raporu birleştirilir.</summary>
    Task<OrderFetchResultDto> FetchAllOrdersAsync();

    /// <summary>Bir siparişin kalemlerini DÜZENLEME satırları olarak döner (OrderItemsDrill kaynağı).</summary>
    Task<List<OrderLineEditDto>> GetOrderLineEditsAsync(Guid orderId);

    /// <summary>Bir kalemin düzenlemesini kaydeder (customText düzeltmesi + manuel ürün eşleştirmesi) — DrillList
    /// PersistUpdate callback'i. Güncel satırı (yeniden hesaplanmış snapshot ile) döner.</summary>
    Task<OrderLineEditDto> SaveOrderLineEditAsync(OrderLineEditDto input);

    // ── Sipariş Fazı O2 — state machine aksiyonları (N11'e YAZAR — GERÇEK, geri alınamaz; yalnız N11 kanalı) ──

    /// <summary>Kalemi N11'e KABUL olarak bildirir (SOAP OrderItemAccept). Guard: Pending olmalı, kanal N11 olmalı.</summary>
    Task<OrderLineEditDto> AcceptOrderLineAsync(OrderLineAcceptDto input);

    /// <summary>Kalemi N11'e RED olarak bildirir (SOAP OrderItemReject). Guard: Pending olmalı, kanal N11 olmalı.</summary>
    Task<OrderLineEditDto> RejectOrderLineAsync(OrderLineRejectDto input);

    /// <summary>Kalemin kargo bilgisini N11'e bildirir (SOAP MakeOrderItemShipment). Guard: Accepted olmalı, kanal N11 olmalı.</summary>
    Task<OrderLineEditDto> ShipOrderLineAsync(OrderLineShipDto input);

    /// <summary>Siparişin TÜM bekleyen kalemlerini TEK N11 isteğiyle KABUL eder (edit formu toolbar'ı — "Kabul Et").
    /// Bekleyen kalem yoksa no-op (AffectedCount=0). Guard: kanal N11 olmalı.</summary>
    Task<OrderBulkActionResultDto> AcceptOrderAsync(OrderAcceptDto input);

    /// <summary>Siparişin TÜM bekleyen kalemlerini TEK N11 isteğiyle RED eder (edit formu toolbar'ı — "Reddet").
    /// Bekleyen kalem yoksa no-op (AffectedCount=0). Guard: kanal N11 olmalı.</summary>
    Task<OrderBulkActionResultDto> RejectOrderAsync(OrderRejectDto input);

    // ── Sipariş Fazı O3 — REZERVASYON (Faz 7). Stoğa dokunur; N11'e YAZMAZ. ─────────────────────────

    /// <summary>Siparişin rezervasyon durumu — edit formundaki salt-okuma grubu + gelen kutusu kaynağı.
    /// null = bu sipariş için rezervasyon kaydı hiç açılmamış.</summary>
    Task<OrderReservationDto?> GetReservationAsync(Guid orderId);

    /// <summary>İptal talebine KARAR verir (2026-08-05 Hakan kararı: hiçbir iptal otomatik değildir).
    /// <para><b>Onay</b> rezervasyonu serbest bırakır → stok yeniden satılabilir olur.
    /// <b>Red</b> rezervasyonu tutmaya devam eder. Çıkış YAPILMIŞSA onay bloklanır (artık iade sürecidir).</para></summary>
    Task<OrderReservationDto> DecideCancellationAsync(OrderCancellationDecisionDto input);

    /// <summary>Rezervasyonu ELLE serbest bırakır (iptal talebi olmadan — operatör kararı). Fiş satırları
    /// soft-delete edilir: sayaçtan düşer, denetim izi kalır.</summary>
    Task<OrderReservationDto> ReleaseReservationAsync(OrderReservationReleaseDto input);

    /// <summary>Bir sipariş kalemini ELLE eşleştirmek için ürün varyantı adayları (aramalı; çalışılan şirket).
    ///
    /// <para><b>Neden elle yol şart:</b> otomatik eşleştirme kanal ürününün SKU sözlüğüne bakar; canlıda tek
    /// N11 kanal ürününün SKU listesi BOŞ olduğu için 126 kalemin HİÇBİRİ eşleşmiyor. Eşleşme olmadan
    /// rezervasyon kurulamaz ve her sipariş <c>Blocked</c>'da kilitli kalır.</para>
    ///
    /// <para>Elle eşleştirme <c>SaveOrderLineEditAsync</c> ile aynı kayda yazar; sonraki senkron turu onu
    /// EZMEZ (insert-only-if-missing) ve rezervasyon kendiliğinden kurulur — ayrı "yeniden dene" gerekmez.</para></summary>
    Task<List<OrderLineMatchCandidateDto>> GetLineMatchCandidatesAsync(OrderLineMatchCandidateRequestDto input);

    /// <summary>Rezervasyonu FİZİKİ ÇIKIŞA çevirir — hazırlayan kasa malı gerçekten çıkarır.
    ///
    /// <para><b>DÖNÜŞÜ OLMAYAN NOKTA:</b> bundan sonra iptal REDDEDİLİR (artık iade sürecidir) ve rezervasyon
    /// serbest bırakılamaz. Entity guard'ları bunu zaten kilitler; uç yalnız orkestre eder.</para>
    ///
    /// <para><b>ÇİFT SAYIM YASAK:</b> fiziki çıkış satırları yazılırken rezervasyon satırları AYNI işlemde
    /// soft-delete edilir. Biri yapılıp diğeri unutulursa aynı mal iki kez düşer ve ürün stokta olmadığı hâlde
    /// satıştan kalkar — hata sessizdir, yalnız kanal adedi sebepsizce küçülür.</para></summary>
    Task<OrderReservationDto> FulfillReservationAsync(OrderFulfillmentInputDto input);

    /// <summary>İADE GİRİŞİNİ kaydeder — mal FİZİKSEL OLARAK kasaya girdiğinde operatör çağırır.
    ///
    /// <para><b>Stok yalnız BURADA döner</b> (2026-08-05 Hakan kararı): iade talebi, kargoda-iade, hatta
    /// "teslim edilmiş iade" statüsü stoğu geri VERMEZ. Mal elimize geçmeden stoğa yazmak, satılabilir
    /// göstermek demektir — müşteri onu ikinci kez satın alabilir.</para>
    ///
    /// <para><b>Rezervasyona DOKUNULMAZ:</b> <c>Fulfilled</c> kalır. İade rezervasyonu diriltseydi stok iki
    /// kez artardı — bir kez giriş fişiyle, bir kez de rezervasyonun serbest kalmasıyla.</para></summary>
    Task<OrderReturnEntryResultDto> RegisterReturnEntryAsync(OrderReturnEntryDto input);
}

/// <summary>İade girişi girdisi — mal teslim alındığında.</summary>
public class OrderReturnEntryDto
{
    [Required]
    public Guid OrderId { get; set; }

    /// <summary>Malın GİRDİĞİ şube/kasa — çıkış yapılan kasadan farklı olabilir (başka şube teslim almış).</summary>
    [Required]
    public Guid BranchId { get; set; }

    [Required]
    public Guid VaultId { get; set; }

    [StringLength(OrderConsts.DetailLongTextMaxLength)]
    public string? Note { get; set; }

    /// <summary>İade edilen satırlar. <b>KISMİ iade gerçektir</b> — operatör miktarı düzeltir; çıkış
    /// satırlarından ön-doldurulur ama olduğu gibi kabul edilmez.</summary>
    public List<OrderReturnEntryLineDto> Lines { get; set; } = new();
}

/// <summary>İade edilen tek satır.</summary>
public class OrderReturnEntryLineDto
{
    /// <summary>Karşılık gelen FİZİKİ ÇIKIŞ bağı — iade neyin geri geldiğini bilmek zorundadır.</summary>
    [Required]
    public Guid PhysicalExitLinkId { get; set; }

    /// <summary>Geri gelen miktar (gram/adet — çıkış satırının boyutlarıyla aynı).</summary>
    public decimal Quantity { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>İade girişi sonucu.</summary>
public class OrderReturnEntryResultDto
{
    /// <summary>Yazılan giriş fişi.</summary>
    public Guid VoucherId { get; set; }

    public int RegisteredLines { get; set; }

    /// <summary>Atlanan satırların gerekçeleri — SESSİZ geçilmez.</summary>
    public List<string> Issues { get; set; } = new();
}

/// <summary>Fiziki çıkış girdisi.</summary>
public class OrderFulfillmentInputDto
{
    [Required]
    public Guid OrderId { get; set; }

    /// <summary>Malı HAZIRLAYAN şube — rezervasyon fişi merkezde kesilir, çıkış hazırlayanın kasasından yapılır.</summary>
    [Required]
    public Guid BranchId { get; set; }

    [Required]
    public Guid VaultId { get; set; }

    [StringLength(OrderConsts.DetailLongTextMaxLength)]
    public string? Note { get; set; }

    /// <summary>Satır-başı fiyat farkı beyanları (opsiyonel).</summary>
    public List<OrderFulfillmentLineInputDto> Lines { get; set; } = new();
}

/// <summary>Fiziki çıkış satırı — yalnız FİYAT FARKI beyanı taşır; miktar/emtia rezervasyon fişinden gelir.
/// <para>Miktarı burada yeniden girmek, rezerve edilenden farklı bir şey çıkarmaya sessizce izin vermek
/// olurdu. Farklı miktar çıkacaksa önce rezervasyon düzeltilir.</para></summary>
public class OrderFulfillmentLineInputDto
{
    [Required]
    public Guid FulfillmentLinkId { get; set; }

    /// <summary><b>null = beyan edilmedi</b> · <b>0 = "fark yok" beyanı</b>. Sistem ASLA türetmez —
    /// fiyat farkını yalnız kullanıcı girer (2026-08-05 Hakan kararı).</summary>
    public decimal? PriceDifference { get; set; }

    public Guid? PriceDifferenceUnitId { get; set; }

    [StringLength(OrderConsts.DetailLongTextMaxLength)]
    public string? Note { get; set; }
}

/// <summary>Eşleştirme adayı arama girdisi.</summary>
public class OrderLineMatchCandidateRequestDto
{
    /// <summary>Serbest arama metni — ürün kodu/adı ya da varyant kodunda aranır. Boş = ilk N kayıt.</summary>
    public string? Search { get; set; }

    /// <summary>Dönecek en fazla aday sayısı (combo listesi; tam liste değil).</summary>
    public int MaxCount { get; set; } = 50;
}

/// <summary>Eşleştirme adayı — kullanıcının combo'da göreceği tek satır.</summary>
public class OrderLineMatchCandidateDto
{
    /// <summary>Eşleştirmede yazılacak değer (<c>OrderLineOperationalData.ProductVariantId</c>).</summary>
    public Guid EntityVariantId { get; set; }

    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string VariantCode { get; set; } = string.Empty;

    /// <summary>Combo'da gösterilen birleşik etiket — kullanıcı ürünü koddan da addan da bulabilmeli.</summary>
    public string DisplayText { get; set; } = string.Empty;

    public override string ToString()
    {
        return DisplayText;
    }
}

/// <summary>Siparişin rezervasyon görünümü — İKİ EKSEN ayrı taşınır (stok · iptal kararı).</summary>
public class OrderReservationDto
{
    public Guid OrderId { get; set; }
    public OrderReservationStatus Status { get; set; }
    public OrderCancellationDecision CancellationDecision { get; set; }

    /// <summary>Rezervasyon fişi — kullanıcı fişe gidebilsin diye taşınır. null = fiş yazılamadı (Blocked).</summary>
    public Guid? VoucherId { get; set; }

    public DateTime? ReservedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? CancellationRequestedAt { get; set; }
    public DateTime? CancellationDecidedAt { get; set; }

    /// <summary>Kurulamama gerekçesi ya da karar notu — SESSİZ atlama olmadığının kullanıcıya görünen yüzü.</summary>
    public string? Note { get; set; }

    /// <summary>Rezerve edilen kalemler (bağ kayıtları) — hangi fiş satırı hangi sipariş kalemini karşılıyor.</summary>
    public List<OrderFulfillmentLinkDto> Links { get; set; } = new();
}

/// <summary>Sipariş kalemi ↔ fiş satırı bağı (çoka-çok).</summary>
public class OrderFulfillmentLinkDto
{
    public Guid Id { get; set; }
    public string RemoteLineId { get; set; } = string.Empty;
    public Guid VoucherId { get; set; }
    public Guid VoucherLineId { get; set; }
    public OrderFulfillmentLinkKind Kind { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public decimal FulfilledAmount { get; set; }

    /// <summary><b>null = beyan edilmedi</b> · <b>0 = "fark yok" beyanı</b>. Sistem ASLA türetmez.</summary>
    public decimal? PriceDifference { get; set; }

    public string? Note { get; set; }
}

/// <summary>İptal kararı girdisi.</summary>
public class OrderCancellationDecisionDto
{
    [Required]
    public Guid OrderId { get; set; }

    /// <summary>true = onayla (rezervasyon serbest) · false = reddet (rezervasyon tutulur).</summary>
    public bool Approve { get; set; }

    [StringLength(OrderConsts.DetailLongTextMaxLength)]
    public string? Note { get; set; }
}

/// <summary>Elle serbest bırakma girdisi.</summary>
public class OrderReservationReleaseDto
{
    [Required]
    public Guid OrderId { get; set; }

    [StringLength(OrderConsts.DetailLongTextMaxLength)]
    public string? Reason { get; set; }
}
