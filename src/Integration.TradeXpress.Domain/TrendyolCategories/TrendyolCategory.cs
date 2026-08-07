namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol kategori ağacı düğümü — <b>HOST-GLOBAL</b> referans taksonomi. <see cref="IMultiTenant"/> DEĞİL (TenantId
/// YOK) → tüm tenant'lar aynı ağacı paylaşır; host bir kez REST <c>/integration/product/product-categories</c>'ten
/// sync'ler (<see cref="N11Categories.N11Category"/> ikizi). Attribute/value SAKLANMAZ — bir yaprak kategori seçilince
/// ilgili SalesChannel'ın KENDİ kimliğiyle on-demand çekilir (T2). Ürünler yalnız <see cref="IsLeaf"/> (subCategories
/// boş) düğüme tanımlanır. N11'in komisyon/valör alanları Trendyol'da yok (ayrı kaynak) → o blok düşer.
/// </summary>
public class TrendyolCategory : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected TrendyolCategory()
    {
    }

    public TrendyolCategory(string externalId, string? parentExternalId, string name, bool isLeaf)
    {
        // ExternalId set-once (Trendyol'dan gelir): normalize YOK, yalnız null/uzunluk guard'ı (kimlik, matematik değil).
        ExternalId = StringFieldGuard.EnsureRequiredText(externalId, nameof(ExternalId), 1, TrendyolCategoryConsts.ExternalIdMaxLength);
        SetParent(parentExternalId);
        SetName(name);
        IsLeaf = isLeaf;
    }

    #endregion

    #region Properties

    /// <summary>Trendyol kategori id'si (numerik ama matematik değil → string). Global benzersiz.</summary>
    public string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Üst kategori Trendyol id'si (kök kategorilerde null). Ağaç REST <c>parentId</c>'sinden kurulur.</summary>
    public string? ParentExternalId { get; protected set; }

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Dip seviye (subCategories boş) mi — ürün yalnız buraya tanımlanır; attribute yalnız burada.</summary>
    public bool IsLeaf { get; protected set; }

    /// <summary>Komisyon oranı (%) — <b>KALITIMLI</b> (2026-08-06 Hakan kararı).
    ///
    /// <para><c>null</c> = "bu seviyede TANIMLI DEĞİL", <b>sıfır komisyon DEĞİL</b>. Çözüm ağaçta YUKARI yürür:
    /// yaprak → üst → kök; ilk dolu oran kullanılır. Böylece oran yalnız belirgin üst kategorilere girilir,
    /// binlerce yaprağa tek tek girilmesi gerekmez.</para>
    ///
    /// <para><b>Neden N11'den farklı:</b> N11 kategori ucu komisyonu KENDİSİ döndürür
    /// (<c>N11Category.CommissionRate</c> her düğümde dolu). Trendyol'un kategori ucu
    /// (<c>/integration/product/product-categories</c>) yalnız <c>id/name/parentId/isLeaf</c> verir —
    /// komisyon orada YOKTUR ve satıcının sözleşmesine (satıcı seviyesi, program, kampanya) bağlıdır.</para>
    ///
    /// <para>⚠ Oranın TABANI (KDV dahil mi hariç mi) henüz modellenmedi; kaynaklar çelişiyor ve fark ~4 puan.
    /// Production'da netleştirilecek — <c>TrendyolCommissionDefaults</c> notuna bakınız.</para></summary>
    public decimal? CommissionRate { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, TrendyolCategoryConsts.NameMaxLength);
    }

    public virtual void SetParent(string? parentExternalId)
    {
        ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
    }

    public virtual void SetIsLeaf(bool isLeaf)
    {
        IsLeaf = isLeaf;
    }

    /// <summary>Komisyon oranını atar. <c>null</c> geçmek oranı KALDIRIR (üstten miras alınır) — bu, oranı
    /// sıfırlamakla AYNI ŞEY DEĞİLDİR. Negatif oran anlamsızdır; kabul edilmez.</summary>
    public virtual void SetCommissionRate(decimal? commissionRate)
    {
        if (commissionRate is < 0m)
        {
            throw new BusinessException("TradeXpress:TrendyolCategory:CommissionRateInvalid")
                .WithData("Rate", commissionRate);
        }

        CommissionRate = commissionRate;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
