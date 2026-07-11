using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanalın <b>yan-maliyet (gider) ayarları</b> — owned VO, <c>AppSalesChannels.SideCosts</c> JSON kolonunda yaşar.
/// 2026-07-10 yeniden şekillendirme (kullanıcı kararı): sabit-alanlı form yerine ürün reçetesi grid'i tarzı
/// <b>GİDER SATIRLARI</b> listesi — her satır <see cref="SideCostItem"/> (tür + hesaplama + değer + fiş hedefi).
/// KANAL-AGNOSTİK: kanal tipine göre varsayılan tohum farklıdır (N11/Trendyol/Etsy), model tektir.
/// <c>SideCostRecipeComposer</c> satırlardan kanal varyant reçetesine OTOMATİK satırlar üretir
/// (<see cref="SideCostKind"/> işaretli; idempotent reconcile; GrossUp satırları HEP EN SONDA).
///
/// <para><b>Eski şema toleransı:</b> JSON kolonundaki eski sabit-alanlı payload (PackagingCost/CargoCost/
/// InsuredShipping*/DefaultCommissionRate/PerSaleFixedFee/ExtraFeeRate) okumada Items listesine DÖNÜŞTÜRÜLÜR
/// (<c>SideCostSettingsJson</c> — kullanıcı test verisi kaybolmaz); yazım hep yeni şemadır.</para>
///
/// <para>Komisyon oranları (araştırma SSOT: .claude/research/channel-commissions/): N11 kategoriden OTOMATİK
/// (AutoRate işaretli Commission satırı; Value = fallback), Trendyol ilk fazda kanal-oran, Etsy kanal-sabit
/// %9,5 + $0,45/satış USD + opsiyonel Offsite Ads (ayrı GrossUp satırı, varsayılan kapalı).</para>
/// </summary>
public class SideCostSettings
{
    #region Constructors

    protected SideCostSettings()
    {
        Items = new List<SideCostItem>();
    }

    public SideCostSettings(IEnumerable<SideCostItem>? items)
    {
        // Sıra kullanıcı verisidir (DisplayOrder) — burada dokunulmaz; GrossUp-en-sonda kuralı MOTORDA
        // (SideCostRecipeComposer) uygulanır, ayar hangi sırada verilirse verilsin fiyat matematiği korunur.
        Items = items?.Where(i => i is not null).ToList() ?? new List<SideCostItem>();

        // GrossUp ücretleri AYNI satış fiyatının yüzdesidir → composer oranları TOPLAYIP tek satır üretir
        // (P = taban ÷ (1−Σ/100)); kalemler tek tek geçerli olsa da AKTİF toplam payda sınırını aşamaz.
        // (AutoRate'in çözülmüş oranı burada bilinemez — Value fallback'leri denetlenir; nihai toplam
        // composer'da da guard'lıdır.)
        var grossUpTotal = Items
            .Where(i => i.IsEnabled && i.CalcMode == SideCostCalcMode.GrossUpPercent)
            .Sum(i => i.Value);
        if (grossUpTotal >= ProductRecipeConsts.GrossUpOperandExclusiveMax)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostRateOutOfRange")
                .WithData("property", nameof(SideCostItem.Value));
        }

        // Çözülmüş efektif oran TEK AutoRate kalemi varsayar (GetAutoCommissionFallbackRate FirstOrDefault;
        // composer çözülmüş oranı HER AutoRate kaleme uygular — ikinci aktif AutoRate kalemi aynı oranı
        // sessizce 2x saydırırdı) → fail-fast. Kapalı (IsEnabled=false) kalem satır üretmez, sayılmaz.
        if (Items.Count(i => i.IsEnabled && i.AutoRate) > 1)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostSingleAutoRateItem");
        }
    }

    #endregion

    #region Properties

    /// <summary>Gider satırları — reçeteye projeksiyonlanan kalemler (boş liste = kalem yok).</summary>
    public virtual IReadOnlyList<SideCostItem> Items { get; protected set; } = null!;

    #endregion

    #region Methods

    /// <summary>N11 efektif komisyon çözümünde kanal-fallback oranı: AutoRate işaretli AKTİF Commission
    /// satırının <see cref="SideCostItem.Value"/>'su (yoksa null — kategori oranı tek kaynak kalır).</summary>
    public virtual decimal? GetAutoCommissionFallbackRate()
    {
        var item = Items.FirstOrDefault(i => i.Kind == SideCostKind.Commission && i.IsEnabled && i.AutoRate);
        if (item is null || item.Value <= 0m)
        {
            return null;
        }

        return item.Value;
    }

    public override string ToString()
    {
        return $"Items={Items.Count}";
    }

    #endregion
}
