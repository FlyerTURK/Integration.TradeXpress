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
        var lineCosts = new decimal?[lines.Count];   // her satırın GÖSTERİLEN maliyeti (null ⇔ MissingRate)
        decimal net = 0m;                             // devreden = delta toplamı (= gerçek koşan toplam)
        var anyMissing = false;
        var anyMissingSoFar = false;

        // Satırlar LineOrder sırasında; Hizmet (türevsel) satır işlenirken üsttekilerin maliyetleri (lineCosts) ve
        // devreden (net) hazır. Her satırın maliyeti = KENDİ katkısı (fiziki: gerçek maliyet; Hizmet: uygulanan
        // bedel/fee) → net = basit toplam. Ara Toplam = o satır DAHİL koşan toplam.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var result = line.ComponentType == RecipeComponentType.Service
                ? ComputeDerived(line, i, lineCosts, net, anyMissingSoFar, naturalUnitSellByUnitId)
                : ComputeLine(line, naturalUnitSellByUnitId);

            if (result.MissingRate)
            {
                anyMissing = true;
                anyMissingSoFar = true;
                lineCosts[i] = null;
            }
            else
            {
                lineCosts[i] = result.Cost;
                net += result.Cost!.Value;
            }

            results.Add(result with { RunningSubtotal = FinancialRounding.RoundAmount(net) });
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

    /// <summary>Hizmet (türevsel) satırın maliyeti — devralınan taban (AllAbove: devreden <paramref name="runningNet"/>;
    /// SelectedLines: seçili üst satırların maliyet toplamı) üstüne işlem. Taban güvenilmezse
    /// (AllAbove'da üstte kur-eksik satır; SelectedLines'ta boş/ileri-öz/eksik-kur referans) MissingRate.
    /// Dönen <see cref="RecipeLineCost.Cost"/> = uygulanan bedel (fee = sonuç − taban); <see cref="RecipeLineCost.AppliedBase"/> = taban.</summary>
    private static RecipeLineCost ComputeDerived(
        RecipeLineCostInput line, int index, decimal?[] lineCosts, decimal runningNet, bool anyMissingSoFar,
        IReadOnlyDictionary<Guid, decimal> sellByUnit)
    {
        decimal baseValue;
        if (line.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines)
        {
            if (line.DerivedSourceOrdinals is not { Count: > 0 } ordinals)
            {
                return new RecipeLineCost(null, MissingRate: true, 0m, 0m);
            }

            decimal sum = 0m;
            foreach (var ordinal in ordinals)
            {
                // Yalnız kendinden ÖNCEKİ (ordinal < index) + kur-eksik olmayan satırlar → aksi MissingRate (fail-fast).
                if (ordinal < 0 || ordinal >= index || lineCosts[ordinal] is not { } cost)
                {
                    return new RecipeLineCost(null, MissingRate: true, 0m, 0m);
                }

                sum += cost;
            }

            baseValue = sum;
        }
        else
        {
            // AllAbove: devreden. Üstte kur-eksik bir satır varsa taban sessizce eksik olur → türev de MissingRate.
            if (anyMissingSoFar)
            {
                return new RecipeLineCost(null, MissingRate: true, 0m, 0m);
            }

            baseValue = runningNet;
        }

        // Add = mutlak tutar (operand @ opsiyonel birim → ülke birimine rebase). Diğer işlemler tabana ORANLA.
        if (line.DerivedOperation == RecipeDerivedOperation.Add)
        {
            var amount = line.DerivedOperand;
            if (line.PayUnitId is { } addUnit)
            {
                if (!sellByUnit.TryGetValue(addUnit, out var addSell))
                {
                    return new RecipeLineCost(null, MissingRate: true, 0m, 0m, AppliedBase: baseValue);
                }

                amount = line.DerivedOperand * addSell;
            }

            return new RecipeLineCost(FinancialRounding.RoundAmount(amount), MissingRate: false, 0m, 0m, AppliedBase: baseValue);
        }

        if (!TryApplyDerivedOperation(baseValue, line.DerivedOperation, line.DerivedOperand, out var resultValue))
        {
            return new RecipeLineCost(null, MissingRate: true, 0m, 0m, AppliedBase: baseValue);
        }

        // Satır maliyeti = UYGULANAN BEDEL (fee = sonuç − taban); net'e bu eklenir (taban zaten devreden içinde
        // sayılı → çift sayım yok). AppliedBase = "Uygulanacak Bedel" kolonu (işlemin tabanı).
        var fee = FinancialRounding.RoundAmount(resultValue - baseValue);
        return new RecipeLineCost(fee, MissingRate: false, 0m, 0m, AppliedBase: baseValue);
    }

    /// <summary>Devralınan tabana işlemi uygular (delta modeli): Add=taban+operand, Multiply=taban×operand,
    /// Percent=taban×(1+operand/100), GrossUp=taban÷(1−operand/100). GrossUp'ta payda ≤ 0 ise BAŞARISIZ
    /// (fail-safe; domain [0,100) zorlar ama calculator saf/savunmalı kalır).</summary>
    private static bool TryApplyDerivedOperation(
        decimal baseValue, RecipeDerivedOperation? operation, decimal operand, out decimal result)
    {
        switch (operation)
        {
            case RecipeDerivedOperation.Add:
                result = baseValue + operand;
                return true;
            case RecipeDerivedOperation.Multiply:
                result = baseValue * operand;
                return true;
            case RecipeDerivedOperation.Percent:
                result = baseValue * (1m + operand / 100m);
                return true;
            case RecipeDerivedOperation.GrossUp:
                var denominator = 1m - operand / 100m;
                if (denominator <= 0m)
                {
                    result = 0m;
                    return false;
                }

                result = baseValue / denominator;
                return true;
            default:
                result = 0m;
                return false;
        }
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
    Guid? ManualUnitId,
    // ── türev/devralan satır (3b) — yalnız ComponentType == Derived'da dolu; aksi null/0/boş ──
    RecipeDerivedBaseMode? DerivedBaseMode = null,
    RecipeDerivedOperation? DerivedOperation = null,
    decimal DerivedOperand = 0m,
    IReadOnlyList<int>? DerivedSourceOrdinals = null);

/// <summary>Tek satırın hesap sonucu — <see cref="Cost"/> (Satır Maliyeti = satırın katkısı; null ⇔ <see cref="MissingRate"/>).
/// <see cref="Total"/>/<see cref="PayTotal"/> fiziki satırın doğal/karşı birim görüntü değerleri. <see cref="AppliedBase"/> =
/// Hizmet satırının uyguladığı taban ("Uygulanacak Bedel"; fiziki satırda null). <see cref="RunningSubtotal"/> = o satır
/// DAHİL koşan toplam ("Ara Toplam", ülke birimi; Compute doldurur).</summary>
public sealed record RecipeLineCost(
    decimal? Cost, bool MissingRate, decimal Total, decimal PayTotal,
    decimal? AppliedBase = null, decimal? RunningSubtotal = null);

/// <summary>Reçetenin net-maliyet sonucu — satır tutarları + net toplam + ülke birim kodu + eksik-kur bayrağı.</summary>
public sealed record RecipeCostResult(
    IReadOnlyList<RecipeLineCost> Lines,
    decimal Net,
    string CurrencyCode,
    bool AnyMissingRate);
