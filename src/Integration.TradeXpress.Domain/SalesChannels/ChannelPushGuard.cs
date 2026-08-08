using System;
using System.Globalization;
using Volo.Abp;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// KANAL PUSH EMNİYET KURALLARI — kanal-ürün başına stok payı ve fiyat bandının TEK kaynağı.
///
/// <para><b>Neden ortak sınıf:</b> N11 ve Trendyol kanal-ürünlerinin ortak bir tabanı yok (her pazaryerinin alanları
/// farklı), ama bu iki kural ikisinde de HARFİ HARFİNE aynı. Aritmetiği ve doğrulamayı iki entity'ye kopyalamak
/// connascence-of-algorithm üretirdi: biri düzeltilir, diğeri sessizce eski kalırdı — ve fark ancak canlıda,
/// aşırı satış olarak görünürdü.</para>
///
/// <para><b>Emniyet payı</b> aşırı satış savunmasının DÖRDÜNCÜ katmanıdır (mevcut üçü: <c>bundle=false</c> ·
/// stok bitince adet-0 · sipariş rezervasyonu). Diğerleri "aynı an / aynı sepet" çakışmasını kapatır; pay,
/// senkron turları ARASINDAKİ pencereyi daraltır — kanalda gerçekte olandan bilerek az adet gösterir.
/// Kapatmaz, daraltır: bu yüzden opsiyoneldir ve kullanıcının kararıdır.</para>
///
/// <para><b>Fiyat bandı</b> 15 dakikada bir İNSANSIZ fiyat yazan repricing motorunun son emniyetidir. Bant dışına
/// düşen fiyat KIRPILMAZ — kırpmak, motorun ürettiği yanlış sayıyı meşru bir fiyata dönüştürüp hatayı gizlerdi.
/// Push DURUR ve gerekçe operatöre görünür (aynı felsefe: kursuz birime uydurma kur yazılmaz).</para>
/// </summary>
public static class ChannelPushGuard
{
    /// <summary>Push satırının NİHAİ stoğuna emniyet payını uygular: <c>max(0, stok − pay)</c>.
    ///
    /// <para>Stok <c>null</c> ise (çözülemedi) pay uygulanmaz — <c>null</c> "sıfır" değil "bilinmiyor"dur ve
    /// fail-fast kararı çağıranındır. Pay stoktan büyükse sonuç 0'dır; negatif adet ASLA üretilmez.</para></summary>
    public static int? ApplySafetyStock(int? stock, int? safetyStock)
    {
        if (stock is not { } value)
        {
            return null;
        }

        return Math.Max(0, value - (safetyStock ?? 0));
    }

    /// <summary>Emniyet payını doğrular ve normalleştirir. Negatif pay stok ŞİŞİRİRDİ (aşırı satışın ta kendisi) →
    /// fail-fast. <c>0</c> ile <c>null</c> davranışça aynıdır ama ayrımı korunur: <c>0</c> "bilinçle pay yok"
    /// beyanıdır, <c>null</c> "hiç dokunulmadı".</summary>
    public static int? NormalizeSafetyStock(int? safetyStock)
    {
        if (safetyStock is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Product:SafetyStockNegative")
                .WithData("SafetyStock", value);
        }

        return safetyStock;
    }

    /// <summary>Fiyat bandını doğrular ve normalleştirir: negatif sınır ve <c>min &gt; max</c> reddedilir
    /// (kaydedilseydi HİÇBİR fiyat bandı geçemez, ürün sessizce push edilemez hâle gelirdi).
    /// Tek uçlu bant meşrudur — yalnız taban ya da yalnız tavan konabilir.</summary>
    public static (decimal? Min, decimal? Max) NormalizePriceBand(decimal? minPrice, decimal? maxPrice)
    {
        if (minPrice is { } min && min < 0m)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Product:PriceBandNegative").WithData("Price", min);
        }

        if (maxPrice is { } max && max < 0m)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Product:PriceBandNegative").WithData("Price", max);
        }

        if (minPrice is { } lo && maxPrice is { } hi && lo > hi)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Product:PriceBandInverted")
                .WithData("MinPrice", lo)
                .WithData("MaxPrice", hi);
        }

        return (minPrice, maxPrice);
    }

    /// <summary>Tek bir push fiyatı banda uyuyor mu. Fiyat <c>null</c> ise banda TAKILMAZ — fiyatsızlığın kendi
    /// fail-fast'i vardır (<c>PriceMissingForPush</c>) ve iki kuralın aynı eksikliği iki farklı hatayla anlatması
    /// operatörü şaşırtır.</summary>
    public static bool IsWithinPriceBand(decimal? price, decimal? minPrice, decimal? maxPrice)
    {
        if (price is not { } value)
        {
            return true;
        }

        return (minPrice is not { } min || value >= min)
            && (maxPrice is not { } max || value <= max);
    }

    /// <summary>Banda uymayan fiyatı fail-closed reddeder — hata verisi operatörün hangi SKU'da, hangi fiyatla,
    /// hangi bandın dışına çıktığını GÖRMESİ için doludur (yalnız "başarısız" demek teşhis ettirmez).
    /// Kod kanal-agnostiktir: kural ortak olduğu için tek lokalizasyon anahtarı yaşar, kanal adı veriyle taşınır.</summary>
    public static void EnsureWithinPriceBand(
        string channel, string stockCode, decimal? price, decimal? minPrice, decimal? maxPrice)
    {
        if (IsWithinPriceBand(price, minPrice, maxPrice) || price is not { } value)
        {
            return;
        }

        // Tek uçlu bantta boş kalan sınır mesajda "-" görünür — "0" yazmak, konulmamış bir tabanı konulmuş
        // gibi gösterip operatörü yanlış yere baktırırdı.
        throw new BusinessException("TradeXpress:SalesChannel:Product:PriceOutOfBand")
            .WithData("Channel", channel)
            .WithData("StockCode", stockCode)
            .WithData("Price", value)
            .WithData("MinPrice", Describe(minPrice))
            .WithData("MaxPrice", Describe(maxPrice));
    }

    private static string Describe(decimal? limit)
    {
        return limit?.ToString("N2", CultureInfo.CurrentCulture) ?? "-";
    }
}
