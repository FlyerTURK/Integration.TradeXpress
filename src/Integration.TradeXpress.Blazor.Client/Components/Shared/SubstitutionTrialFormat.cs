using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.Substitutions;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Muadil deneme tablosunun SAF metin biçimlendiricileri — hesaplama sayfası ile ürün Muadil sekmesi
/// AYNI formatı üretir (SSOT; kullanıcının onayladığı "1×10gr + 2×1gr" biçimi). Lokalize durum metinleri
/// (başarı/elenme nedenleri) BURADA DEĞİL — L gerektirir, tüketici bileşende kalır.</summary>
public static class SubstitutionTrialFormat
{
    /// <summary>"1×10gr + 2×1gr" bileşim metni (tüketim önceliği sırası korunur).</summary>
    public static string CombinationText(SubstitutionTrialDto trial)
    {
        return string.Join(" + ", trial.Lines.Select(
            l => $"{l.Count}×{l.PieceWeight.ToString("0.#####", CultureInfo.CurrentCulture)}gr"));
    }

    /// <summary>
    /// Varyant kolonu metni — bileşim satırlarıyla aynı sırada seçilen varyant kodları; katalog varyantı
    /// olmayan legacy satır "—" gösterir (maden koduyla karıştırılmaz).
    /// <para>Varyant adı YALNIZ ayırt ediyorsa yazılır: bir maden tek varyantla temsil ediliyorsa
    /// "3×ANAVARYANT + 2×ANAVARYANT" hiçbir bilgi taşımayan gürültüdür (2026-07-27 Hakan kararı).
    /// Böyle satırlarda yalnız adet gösterilir; tüm satırlar tekil varyantlıysa metin tamamen boş kalır ve
    /// kolon sessizce boşalır.</para>
    /// </summary>
    public static string VariantsText(SubstitutionTrialDto trial, IReadOnlySet<Guid>? multiVariantMetalIds = null)
    {
        var parcalar = trial.Lines
            .Where(l => multiVariantMetalIds is null || multiVariantMetalIds.Contains(l.MetalId))
            .Select(l => $"{l.Count}×{l.VariantCode ?? "—"}")
            .ToList();

        return string.Join(" + ", parcalar);
    }

    /// <summary>Denemelerin TAMAMINA bakarak birden çok varyantla temsil edilen madenleri bulur — varyant adı
    /// yalnız bu madenlerde ayırt edicidir (kombinasyon kodu üretimiyle aynı ölçüt).</summary>
    public static IReadOnlySet<Guid> MultiVariantMetalIds(IEnumerable<SubstitutionTrialDto> trials)
    {
        return trials
            .SelectMany(t => t.Lines)
            .GroupBy(l => l.MetalId)
            .Where(g => g.Select(l => l.VariantId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
    }

    /// <summary>Elenen aday etiketi — varyantlı adayda "MADEN/VARYANT" (hangi varyantın elendiği görünür).</summary>
    public static string FilteredOutLabel(SubstitutionFilteredOutDto filtered)
    {
        return filtered.VariantCode is { Length: > 0 } variantCode
            ? $"{filtered.MetalCode}/{variantCode}"
            : filtered.MetalCode;
    }
}
