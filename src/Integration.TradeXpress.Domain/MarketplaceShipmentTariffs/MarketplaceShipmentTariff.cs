using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Pazaryerinin YAYIMLADIĞI anlaşmalı kargo tarifesi — bir kanal (<see cref="Channel"/>) + bir taşıyıcı
/// (<see cref="CarrierCode"/>) + bir yürürlük dönemi (<see cref="EffectiveFrom"/>) için desi fiyat tablosu.
///
/// <para><b>HOST-GLOBAL</b> (IMultiTenant DEĞİL; TenantId kolonu YOK → tüm tenant'lar paylaşır). Gerekçe: bu
/// veri pazaryerinin herkese ilan ettiği listedir, tenant'a göre değişmez — <see cref="N11Cities.N11City"/>
/// ile aynı sınıf. Kendi kargo anlaşması olan şirket bu satırı EZMEZ; kanalın kendi "elle girilen kargo
/// tutarı" (SideCostItem Kind=Cargo) alanını kullanır ve o DAİMA kazanır (2026-07-26 Hakan kararı).
/// İleride tenant-özel tarife istenirse aynı şekle sahip IMultiTenant bir override entity'si eklenip
/// çözümleme "önce tenant, yoksa host" zincirine çevrilir — bu yüzden okuma yolu tek bir çözümleyiciden
/// geçirilir, çağrı yerlerine dağıtılmaz.</para>
///
/// <para><b>Neden yürürlük tarihli:</b> tarife sık değişiyor — 2026-07-10 → 07-26 arası 16 günde fiyatlar
/// ~%6 arttı, 100+ katsayıları ve şartlı barem KAPSAMI ("≤5 desi" → "10 desi ve altı") değişti. Eski sürüm
/// SİLİNMEZ: geçmiş siparişin maliyeti kendi dönemindeki tarifeden doğrulanabilmeli.</para>
///
/// <para><b>Vergi/harç tarifenin ÜSTÜNE eklenir</b> ve kanaldan kanala değişir (N11: %20 KDV + %2,35 posta
/// hizmet bedeli, Yurtiçi'de ayrıca 0,60 TL SMS; Trendyol'da posta/SMS fiyata dahil). Bu yüzden oranlar
/// tarife satırında taşınır — hesapta sabit varsayılmaz.</para>
/// </summary>
public class MarketplaceShipmentTariff : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected MarketplaceShipmentTariff()
    {
    }

    public MarketplaceShipmentTariff(
        SalesChannelType channel,
        string carrierCode,
        string carrierName,
        ShipmentChargeBasis chargeBasis,
        decimal overflowIncrementAmount,
        DateTime effectiveFrom,
        string sourceVersion)
    {
        Channel = channel;
        CarrierCode = StringFieldGuard.NormalizeInvariantCode(
            carrierCode, nameof(CarrierCode), 1, MarketplaceShipmentTariffConsts.CarrierCodeMaxLength);
        CarrierName = StringFieldGuard.EnsureRequiredText(
            carrierName, nameof(CarrierName), 1, MarketplaceShipmentTariffConsts.CarrierNameMaxLength);
        ChargeBasis = chargeBasis;
        SetOverflowIncrement(overflowIncrementAmount);
        EffectiveFrom = effectiveFrom.Date;
        SourceVersion = StringFieldGuard.EnsureRequiredText(
            sourceVersion, nameof(SourceVersion), 1, MarketplaceShipmentTariffConsts.SourceVersionMaxLength);
    }

    #endregion

    #region Properties

    /// <summary>Hangi pazaryeri (N11/Trendyol/Etsy) — mevcut <see cref="SalesChannelType"/> yeniden kullanılır,
    /// tarifeye özel ikinci bir kanal enum'u AÇILMAZ.</summary>
    public virtual SalesChannelType Channel { get; protected set; }

    /// <summary>Tarifenin KENDİ nötr taşıyıcı kodu (ARAS/SURAT/PTT/…). Pazaryerinin firma kimliğine bilinçli
    /// olarak BAĞLI DEĞİL: N11 firmalarını yansıtan <c>N11ShipmentCompany</c> her gece tam re-sync'te satır silebiliyor ve kısa kod opsiyonel
    /// (kısa kodu olmayan firmalar var) — tarife o kırılganlığa bağlanmaz.</summary>
    public virtual string CarrierCode { get; protected set; } = string.Empty;

    /// <summary>Pazaryerinin ilan ettiği ad ("Aras Kargo", "DHL e-Commerce", "Kolay Gelsin/Sendeo").</summary>
    public virtual string CarrierName { get; protected set; } = string.Empty;

    /// <summary>Pazaryerindeki kargo firmasının kimliği (N11'de <c>N11ShipmentCompany.ExternalId</c>) —
    /// GEVŞEK id-only bağ: FK YOK, navigation YOK, <c>null</c> = henüz eşlenmedi. <c>N11ShipmentCompany</c> satırı silinse bile tarife
    /// öksüz kalmaz (N11City.CoreAdministrativeAreaId ile aynı desen).</summary>
    public virtual string? ChannelCompanyExternalId { get; protected set; }

    /// <summary>Çok parçalı gönderi kümülatif mi parça başı mı ücretlenir (N11'de yalnız PTT parça başı).</summary>
    public virtual ShipmentChargeBasis ChargeBasis { get; protected set; }

    /// <summary>Tablo son satırının (<see cref="MarketplaceShipmentTariffConsts.TabulatedMaxDesi"/>) ÜSTÜ için
    /// desi başına artış. Tablo eğiminden TÜRETİLMEZ — pazaryeri bunu ayrıca ilan eder ve eğimle uyuşmayabilir
    /// (N11'de PTT/Sürat'ta uyuşmuyor).</summary>
    public virtual decimal OverflowIncrementAmount { get; protected set; }

    /// <summary>Tarife üstüne eklenen KDV oranı (N11: 0,20). Fiyatlar tabloda KDV HARİÇ saklanır.</summary>
    public virtual decimal VatRate { get; protected set; }

    /// <summary>Posta Hizmet Bedeli oranı (N11: 0,0235 — 30 kg / 300 dm³ altı yasal posta tanımı).
    /// Trendyol gibi fiyata dahil eden kanallarda 0.</summary>
    public virtual decimal PostalServiceFeeRate { get; protected set; }

    /// <summary>Taşıyıcıya özel sabit ek bedel (N11'de yalnız Yurtiçi Kargo'nun 0,60 TL SMS ücreti). KDV'ye tabi.</summary>
    public virtual decimal ExtraFeeAmount { get; protected set; }

    /// <summary>Teslim edilemeyip satıcıya iade edilen gönderinin ek maliyeti — gönderi bedelinin oranı
    /// (N11: PTT 0,30 · çoğu taşıyıcı 0,50 · DHL/Horoz/Ceva Lojistik 1,00).</summary>
    public virtual decimal FailedDeliveryRate { get; protected set; }

    /// <summary>Ağır kargo (100+ desi) ek bedeli. <b>Bilinçli olarak NULL bırakıldı</b> (2026-07-26 Hakan
    /// kararı): resmi metindeki "4,250 / 3,000" yazımında virgülün ondalık mı binlik mi olduğu belirsiz —
    /// yanlış okunursa fiyatlama sessizce 1000× bozulur. Teyit gelene kadar hiçbir hesaba girmez.</summary>
    public virtual decimal? HeavyCargoAmount { get; protected set; }

    /// <summary>Bu tarife sürümünün yürürlük başlangıcı (tarih; saat taşımaz).</summary>
    public virtual DateTime EffectiveFrom { get; protected set; }

    /// <summary><c>null</c> = hâlâ yürürlükte. Yeni sürüm gelince eskisi kapatılır, SİLİNMEZ.</summary>
    public virtual DateTime? EffectiveTo { get; protected set; }

    /// <summary>Kaynak yayın etiketi (ör. "2026-07-26") — hangi ilandan geldiği izlenebilsin.</summary>
    public virtual string SourceVersion { get; protected set; } = string.Empty;

    public virtual bool IsActive { get; protected set; } = true;

    /// <summary>Desi fiyat satırları (0 = "Dosya"). Aggregate içi koleksiyon.</summary>
    public virtual List<MarketplaceShipmentTariffRate> Rates { get; protected set; } = new();

    /// <summary>Şartlı kargo baremi — sepet tutarı eşiğin altındayken uygulanan SABİT ücretler.
    /// Owned JSON (sorgulanmaz, satır sayısı çok az).</summary>
    public virtual List<MarketplaceShipmentConditionalRate> ConditionalRates { get; protected set; } = new();

    /// <summary>Baremin geçerli olduğu üst desi sınırı (N11 yayını: "10 desi ve altındaki gönderiler").
    /// <c>null</c> = kanalda barem yok.</summary>
    public virtual int? ConditionalMaxDesi { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetChannelCompanyExternalId(string? externalId)
    {
        ChannelCompanyExternalId = StringFieldGuard.EnsureOptionalText(
            externalId, nameof(ChannelCompanyExternalId), 1,
            MarketplaceShipmentTariffConsts.ChannelCompanyExternalIdMaxLength);
    }

    public virtual void SetOverflowIncrement(decimal amount)
    {
        EnsureNotNegative(amount, "TradeXpress:ShipmentTariff:OverflowIncrementNegative");
        OverflowIncrementAmount = amount;
    }

    /// <summary>Tarife üstüne eklenen vergi/harç kuralını set eder — kanaldan kanala değişir.</summary>
    public virtual void SetSurcharges(decimal vatRate, decimal postalServiceFeeRate, decimal extraFeeAmount)
    {
        EnsureRate(vatRate, "TradeXpress:ShipmentTariff:VatRateInvalid");
        EnsureRate(postalServiceFeeRate, "TradeXpress:ShipmentTariff:PostalServiceFeeRateInvalid");
        EnsureNotNegative(extraFeeAmount, "TradeXpress:ShipmentTariff:ExtraFeeNegative");

        VatRate = vatRate;
        PostalServiceFeeRate = postalServiceFeeRate;
        ExtraFeeAmount = extraFeeAmount;
    }

    public virtual void SetFailedDeliveryRate(decimal rate)
    {
        EnsureRate(rate, "TradeXpress:ShipmentTariff:FailedDeliveryRateInvalid");
        FailedDeliveryRate = rate;
    }

    /// <summary>Ağır kargo bedeli — <c>null</c> geçilebilir (teyit bekleyen alan).</summary>
    public virtual void SetHeavyCargoAmount(decimal? amount)
    {
        if (amount is { } value)
        {
            EnsureNotNegative(value, "TradeXpress:ShipmentTariff:HeavyCargoAmountNegative");
        }

        HeavyCargoAmount = amount;
    }

    /// <summary>Bu sürümü kapatır (yeni sürüm yürürlüğe girerken). Bitiş, başlangıçtan önce olamaz.</summary>
    public virtual void Close(DateTime effectiveTo)
    {
        if (effectiveTo.Date < EffectiveFrom)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:EffectiveToBeforeFrom");
        }

        EffectiveTo = effectiveTo.Date;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Desi satırını ekler/günceller. Aynı desi iki kez gelirse tutar GÜNCELLENİR (idempotan seed).</summary>
    public virtual void SetRate(int desi, decimal amount)
    {
        if (desi < 0)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:DesiNegative");
        }

        EnsureNotNegative(amount, "TradeXpress:ShipmentTariff:RateAmountNegative");

        var existing = Rates.FirstOrDefault(r => r.Desi == desi);
        if (existing is null)
        {
            Rates.Add(new MarketplaceShipmentTariffRate(Id, desi, amount));
            return;
        }

        existing.SetAmount(amount);
    }

    /// <summary>Şartlı barem satırını ekler. Aralıklar çakışamaz — çakışma sessiz yanlış fiyat demektir.</summary>
    public virtual void AddConditionalRate(decimal basketFrom, decimal? basketTo, decimal amount)
    {
        var rate = new MarketplaceShipmentConditionalRate(basketFrom, basketTo, amount);

        if (ConditionalRates.Any(r => r.Overlaps(rate)))
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:ConditionalRangeOverlap");
        }

        ConditionalRates.Add(rate);
    }

    public virtual void SetConditionalMaxDesi(int? maxDesi)
    {
        if (maxDesi is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:ConditionalMaxDesiNegative");
        }

        ConditionalMaxDesi = maxDesi;
    }

    public override string ToString()
    {
        return $"{Channel}/{CarrierCode} ({SourceVersion})";
    }

    private static void EnsureNotNegative(decimal value, string errorCode)
    {
        if (value < 0m)
        {
            throw new BusinessException(errorCode);
        }
    }

    private static void EnsureRate(decimal value, string errorCode)
    {
        if (value < 0m || value > 1m)
        {
            throw new BusinessException(errorCode);
        }
    }

    #endregion
}
