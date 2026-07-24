using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Products;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>SalesChannel liste sorgusu (per-tenant). Company-owned: sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ — Product deseni). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class SalesChannelListRequestDto : ListRequestDto
{
}

/// <summary>Polymorphic liste satırı — TÜM kanal alt-tipleri (base sorgusu). <see cref="ChannelType"/> somut tipi
/// taşır ("Tür" kolonu + düzenlemede doğru forma yönlendirme). Sir alanları (AppSecret/ApiSecret) LİSTEDE YOK.</summary>
public class SalesChannelListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>Kanal türü (N11 / Trendyol) — TPT alt-tipinden türetilir; grid "Tür" kolonu + edit yönlendirmesi.</summary>
    public SalesChannelType ChannelType { get; set; }
}

// ── Yan-maliyet (gider) ayarları — kanal-agnostik GİDER SATIRLARI listesi (SideCostSettings.Items aynası;
//    2026-07-10 yeniden şekillendirme: sabit-alanlı form yerine ürün reçetesi grid'i tarzı satırlar) ──

/// <summary>Kanalın tek gider satırı — <c>SideCostItem</c> owned VO'sunun form aynası (tür + hesaplama + değer +
/// hizmet kartı + fiş hedefi TEK satırda). BU DİLİMDE FİŞ YAZILMAZ; ileride sipariş→fiş akışında satır doğru
/// Service emtiası + doğru KARŞI CARİ ile VoucherLine'a dönüşecek. Hizmet/cari boş bırakılabilir (kalem
/// fiyatlamada yine çalışır) — UI nazik ipucu gösterir.</summary>
public class SideCostItemDto
{
    /// <summary>Kalem türü — reçete satırındaki idempotent reconcile anahtarı.</summary>
    public SideCostKind Kind { get; set; } = SideCostKind.Packaging;

    /// <summary>Serbest görünen ad — boşsa UI türün lokalizesini gösterir (ör. "Offsite Ads").</summary>
    [StringLength(SalesChannelConsts.SideCostDisplayNameMaxLength)]
    public string? DisplayName { get; set; }

    /// <summary>Hesaplama modu — FixedAmount (Add) / PercentOfCost (Percent) / GrossUpPercent (GrossUp; hep en sonda).</summary>
    public SideCostCalcMode CalcMode { get; set; } = SideCostCalcMode.FixedAmount;

    /// <summary>Tutar ya da oran — moda göre yorumlanır; komisyonda AutoRate açıkken fallback oran.</summary>
    public decimal Value { get; set; }

    /// <summary>Sabit tutarın para birimi — id-only; null = kanal yerel birimi (yalnız FixedAmount'ta anlamlı).</summary>
    public Guid? CurrencyUnitId { get; set; }

    /// <summary>Hizmet kartı (Service kataloğu) — id-only, opsiyonel; reçete satırının Service etiketi de bu olur.</summary>
    public Guid? ServiceId { get; set; }

    /// <summary>Fişleme hedefi (karşı cari / genel gider).</summary>
    public SideCostPostingMode PostingMode { get; set; } = SideCostPostingMode.CounterpartyAccount;

    /// <summary>Karşı taraf cari hesabı — id-only, opsiyonel; yalnız CounterpartyAccount modunda anlamlı.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Karşı taraf alt hesabı — id-only, opsiyonel (Voucher.SubAccountId paritesi; ana hesapsız olamaz).</summary>
    public Guid? SubAccountId { get; set; }

    /// <summary>Oran otomatik çözülsün mü — YALNIZ Commission (N11: kategoriden efektif oran; Value = fallback).</summary>
    public bool AutoRate { get; set; }

    /// <summary>Kalem aktif mi — kapalı kalem reçeteye satır üretmez (satır grid'de durur, veri kaybolmaz).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Grid/reçete sırası — GrossUp kalemleri sıradan bağımsız hep en sona projeksiyonlanır.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Yalnız varyantta anahtar açıksa uygulanır (sigortalı-gönderim/Loomis deseninin genellemesi).</summary>
    public bool RequiresVariantOptIn { get; set; }
}

/// <summary>Kanalın yan-maliyet (gider) ayarları — <c>SideCostSettings</c> owned VO'sunun form aynası:
/// gider satırları listesi. Komisyon oranları için araştırma SSOT: .claude/research/channel-commissions.</summary>
public class SideCostSettingsDto
{
    /// <summary>Gider satırları (boş liste = kalem yok; form açılışında boşsa kanal tipine göre varsayılan tohum önerilir).</summary>
    public List<SideCostItemDto> Items { get; set; } = new();
}

// ── N11 (SalesChannelTrN11): AppKey/AppSecret ──────────────────────────────────────────────────────

public class SalesChannelTrN11GetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SIZINTI ÖNLEME: GetDto'da AppKey/AppSecret DAİMA boş döner (AppService redakte eder) → update formunda boş
    // görünür. Kullanıcı doldurursa değişir (application katmanı N11'e doğrular), boş bırakırsa mevcut korunur.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppSecret { get; set; } = string.Empty;

    // Kanal düzeyi varsayılan bilgi metinleri (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur (kullanıcı ezebilir).
    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultShippingInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultExchangeInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultInstallmentInfo { get; set; }

    /// <summary>Yan-maliyet (gider) ayarları — null = hiç yapılandırılmamış (form açılışında boş DTO'yla doldurulur).</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}

public class SalesChannelTrN11CreateDto : ICreateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Oluşturmada kimlik ZORUNLU (application katmanı N11'e doğrular, geçmezse kayıt açılmaz).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string AppKey { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string AppSecret { get; set; } = string.Empty;

    // Kanal düzeyi varsayılan bilgi metinleri (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur (kullanıcı ezebilir).
    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultShippingInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultExchangeInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultInstallmentInfo { get; set; }

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok.</summary>
    public SideCostSettingsDto? SideCosts { get; set; }
}

public class SalesChannelTrN11UpdateDto : IUpdateDto
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik AppService'te (TenantId+CompanyId scope).
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Boş = mevcut korunur; doldurulursa (İKİSİ birlikte) application katmanı N11'e doğrular, geçerse günceller.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppSecret { get; set; } = string.Empty;

    // Kanal düzeyi varsayılan bilgi metinleri (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur (kullanıcı ezebilir).
    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultShippingInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultExchangeInfo { get; set; }

    [StringLength(N11ShipmentConsts.InfoMaxLength)]
    public string? DefaultInstallmentInfo { get; set; }

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok (mevcut ayar update'te null gönderilirse TEMİZLENİR;
    /// form daima dolu DTO gönderir).</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}

// ── Trendyol (SalesChannelTrTrendyol): SellerId/ApiKey/ApiSecret ────────────────────────────────────

public class SalesChannelTrTrendyolGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SellerId matematiksel değil bir KİMLİK → string (sır değil; görünür kalır). UI regex: yalnız rakam.
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    // SIZINTI ÖNLEME: ApiKey/ApiSecret GetDto'da DAİMA boş döner (redakte). Update'te boş = korunur, dolu = değişir.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiSecret { get; set; } = string.Empty;

    // YALNIZ-YAZILIR giriş kolaylığı: Trendyol panelindeki hazır Token (base64(apiKey:apiSecret)). Form GetDto'ya bind
    // ettiğinden burada durur ki commit'te Create/Update'e map'lensin; GetAsync'te ApiKey/ApiSecret gibi DAİMA redakte
    // edilir (sır türevi asla client'a dönmez). PERSIST EDİLMEZ — AppService decode edip ApiKey/ApiSecret'a ayırır.
    [StringLength(SalesChannelConsts.TokenMaxLength)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Yan-maliyet (gider) ayarları — null = hiç yapılandırılmamış (form açılışında boş DTO'yla doldurulur).</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}

public class SalesChannelTrTrendyolCreateDto : ICreateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    // ApiKey/ApiSecret AYRI giriş yolu — ZORUNLU DEĞİL çünkü ALTERNATİF olarak tek Token yapıştırılabilir. AppService
    // Token doluysa onu decode edip bu ikisini override eder; boşsa ApiKey/ApiSecret zorunlu (VerifyOrThrow'da sınanır).
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiSecret { get; set; } = string.Empty;

    // Trendyol panelindeki hazır Token = base64(apiKey:apiSecret). Doluysa ApiKey/ApiSecret'ın alternatifi/önceliklisi
    // (SellerId İÇERMEZ → ayrı alandan gelir). Boşsa ApiKey/ApiSecret ikilisi kullanılır. PERSIST EDİLMEZ.
    [StringLength(SalesChannelConsts.TokenMaxLength)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok.</summary>
    public SideCostSettingsDto? SideCosts { get; set; }
}

public class SalesChannelTrTrendyolUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SellerId görünür kimlik → daima gönderilir/güncellenir (sır değil).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    // Boş = mevcut korunur; doldurulursa (İKİSİ birlikte) güncellenir. ALTERNATİF: tek Token yapıştırılabilir.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiSecret { get; set; } = string.Empty;

    // Trendyol panelindeki hazır Token = base64(apiKey:apiSecret). Doluysa ApiKey/ApiSecret ikilisinin alternatifi/önceliklisi
    // (kimlik değiştirme yolu); boşsa mevcut ApiKey/ApiSecret mantığı geçerli. PERSIST EDİLMEZ.
    [StringLength(SalesChannelConsts.TokenMaxLength)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok (temizler; form daima dolu DTO gönderir).</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}

// ── Etsy (SalesChannelEtsy): Keystring/SharedSecret + OAuth 2.0 PKCE bağlantısı ─────────────────────
//    Kimlik modeli N11/Trendyol'dan FARKLI: statik credential yerine satıcı-onaylı OAuth. Token'lar (access/refresh)
//    DTO'da HİÇ YOK — sunucuda yaşar (sızıntı yüzeyi sıfır); UI yalnız türetilmiş IsConnected durumunu görür.

public class SalesChannelEtsyGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Keystring = Etsy OAuth client_id + x-api-key — PUBLIC uygulama kimliği, SIR DEĞİL → görünür kalır (SellerId gibi).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string Keystring { get; set; } = string.Empty;

    // SIZINTI ÖNLEME: SharedSecret GetDto'da DAİMA boş döner (redakte). Update'te boş = korunur, dolu = değişir.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string SharedSecret { get; set; } = string.Empty;

    // OAuth bağlantısında Etsy API'den çözülür (best-effort) — SALT-OKUNUR görüntü; client'tan yazılmaz.
    public string? ShopId { get; set; }
    public string? ShopName { get; set; }

    /// <summary>Türetilmiş bağlantı durumu (refresh token dolu + süresi geçmemiş) — sunucu hesaplar, token sızmaz.</summary>
    public bool IsConnected { get; set; }

    /// <summary>Yan-maliyet (gider) ayarları — Etsy'de tipik: GrossUp %9,5 + $0,45/satış (USD) + opsiyonel Offsite Ads.</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}

public class SalesChannelEtsyCreateDto : ICreateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Oluşturmada uygulama kimliği ZORUNLU. Keystring Etsy'nin public ping ucuyla doğrulanır (OAuth'suz);
    // SharedSecret ping'le sınanamaz — OAuth token değişiminde dolaylı doğrulanır.
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string Keystring { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok.</summary>
    public SideCostSettingsDto? SideCosts { get; set; }
}

public class SalesChannelEtsyUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Keystring görünür kimlik → daima gönderilir. DEĞİŞİRSE mevcut token'lar eski uygulamaya ait olur →
    // sunucu token'ları temizler (yeniden "Etsy'ye Bağlan" gerekir).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string Keystring { get; set; } = string.Empty;

    // Boş = mevcut korunur; dolu = değişir (redaksiyonlu edit — Trendyol deseni).
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Yan-maliyet (gider) ayarları — null = yapılandırma yok (temizler; form daima dolu DTO gönderir).</summary>
    public SideCostSettingsDto? SideCosts { get; set; }

    public bool IsActive { get; set; }
}
