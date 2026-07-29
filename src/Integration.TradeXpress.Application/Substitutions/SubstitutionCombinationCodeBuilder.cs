using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.Framework;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil kombinasyonunun DETERMİNİSTİK kodunu üretir — "G5.0GR995X1+G1.0GR995X3" (maden koduna göre sıralı,
/// aynı bileşim her hesapta AYNI kod). SAF STATİK.
///
/// <para><b>Neden ayrı sınıf (SSOT):</b> aynı kodu İKİ yer üretiyor — varyantları kalıcılaştıran
/// <c>SubstitutionVariantMaterializer</c> (kayıt anı) ve hesap sonucunu döndüren
/// <c>SubstitutionCalculationAppService</c> (kaydetmeden önizleme). İki ayrı uygulama olsaydı biri
/// değiştiğinde önizlemedeki kod kayıttakiyle ayrışır, kullanıcı kaydettiğinde varyantlar "yeniden doğmuş"
/// gibi görünür ve kanal SKU bağları kopardı.</para>
/// </summary>
public static class SubstitutionCombinationCodeBuilder
{
    /// <summary>Varyant adı koda YALNIZ ayırt ediyorsa girer: bir maden tek varyantla temsil ediliyorsa
    /// ".ANAVARYANT" her kombinasyonda tekrarlanan, hiçbir şeyi ayırmayan gürültüdür (2026-07-27 kararı).
    /// Çoklu varyantta ise ŞART — aynı madenin iki varyantı farklı işçilik/maliyet taşır.</summary>
    public static IReadOnlySet<Guid> MultiVariantMetalIds(IEnumerable<SubstitutionTrialDto> trials)
    {
        return trials
            .SelectMany(t => t.Lines)
            .GroupBy(l => l.MetalId)
            .Where(g => g.Select(l => l.VariantId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
    }

    /// <summary>
    /// Bileşim → kod: <c>"1xG5.0GR995+4xG1.0GR995"</c>. Adet ÖNDE ve satırlar BÜYÜK parçadan küçüğe sıralı —
    /// okurken kombinasyonun ağırlığı ilk bakışta anlaşılsın (2026-07-27 Hakan kararı; öncesinde alfabetik
    /// sıra + sondaki "X4" biçimi kullanılıyordu). Sıra ölçütü parça ağırlığı olduğundan kod, kalemlerin
    /// listede hangi düzende geldiğinden bağımsız ve DETERMİNİSTİKTİR.
    /// <para>64 karakteri aşarsa içerikten türetilen KARARLI son ekle kısaltılır (<c>GetHashCode</c> DEĞİL —
    /// process'e göre değişir, kimlik bozulurdu).</para>
    /// </summary>
    public static string Build(SubstitutionTrialDto trial, IReadOnlySet<Guid> multiVariantMetalIds)
    {
        var parts = OrderByPieceWeightDescending(trial)
            .Select(l =>
            {
                // TAM metal kodu (boşluksuz) — "G5.0 GR 995" ile "G5.0 GR 9999" AYNI kimliğe inmesin.
                var metalFull = l.MetalCode.Replace(" ", string.Empty);
                var variantPart = string.IsNullOrEmpty(l.VariantCode) || !multiVariantMetalIds.Contains(l.MetalId)
                    ? string.Empty
                    : "." + l.VariantCode;
                return $"{l.Count}x{metalFull}{variantPart}";
            });

        var code = string.Join("+", parts).NormalizeAsCode();

        if (code.Length <= EntityVariantConsts.VariantCodeMaxLength)
        {
            return code;
        }

        var suffix = "~" + StableHash(code);
        var keep = EntityVariantConsts.VariantCodeMaxLength - suffix.Length;
        return code[..keep] + suffix;
    }

    /// <summary>
    /// Bileşimin OKUNABİLİR özeti: <c>"1×5gr + 4×1gr"</c> — kodun aksine maden kodu değil PARÇA AĞIRLIĞI
    /// gösterir; kullanıcı "kaç gramlıktan kaç tane" sorusunun cevabını burada okur. Varyant tablosundaki
    /// "Kombinasyon" kolonunu besler (materyalize varyantlarda nitelik bağı olmadığı için o kolon boş kalıyordu).
    /// Sıra kodla AYNI: büyük parçadan küçüğe.
    /// </summary>
    public static string BuildSummary(SubstitutionTrialDto trial, IReadOnlySet<Guid>? multiVariantMetalIds = null)
    {
        var bilesim = string.Join(" + ", OrderByPieceWeightDescending(trial)
            .Select(l =>
            {
                var agirlik = $"{l.Count}×{l.PieceWeight.ToString("0.#####", CultureInfo.InvariantCulture)}gr";

                // Varyant adı parantez içinde — YALNIZ ayırt ettiğinde: "1×5gr(Ambalajlı) + 4×1gr(Ambalajsız)".
                // Tek varyantlı madende "(ANAVARYANT)" hiçbir şey ayırmaz, yalnız satırı uzatır.
                var ayirtEdiyor = multiVariantMetalIds?.Contains(l.MetalId) == true
                    && !string.IsNullOrEmpty(l.VariantCode);

                return ayirtEdiyor ? $"{agirlik}({l.VariantCode})" : agirlik;
            }));

        // TOPLAM ÖZETİ (parça adedi + gram) burada EKLENMEZ: metin lokalize olmalı ("Toplam 5 parça 10,02 gr")
        // ve bu katmanda localizer yok. Sayılar zaten DTO'da (PieceCount/TotalWeight) — cümleyi kuran taraf
        // kullanıcının dilini bilen istemcidir. Burada yalnız dilden bağımsız bileşim üretilir.
        return bilesim;
    }

    /// <summary>Kod ve özetin ORTAK sırası — büyük parçadan küçüğe; eşitlikte maden kodu (deterministiklik).</summary>
    private static IEnumerable<SubstitutionTrialLineDto> OrderByPieceWeightDescending(SubstitutionTrialDto trial)
    {
        return trial.Lines
            .OrderByDescending(l => l.PieceWeight)
            .ThenBy(l => l.MetalCode, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>FNV-1a — process'ten bağımsız, kararlı kısa özet (kod kısaltma son eki).</summary>
    private static string StableHash(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= prime;
            }

            return hash.ToString("x8");
        }
    }
}
