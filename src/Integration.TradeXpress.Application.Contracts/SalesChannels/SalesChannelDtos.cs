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

// Yan-maliyet (gider) DTO'ları 2026-07-28'de KALDIRILDI: kanal başına elle gider girme ihtiyacı kalmadı —
// komisyon kategori senkronundan, kargo şablon/tarifeden geliyor. Alanın sözleşmeden çıkması aynı zamanda bir
// EMNİYET: istemci artık kanala boş bir gider listesi ({"Items":[]}) gönderemez, ki o değer komisyonu sessizce
// sıfırlıyordu. Kanal kaydındaki JSON kolonu DURUYOR (mevcut satırlar dondurulmuş hâlde).

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

    /// <summary>Bu kanalın N11 istekleri GERÇEK N11'e mi gidiyor, yoksa yerel sahte sunucuya mı — salt görüntü.
    /// <b>Neden DTO'da:</b> kullanıcının sahte sonucu gerçek sanması, yanlış kararların en sessiz yoludur
    /// ("push başarılı" görüp ürünün N11'de olduğunu sanmak). Form bunu rozet olarak gösterir.</summary>
    public bool IsMockEndpoint { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

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

    public bool IsActive { get; set; }

    /// <summary>Kanalın muhasebe cari ALT hesabı (<c>SubAccount.Id</c>) — <b>ZORUNLU</b> (2026-08-06 Hakan kararı).
    /// Cari hesap ayrıca tutulmaz; alt hesap zaten bir cariye bağlıdır.
    /// <para>Zorunluluğun sebebi: kanalın carisi yoksa sipariş rezervasyon fişinin BAŞLIĞI kurulamıyor ve her
    /// sipariş sessizce <c>Blocked</c>'a düşüyordu — engel kanalda değil siparişte görünüyordu. Eksikliği
    /// kaydın kendisinde durdurmak, sonradan anlaşılmaz bir arızaya dönüşmesinden iyidir.</para></summary>
    [Required]
    public Guid? SubAccountId { get; set; }

}
