namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 kanal-ürününde bir VARYANTIN <b>kanal-özel override başlığı</b> — ERP <see cref="Integration.TradeXpress.Products.ProductVariant"/>'ın
/// (fiyat/stok SSOT'u) N11-scope özelleştirmesi. null alan = ERP'den DEVRAL (kullanıcı kararı 2026-07-08).
/// <b>Company-owned</b> (<see cref="CompanyId"/> kanal-üründen denormalize) + per-tenant. Anchor artık BU entity'nin
/// KENDİ <see cref="Entity.Id"/>'sidir — N11'in gerçek varyant kimliği (2026-07-09 kullanıcı kararı, "klon-sonra-ayrış"
/// felsefesi: N11 ürünü ERP'den yalnız genetik alır, sonrasında ERP'ye dokunmadan bağımsız yaşar). Kanal-özel reçete
/// satırları (<see cref="SalesChannelTrN11ProductStockItemRecipeLine"/>) bu başlığa <see cref="SalesChannelTrN11ProductId"/> +
/// <see cref="ProductVariantId"/> çiftiyle eşlenir (ayrı tablo; nav yok, id-only).
///
/// <para><b>Türetilmiş fiyat/NetCost PERSIST EDİLMEZ</b> — canlı hesaplanır (<c>ProductRecipeCostCalculator</c>):
/// türetilmiş fiyat = NetCost × (1 + <see cref="Margin"/>/100) [MARKUP]. <see cref="Margin"/> varyant-başı yüzde marj.
/// Push fiyat zinciri: <see cref="OverridePrice"/> ?? türetilmiş ?? ERP SalePrice; stok: <see cref="OverrideStock"/> ?? ERP StockQuantity.</para>
/// /// <para><b>Sözlük (S3):</b> StockItem = kanal KOMBİNASYON satırı (özellik değerleri + override/reçete — kullanıcının
/// yönettiği niyet); Sku (<see cref="SalesChannelTrN11ProductSku"/>) = N11 push KİMLİK satırı (sellerStockCode/N11SkuId —
/// fiilen gönderilenin kaydı).</para>
/// </summary>
public class SalesChannelTrN11ProductStockItem : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductStockItem()
    {
    }

    public SalesChannelTrN11ProductStockItem(
        Guid companyId,
        Guid salesChannelTrN11ProductId,
        Guid? productVariantId)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrN11ProductId);
        SetProductVariant(productVariantId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip N11 kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrN11ProductId { get; protected set; }

    /// <summary>
    /// Override'ın ait olduğu ERP varyantı — id-only referans, OPSİYONEL (set-once; null da set edilebilir).
    /// null → N11-only kombinasyon (ERP'de karşılığı yok; ör. N11'de sonradan eklenen "Siyah" rengi).
    /// dolu → ERP varyantından türedi/izleniyor. <b>ProductVariantId null iken <see cref="OverridePrice"/> ve
    /// <see cref="OverrideStock"/> ZORUNLU olacaktır (ERP'den devralınacak kaynak yok) — bu kural burada
    /// ZORLANMAZ, üst katmanda (UI/AppService) doğrulanır.</b>
    /// </summary>
    public virtual Guid? ProductVariantId { get; protected set; }

    /// <summary>Kanal-özel mutlak liste fiyatı (opsiyonel). null = ERP/türetilmiş fiyat devralınır.</summary>
    public virtual decimal? OverridePrice { get; protected set; }

    /// <summary>Override fiyatının para birimi (id-only, nav yok). Fiyat null ise null.</summary>
    public virtual Guid? OverridePriceCurrencyUnitId { get; protected set; }

    /// <summary>Kanal-özel stok miktarı (opsiyonel). null = ERP StockQuantity devralınır.</summary>
    public virtual int? OverrideStock { get; protected set; }

    /// <summary>Varyant-başı marj (markup yüzdesi; ör. 20 → türetilmiş = NetCost × 1.20). null = marj yok.</summary>
    public virtual decimal? Margin { get; protected set; }

    /// <summary>
    /// Kartezyen kombinasyon KİMLİĞİ — <c>"{AttributeId}={ValueId}|..."</c>, AttributeId'ye göre sıralı (2026-07-09 kararı:
    /// STABİL ID'lerden kurulur, Name/Value METİN değil — özellik/değer yeniden adlandırılırsa imza bozulmaz).
    /// <see cref="SalesChannelTrN11ProductAttribute"/>/<see cref="SalesChannelTrN11ProductAttributeValue"/>
    /// tarafından üretilen HER kombinasyon satırı (ERP-backed VE N11-only fark etmez) bu imzayla reconcile edilir —
    /// <see cref="ProductVariantId"/> artık yalnız fiyat/stok fallback KAYNAĞI, reconcile anahtarı DEĞİL. Özellik
    /// tanımlanmamış (legacy ERP-doğrudan) kanal ürünlerinde null.
    /// </summary>
    public virtual string? CombinationSignature { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Kanal-özel mutlak fiyat + para birimi (fiyat null → para birimi de null). Negatif fiyat geçersiz (fail-fast).</summary>
    public virtual void SetOverridePrice(decimal? price, Guid? currencyUnitId)
    {
        if (price is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:OverridePriceNegative");
        }

        OverridePrice = price;
        OverridePriceCurrencyUnitId = price is null ? null : (currencyUnitId == Guid.Empty ? null : currencyUnitId);
    }

    /// <summary>Kanal-özel stok (opsiyonel; negatif geçersiz).</summary>
    public virtual void SetOverrideStock(int? stock)
    {
        if (stock is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:OverrideStockNegative");
        }

        OverrideStock = stock;
    }

    /// <summary>Varyant-başı marj (markup yüzdesi; opsiyonel). Negatif geçersiz (fail-fast).</summary>
    public virtual void SetMargin(decimal? margin)
    {
        if (margin is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:N11:ProductVariant:MarginNegative");
        }

        Margin = margin;
    }

    /// <summary>Kartezyen kombinasyon imzasını atar — kartezyen motor yalnız İNSERT'te çağırır (kombinasyon değişirse
    /// reconcile eski satırı SİLİP yenisini üretir; mevcut satırın imzası sonradan değiştirilmez).</summary>
    public virtual void SetCombinationSignature(string? signature)
    {
        CombinationSignature = StringFieldGuard.EnsureOptionalText(
            signature, nameof(CombinationSignature), EntityFieldConsts.DescriptionMinLength, N11ProductConsts.CombinationSignatureMaxLength);
    }

    public override string ToString()
    {
        return $"{SalesChannelTrN11ProductId}/{Id}";
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetChannelProduct(Guid salesChannelTrN11ProductId)
    {
        if (salesChannelTrN11ProductId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrN11ProductId));
        }

        SalesChannelTrN11ProductId = salesChannelTrN11ProductId;
    }

    private void SetProductVariant(Guid? productVariantId)
    {
        // null serbest (N11-only kombinasyon); dolu ise Guid.Empty geçersiz (fail-fast).
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductVariantId));
        }

        ProductVariantId = productVariantId;
    }

    #endregion
}
