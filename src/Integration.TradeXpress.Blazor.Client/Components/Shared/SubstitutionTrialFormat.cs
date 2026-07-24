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

    /// <summary>Varyant kolonu metni — bileşim satırlarıyla aynı sırada seçilen varyant kodları (Dilim-2 varyant
    /// boyutu); katalog varyantı olmayan legacy satır "—" gösterir (maden koduyla karıştırılmaz).</summary>
    public static string VariantsText(SubstitutionTrialDto trial)
    {
        return string.Join(" + ", trial.Lines.Select(l => $"{l.Count}×{l.VariantCode ?? "—"}"));
    }

    /// <summary>Elenen aday etiketi — varyantlı adayda "MADEN/VARYANT" (hangi varyantın elendiği görünür).</summary>
    public static string FilteredOutLabel(SubstitutionFilteredOutDto filtered)
    {
        return filtered.VariantCode is { Length: > 0 } variantCode
            ? $"{filtered.MetalCode}/{variantCode}"
            : filtered.MetalCode;
    }
}
