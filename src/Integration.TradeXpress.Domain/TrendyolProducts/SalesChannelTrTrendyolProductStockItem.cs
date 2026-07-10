namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol kanal-ürününde bir VARYANTIN <b>kanal-özel override başlığı</b> — ERP <see cref="Integration.TradeXpress.Products.ProductVariant"/>'ın
/// (fiyat/stok SSOT'u) Trendyol-scope özelleştirmesi. null alan = ERP'den DEVRAL (N11 ile aynı desen).
/// <b>Company-owned</b> (<see cref="CompanyId"/> kanal-üründen denormalize) + per-tenant. Anchor artık BU entity'nin
/// KENDİ <see cref="Entity.Id"/>'sidir — Trendyol'un gerçek varyant kimliği (N11'in 2026-07-09 kararının portu,
/// "klon-sonra-ayrış" felsefesi: kanal ürünü ERP'den yalnız genetik alır, sonrasında ERP'ye dokunmadan bağımsız yaşar).
/// Kanal-özel reçete satırları (<see cref="SalesChannelTrTrendyolProductStockItemRecipeLine"/>) bu başlığa
/// <see cref="SalesChannelTrTrendyolProductId"/> + StockItemId çiftiyle eşlenir (ayrı tablo; nav yok, id-only).
///
/// <para><b>Türetilmiş fiyat/NetCost PERSIST EDİLMEZ</b> — canlı hesaplanır (<c>ProductRecipeCostCalculator</c>):
/// türetilmiş fiyat = NetCost × (1 + <see cref="Margin"/>/100) [MARKUP]. <see cref="Margin"/> varyant-başı yüzde marj.
/// Push fiyat zinciri: <see cref="OverridePrice"/> ?? türetilmiş ?? ERP SalePrice; stok: <see cref="OverrideStock"/> ?? ERP StockQuantity.</para>
/// <para><b>Sözlük (S3):</b> StockItem = kanal KOMBİNASYON satırı (özellik değerleri + override/reçete — kullanıcının
/// yönettiği niyet); Sku (<see cref="SalesChannelTrTrendyolProductSku"/>) = Trendyol push KİMLİK satırı
/// (barcode/stockCode — fiilen gönderilenin kaydı).</para>
/// </summary>
public class SalesChannelTrTrendyolProductStockItem : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProductStockItem()
    {
    }

    public SalesChannelTrTrendyolProductStockItem(
        Guid companyId,
        Guid salesChannelTrTrendyolProductId,
        Guid? productVariantId)
    {
        SetCompany(companyId);
        SetChannelProduct(salesChannelTrTrendyolProductId);
        SetProductVariant(productVariantId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kanal-üründen denormalize (güvenlik sınırı). Set-once.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Trendyol kanal ürünü — id-only referans. Set-once.</summary>
    public virtual Guid SalesChannelTrTrendyolProductId { get; protected set; }

    /// <summary>
    /// Override'ın ait olduğu ERP varyantı — id-only referans, OPSİYONEL (set-once; null da set edilebilir).
    /// null → Trendyol-only kombinasyon (ERP'de karşılığı yok; ör. Trendyol'da sonradan eklenen "Siyah" rengi).
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
    /// Kartezyen kombinasyon KİMLİĞİ — <c>"{AttributeId}={ValueId}|..."</c>, AttributeId'ye göre sıralı (N11 ile AYNI
    /// format: STABİL ID'lerden kurulur, Name/Value METİN değil — özellik/değer yeniden adlandırılırsa imza bozulmaz).
    /// <see cref="SalesChannelTrTrendyolProductAttribute"/>/<see cref="SalesChannelTrTrendyolProductAttributeValue"/>
    /// tarafından üretilen HER kombinasyon satırı (ERP-backed VE Trendyol-only fark etmez) bu imzayla reconcile edilir —
    /// <see cref="ProductVariantId"/> yalnız fiyat/stok fallback KAYNAĞI, reconcile anahtarı DEĞİL. Özellik
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
            throw new BusinessException("TradeXpress:Trendyol:ProductVariant:OverridePriceNegative");
        }

        OverridePrice = price;
        OverridePriceCurrencyUnitId = price is null ? null : (currencyUnitId == Guid.Empty ? null : currencyUnitId);
    }

    /// <summary>Kanal-özel stok (opsiyonel; negatif geçersiz).</summary>
    public virtual void SetOverrideStock(int? stock)
    {
        if (stock is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:ProductVariant:OverrideStockNegative");
        }

        OverrideStock = stock;
    }

    /// <summary>Varyant-başı marj (markup yüzdesi; opsiyonel). Negatif geçersiz (fail-fast).</summary>
    public virtual void SetMargin(decimal? margin)
    {
        if (margin is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:Trendyol:ProductVariant:MarginNegative");
        }

        Margin = margin;
    }

    /// <summary>Kartezyen kombinasyon imzasını atar — kartezyen motor yalnız İNSERT'te çağırır (kombinasyon değişirse
    /// reconcile eski satırı SİLİP yenisini üretir; mevcut satırın imzası sonradan değiştirilmez).</summary>
    public virtual void SetCombinationSignature(string? signature)
    {
        CombinationSignature = StringFieldGuard.EnsureOptionalText(
            signature, nameof(CombinationSignature), EntityFieldConsts.DescriptionMinLength, TrendyolProductConsts.CombinationSignatureMaxLength);
    }

    public override string ToString()
    {
        return $"{SalesChannelTrTrendyolProductId}/{Id}";
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetChannelProduct(Guid salesChannelTrTrendyolProductId)
    {
        if (salesChannelTrTrendyolProductId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrTrendyolProductId));
        }

        SalesChannelTrTrendyolProductId = salesChannelTrTrendyolProductId;
    }

    private void SetProductVariant(Guid? productVariantId)
    {
        // null serbest (Trendyol-only kombinasyon); dolu ise Guid.Empty geçersiz (fail-fast).
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductVariantId));
        }

        ProductVariantId = productVariantId;
    }

    #endregion
}
