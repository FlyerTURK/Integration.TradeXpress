namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Tarifenin TEK desi satırı: "bu taşıyıcıda {Desi} desi = {Amount} TL" (vergi/harç HARİÇ).
/// <para><see cref="Desi"/> 0 = pazaryerinin "Dosya" satırı. Tablo <c>TabulatedMaxDesi</c>'ye kadar gider;
/// üstü tarifenin <c>OverflowIncrementAmount</c> katsayısıyla doğrusal uzatılır.</para>
/// <para>Owned JSON DEĞİL ayrı tablo: taşıyıcı başına 101 satır × kanal × sürüm birikir ve "şu desi kaç TL"
/// sorgusu doğrudan indeksten cevaplanmalı; JSON'da her okuma tüm listeyi çözerdi.</para>
/// <para><b>Owned tip (kendi Guid Id'si YOK):</b> doğal anahtarı <c>(TariffId, Desi)</c> — aynı tarifede bir
/// desi yalnız bir kez vardır. Ayrı bir aggregate yapılsaydı 101 satırın her birine ayrıca kimlik ve denetim
/// alanı üretmek gerekirdi; satır bağımsız yaşamıyor, tarifesiyle birlikte doğup ölüyor.</para>
/// </summary>
public class MarketplaceShipmentTariffRate
{
    #region Constructors

    protected MarketplaceShipmentTariffRate()
    {
    }

    public MarketplaceShipmentTariffRate(Guid tariffId, int desi, decimal amount)
    {
        TariffId = tariffId;
        Desi = desi;
        SetAmount(amount);
    }

    #endregion

    #region Properties

    /// <summary>Sahip tarife (aggregate içi FK; navigation YOK — koleksiyon üzerinden erişilir).</summary>
    public virtual Guid TariffId { get; protected set; }

    /// <summary>Desi/kg basamağı; 0 = "Dosya".</summary>
    public virtual int Desi { get; protected set; }

    /// <summary>Pazaryerinin ilan ettiği ÇIPLAK tutar — KDV ve posta hizmet bedeli DAHİL DEĞİL.</summary>
    public virtual decimal Amount { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetAmount(decimal amount)
    {
        if (amount < 0m)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:RateAmountNegative");
        }

        Amount = amount;
    }

    public override string ToString()
    {
        return $"{Desi} → {Amount:N2}";
    }

    #endregion
}
