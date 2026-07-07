using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Satılabilir ürün — <b>kanonik, polimorfik emtia</b> (Maden/Hurda/Hizmet… <b>Nakit hariç</b>). Emtia türü
/// üründe SABİT DEĞİL; ileride BOM bileşenlerinden türer (Adım 2). <b>Company-owned</b> güvenlik sınırı
/// (<see cref="ICompanyOwned"/>, non-nullable <see cref="CompanyId"/>) + per-tenant. Ürün bir VİTRİN +
/// gruplamadır; satılabilir asıl bilgi (fiyat/reçete/görsel) varyantlarda yaşar (bkz. <c>ProductVariant</c>).
/// Marketplace'e listelenince N11 <c>product ↔ stockItem</c> yapısına eşlenir (Product ↔ Variant).
///
/// <para>Ana varyant kavramı Company→HQ Branch / Branch→default Vault değişmezini devralır: en-az-1 varyant,
/// tekil <c>IsMain</c> (bkz. <c>ProductVariant.IsMain</c>, invariant <c>ProductVariantManager</c>'da).</para>
///
/// <para>NOT (Adım 1 — minimal): Reçete/fiyat/stok/görsel + kanal-listeleme SONRAKİ adımlarda. Product↔Variant
/// alan bölüşümü Adım 2'de netleşecek (şu an kanonik kimlik alanları).</para>
/// </summary>
public class Product : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Kapsam DAİMA çalışılan şirket (sunucu zorlar).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Ürün görsel URL'leri (sıralı; marketplace product.images — liste index'i = order). PrimitiveCollection.</summary>
    public virtual List<string> ImageUrls { get; protected set; } = new();

    protected Product() { }

    public Product(
        Guid companyId,
        string code,
        string name)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        IsActive = true;
    }

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04). Normalize + min/max StringFieldGuard'da; benzersizlik AppService'te.
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
    }

    // NOT (Adım 1 varsayımı): NormalizeName TitleCase yapar; marketplace başlıkları casing korumalı olabilir
    // (ör. "iPhone 15") → Adım 5 (kanal-listeleme) öncesi gözden geçirilecek. Şimdilik konvansiyon deseni.
    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ProductConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Görsel URL'lerini ayarlar — boşları at, trim, en fazla <see cref="ProductConsts.MaxImageCount"/> (sıra korunur).</summary>
    public virtual void SetImageUrls(IEnumerable<string> urls)
    {
        ImageUrls = (urls ?? Enumerable.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Take(ProductConsts.MaxImageCount)
            .ToList();
    }

    public override string ToString()
    {
        return Code;
    }
}
