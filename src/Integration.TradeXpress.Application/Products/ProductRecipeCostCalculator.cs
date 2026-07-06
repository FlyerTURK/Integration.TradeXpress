using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Financials;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir reçetenin (varyant başına) <b>design-time net maliyetini</b> hesaplar — LEDGER'A YAZMAZ, saf hesap.
/// Her satırın bacak(lar)ını emtia ailesi + ödeme tipine göre çıkarır, <b>ülke para birimine</b> rebase eder.
/// Rebase <b>SATIŞ (val.Sell)</b> bacağı üzerinden (kullanıcı kararı 2026-07-05: toptan alım satış fiyatından).
/// VoucherLine/diğer değerleme yolları val.Buy'da kalır — yalnız reçete calculator'ı sell kullanır.
///
/// <para><b>Ödeme tipi semantiği (MetalProcessPanel'den türetildi, 2026-07-05 onaylı):</b>
/// Normal → İKİ bacak toplamı: metal <c>Total@MainUnit</c> + işçilik <c>PayTotal@PayUnit</c>
/// (PayTotal = PayFactor × (işçilik adet-bazlıysa Quantity, değilse Amount)).
/// Bedelli (WithCurrency) → TEK bacak: <c>PayTotal = Total × PayFactor @ PayUnit</c> — fişte iki bacak AYNI
/// değerin takasıdır (parite), ikisini toplamak çift sayım olurdu; maliyet = girilen bedel.</para>
///
/// <para>Değerleme dict'i DIŞARIDAN verilir (perf: <c>GetValuationByBaseAsync</c> ürün başına BİR KEZ çekilir,
/// tüm varyant/satırlarda yeniden kullanılır) → bu sınıf saf/senkron/DB'siz = kolay test edilir.</para>
/// </summary>
public class ProductRecipeCostCalculator : ITransientDependency
{
    /// <summary>
    /// Reçete satırlarının maliyetini hesaplar. <paramref name="naturalUnitSellByUnitId"/> = birim Id →
    /// "1 birim = X ülke parası" (SATIŞ bacağı; <c>GetValuationByBaseAsync(countryUnitId)</c> çıktısının Sell'i).
    /// Bir satırın gereken birim kuru çözülemezse satır <see cref="RecipeLineCost.MissingRate"/> işaretlenir
    /// (Cost=null), net toplama katılmaz. Total/PayTotal türetilmiş görüntü değerleri olarak DAİMA döner.
    /// </summary>
    public virtual RecipeCostResult Compute(
        IReadOnlyList<RecipeLineCostInput> lines,
        IReadOnlyDictionary<Guid, decimal> naturalUnitSellByUnitId,
        string countryCurrencyCode)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(naturalUnitSellByUnitId);

        var results = new List<RecipeLineCost>(lines.Count);
        decimal net = 0m;
        var anyMissing = false;

        foreach (var line in lines)
        {
            var result = ComputeLine(line, naturalUnitSellByUnitId);
            results.Add(result);

            if (result.MissingRate)
            {
                anyMissing = true;
            }
            else if (result.Cost is { } cost)
            {
                net += cost;
            }
        }

        return new RecipeCostResult(results, FinancialRounding.RoundAmount(net), countryCurrencyCode, anyMissing);
    }

    /// <summary>Tek satırın bacakları + maliyeti — aile ve ödeme tipine göre.</summary>
    private static RecipeLineCost ComputeLine(
        RecipeLineCostInput line,
        IReadOnlyDictionary<Guid, decimal> sellByUnit)
    {
        // Metal-bacaklı katalog satırı: ana bacak (Total@MainUnit) + ödeme tipine göre karşı bacak.
        if (line.ComponentType == RecipeComponentType.CatalogCommodity && IsMetalLegged(line.Family))
        {
            var grams = line is { IsQuantity: true, StableQuantity: > 0m }
                ? line.Quantity * line.StableQuantity
                : line.Amount;
            var total = grams * line.Factor;   // ana bacak toplamı (doğal birim; ör. HAS)

            if (line.PaymentType == ProcessPaymentType.WithCurrency)
            {
                // BEDELLİ → TEK bacak: maliyet = girilen bedel (Total × PayFactor @ PayUnit). Çift sayım YOK.
                var payTotal = total * line.PayFactor;
                if (line.PayUnitId is not { } payUnit || !sellByUnit.TryGetValue(payUnit, out var paySell))
                {
                    return new RecipeLineCost(null, MissingRate: true, total, payTotal);
                }

                return new RecipeLineCost(
                    FinancialRounding.RoundAmount(payTotal * paySell), MissingRate: false, total, payTotal);
            }

            // NORMAL → metal bacağı + işçilik bacağı (işçilik adet-bazlıysa Quantity, değilse Amount üzerinden).
            var laborTotal = line.PayFactor * (line.LaborByQuantity ? line.Quantity : line.Amount);

            if (line.NaturalUnitId is not { } mainUnit || !sellByUnit.TryGetValue(mainUnit, out var mainSell))
            {
                return new RecipeLineCost(null, MissingRate: true, total, laborTotal);
            }

            var costValue = total * mainSell;

            if (laborTotal != 0m)
            {
                if (line.PayUnitId is not { } laborUnit || !sellByUnit.TryGetValue(laborUnit, out var laborSell))
                {
                    return new RecipeLineCost(null, MissingRate: true, total, laborTotal);
                }

                costValue += laborTotal * laborSell;
            }

            return new RecipeLineCost(FinancialRounding.RoundAmount(costValue), MissingRate: false, total, laborTotal);
        }

        // Parasal katalog (Jewelry/Stone): EntryPrice × (adet ya da gram) @ EntryPrice birimi. Pay bacağı yok.
        if (line.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            var quantity = line.PriceByQuantity ? line.Quantity : line.Amount;
            var total = line.EntryPrice * quantity;

            if (line.NaturalUnitId is not { } unit || !sellByUnit.TryGetValue(unit, out var sell))
            {
                return new RecipeLineCost(null, MissingRate: true, total, 0m);
            }

            return new RecipeLineCost(FinancialRounding.RoundAmount(total * sell), MissingRate: false, total, 0m);
        }

        // Hizmet / Manuel: sabit tutar @ birim. Bacak kavramı yok.
        var manual = line.ManualAmount ?? 0m;
        if (line.ManualUnitId is not { } manualUnit || !sellByUnit.TryGetValue(manualUnit, out var manualSell))
        {
            return new RecipeLineCost(null, MissingRate: true, 0m, 0m);
        }

        return new RecipeLineCost(FinancialRounding.RoundAmount(manual * manualSell), MissingRate: false, 0m, 0m);
    }

    /// <summary>Metal-bacaklı aile mi (milyem×miktar → FollowingUnit): Metal/Scrap/Future.</summary>
    private static bool IsMetalLegged(ProcessType? family)
    {
        return family is ProcessType.Metal or ProcessType.Scrap or ProcessType.Future;
    }
}

/// <summary>Bir reçete satırının hesaba giren TÜM verisi — AppService katalogdan (StableQuantity/EntryPrice/
/// LaborType/adet bayrakları) çözüp doldurur; calculator saf math yapar (DB'siz → test edilebilir).</summary>
public sealed record RecipeLineCostInput(
    RecipeComponentType ComponentType,
    ProcessType? Family,
    decimal Quantity,
    decimal Amount,
    decimal Factor,
    bool IsQuantity,         // metal: adetli mi (adet→gram için)
    decimal StableQuantity,  // metal: adet başına gram
    bool PriceByQuantity,    // parasal: fiyat adet başına mı (aksi gram)
    decimal EntryPrice,      // parasal: katalog giriş fiyatı
    Guid? NaturalUnitId,     // ana/doğal birim (MainUnit rolü: FollowingUnit ya da EntryPrice birimi)
    ProcessPaymentType PaymentType,
    decimal PayFactor,       // Normal: işçilik rate'i; Bedelli: 1 ana-birim başına bedel
    Guid? PayUnitId,         // karşı bacak birimi
    bool LaborByQuantity,    // metal: işçilik adet-bazlı mı (Metal.LaborType==Quantity)
    decimal? ManualAmount,
    Guid? ManualUnitId);

/// <summary>Tek satırın hesap sonucu — <see cref="Cost"/> null ⇔ <see cref="MissingRate"/>.
/// <see cref="Total"/>/<see cref="PayTotal"/> türetilmiş görüntü değerleri (doğal/karşı birimde) — daima dolu.</summary>
public sealed record RecipeLineCost(decimal? Cost, bool MissingRate, decimal Total, decimal PayTotal);

/// <summary>Reçetenin net-maliyet sonucu — satır tutarları + net toplam + ülke birim kodu + eksik-kur bayrağı.</summary>
public sealed record RecipeCostResult(
    IReadOnlyList<RecipeLineCost> Lines,
    decimal Net,
    string CurrencyCode,
    bool AnyMissingRate);
