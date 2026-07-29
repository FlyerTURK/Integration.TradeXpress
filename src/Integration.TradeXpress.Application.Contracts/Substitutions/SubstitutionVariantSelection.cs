using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Hesaplanan kombinasyonlardan HANGİLERİNİN varyant olacağını seçer — SAF STATİK.
///
/// <para><b>Neden Contracts'ta ve neden ayrı sınıf (SSOT):</b> bu kuralı İKİ taraf uyguluyor — sunucu
/// kayıt anında (<c>SubstitutionVariantMaterializer</c>) ve istemci kaydetmeden önizlemede
/// (ürün formu). İki ayrı kopya olsaydı biri değiştiğinde önizlemede görünen varyantlarla kaydedilenler
/// ayrışır, kullanıcı "kaydettim, varyantlarım değişti" derdi. Contracts her iki katmandan da görülebilir.</para>
/// </summary>
public static class SubstitutionVariantSelection
{
    /// <summary>
    /// Varyanta dönüşecek kombinasyonlar: yalnız BAŞARILI ve sıralanmış adaylar, en iyiden başlayarak.
    /// <c>Single</c> modda yalnız birincisi (ana varyant), <c>Multi</c> modda tavana kadar.
    /// Dönen listenin İLK elemanı ana varyanttır.
    /// </summary>
    public static List<SubstitutionTrialDto> Select(
        IEnumerable<SubstitutionTrialDto> trials,
        SubstitutionVariantMode variantMode)
    {
        var successful = trials
            .Where(t => t.Success && t.Rank is not null)
            .OrderBy(t => t.Rank!.Value)
            .ToList();

        if (successful.Count == 0)
        {
            return successful;
        }

        // ANA VARYANT = GRAM BAŞINA en ucuz kombinasyon (2026-07-27 Hakan kararı). Rank toplam maliyete göre
        // sıralar; toplamı düşük olan kombinasyon daha AZ gram taşıyor olabilir ve gram başına pahalıya gelir.
        // Müşteriye "en uygun" diye sunulan varyant birim fiyatı en düşük olandır. Rank listenin geri kalanının
        // sırasını belirlemeye devam eder — değişen yalnız hangisinin BAŞA geçtiği.
        var mainVariant = successful.OrderBy(UnitCost).First();
        var ordered = new List<SubstitutionTrialDto> { mainVariant };
        ordered.AddRange(successful.Where(t => !ReferenceEquals(t, mainVariant)));

        return variantMode == SubstitutionVariantMode.Multi
            ? ordered.Take(ProductConsts.SubstitutionMaterializedVariantMax).ToList()
            : ordered.Take(1).ToList();
    }

    /// <summary>Gram başına maliyet. Ağırlık 0 ise (olmaması gereken durum) sıralamada en sona atılır —
    /// sıfıra bölme yerine sessizce "en pahalı" sayılır, çünkü böyle bir kombinasyon ana varyant olmamalı.</summary>
    private static decimal UnitCost(SubstitutionTrialDto trial)
    {
        return trial.TotalWeight > 0m ? trial.TotalCost / trial.TotalWeight : decimal.MaxValue;
    }
}
