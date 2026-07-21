namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürüne atanmış EKLENTİ (owned → JSON; <see cref="Products.Product.AddOns"/>). Katalogdan (<see cref="AddOnId"/>)
/// seçilir; satır bazında fiyat/para birimi/zorunluluk OVERRIDE edilebilir (boş bırakılan alan katalog varsayılanını
/// devralır). Pazaryerine push'ta native "seçenek" olmadığından bu atamalar VARYANT olarak yansıtılır (projeksiyon
/// Faz 2). <see cref="AddOnId"/> = katalog referansı (id-only; nav YOK).
/// </summary>
public class ProductAddOn
{
    public Guid AddOnId { get; set; }

    /// <summary>Fiyat override — null ise katalog fiyatı geçerli.</summary>
    public decimal? PriceOverride { get; set; }

    /// <summary>Para birimi override — null ise katalog para birimi geçerli.</summary>
    public Guid? CurrencyUnitOverrideId { get; set; }

    /// <summary>Zorunlu mu — true ise müşteri bu eklentiyi seçmek zorunda (varsayılan olarak dahil).</summary>
    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Ürüne özel not (opsiyonel).</summary>
    public string? Note { get; set; }

    public ProductAddOn()
    {
    }

    public ProductAddOn(
        Guid addOnId,
        decimal? priceOverride,
        Guid? currencyUnitOverrideId,
        bool isRequired,
        int displayOrder,
        string? note)
    {
        AddOnId = addOnId;
        PriceOverride = priceOverride;
        CurrencyUnitOverrideId = currencyUnitOverrideId;
        IsRequired = isRequired;
        DisplayOrder = displayOrder;
        Note = note;
    }
}
