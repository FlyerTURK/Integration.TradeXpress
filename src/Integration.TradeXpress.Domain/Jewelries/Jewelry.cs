using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Jewelries;

/// <summary>
/// Jewelry = bir <b>mücevher</b> (bitmiş ürün) tanımı (katalog). Taş gibi basit parasal/adet —
/// milyem/işçilik/has YOK. Tanım nitelikleri (model/cins/tür/renk/kategori/grup) + adet takibi +
/// fiyat tipi (adet/miktar başına) + giriş/çıkış fiyatı &amp; para birimi.
///
/// <para><b>Company-scoped:</b> opsiyonel <see cref="CompanyId"/> — null = holding-host (tüm şirketlere),
/// dolu = o şirkete-özel. Host (TenantId=null) global. Görünürlük working-company'ye göre süzülür.</para>
/// </summary>
public class Jewelry : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected Jewelry()
    {
    }

    public Jewelry(
        string code,
        string name,
        Guid? companyId = null,
        bool isQuantity = false,
        bool priceByQuantity = false,
        bool priceTypeChange = true,
        decimal entryPrice = 0m,
        Guid? entryPriceUnitId = null,
        decimal exitPrice = 0m,
        Guid? exitPriceUnitId = null,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        CompanyId        = companyId;
        IsQuantity       = isQuantity;
        PriceByQuantity  = priceByQuantity;
        PriceTypeChange  = priceTypeChange;
        EntryPrice       = entryPrice;
        EntryPriceUnitId = entryPriceUnitId;
        ExitPrice        = exitPrice;
        ExitPriceUnitId  = exitPriceUnitId;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    /// <summary>Şirkete-özel kayıt için sahip Company. null = holding-host / host global.</summary>
    public virtual Guid? CompanyId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }

    // Tanım nitelikleri (opsiyonel)
    public virtual string? Model { get; protected set; }      // Model
    public virtual string? Kind { get; protected set; }       // Cins
    public virtual string? Type { get; protected set; }       // Tür
    public virtual string? Color { get; protected set; }      // Renk
    public virtual string? Category { get; protected set; }   // Kategori
    public virtual string? GroupCode { get; protected set; }  // Grup kodu

    public virtual bool IsQuantity { get; protected set; }
    public virtual bool PriceByQuantity { get; protected set; }
    public virtual bool PriceTypeChange { get; protected set; }

    public virtual decimal EntryPrice { get; protected set; }
    public virtual Guid? EntryPriceUnitId { get; protected set; }
    public virtual decimal ExitPrice { get; protected set; }
    public virtual Guid? ExitPriceUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, JewelryConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, JewelryConsts.DescriptionMaxLength);
    }

    public virtual void SetAttributes(
        string? model, string? kind, string? type, string? color, string? category, string? groupCode)
    {
        Model     = Trim(model);
        Kind      = Trim(kind);
        Type      = Trim(type);
        Color     = Trim(color);
        Category  = Trim(category);
        GroupCode = Trim(groupCode);

        static string? Trim(string? v)
        {
            var t = v?.Trim();
            return string.IsNullOrEmpty(t) ? null : t.Length > JewelryConsts.AttributeMaxLength ? t[..JewelryConsts.AttributeMaxLength] : t;
        }
    }

    public virtual void SetPricing(
        bool isQuantity, bool priceByQuantity, bool priceTypeChange,
        decimal entryPrice, Guid? entryPriceUnitId, decimal exitPrice, Guid? exitPriceUnitId)
    {
        IsQuantity       = isQuantity;
        PriceByQuantity  = priceByQuantity;
        PriceTypeChange  = priceTypeChange;
        EntryPrice       = entryPrice;
        EntryPriceUnitId = entryPriceUnitId;
        ExitPrice        = exitPrice;
        ExitPriceUnitId  = exitPriceUnitId;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId+CompanyId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, JewelryConsts.CodeMaxLength);
    }

    #endregion
}
