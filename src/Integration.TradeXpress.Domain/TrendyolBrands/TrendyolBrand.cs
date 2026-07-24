namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// Trendyol markası — <b>HOST-GLOBAL</b> write-through CACHE (<see cref="IMultiTenant"/> DEĞİL; TenantId YOK → tüm
/// tenant'lar paylaşır; <see cref="TrendyolCategories.TrendyolCategory"/> ikizi). Marka evreni ~780K kayıt (id'ler
/// ~3M'a kadar seyrek; canlı ölçüm 2026-07-23) → TAM SYNC YOK: canlı ada-göre arama SSOT kalır; yalnız kullanıcının
/// SEÇİP kanal-ürüne kaydettiği marka buraya upsert edilir (K3 hybrid karar, 2026-07-23). Picker açılışta bu cache'ten
/// beslenir; cache'te olmayan marka canlı aramayla bulunur, seçilince buraya düşer.
/// </summary>
public class TrendyolBrand : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected TrendyolBrand()
    {
    }

    public TrendyolBrand(long externalId, string name, bool isLuxury)
    {
        // ExternalId set-once (Trendyol'dan gelir): pozitif long (API `id` alanı int döner ama evren büyüyor → long güvenli).
        if (externalId <= 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Brand:ExternalIdInvalid")
                .WithData("externalId", externalId);
        }

        ExternalId = externalId;
        SetName(name);
        IsLuxury = isLuxury;
    }

    #endregion

    #region Properties

    /// <summary>Trendyol marka id'si (API `id`; ürün push'unda zorunlu brandId budur). Global benzersiz.</summary>
    public long ExternalId { get; protected set; }

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Trendyol "luxury" bayrağı (API'de hazır geliyor) — lüks marka segmenti işareti.</summary>
    public bool IsLuxury { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, TrendyolBrandConsts.NameMaxLength);
    }

    public virtual void SetIsLuxury(bool isLuxury)
    {
        IsLuxury = isLuxury;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
