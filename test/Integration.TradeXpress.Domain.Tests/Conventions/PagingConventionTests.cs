using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Sayfalama konvansiyonunun MEKANİK güvenlik ağı (governance Katman 2).
///
/// <para><b>Kural:</b> <c>ListRequestDto</c> taşıyan bir sorguda sayfalama ELLE yazılmaz —
/// <c>ApplyPaging(input)</c> kullanılır.</para>
///
/// <para><b>Neden konvansiyon testi:</b> <c>MaxResultCount</c> artık pozitif olmayan değerlerle "TÜM kayıtlar"
/// (<c>ListRequestDto.AllPages</c>) anlamına gelir. Elle yazılan <c>Take(input.MaxResultCount)</c> bu durumda
/// <c>Take(-1)</c> olur ve LINQ bunu <b>istisna atmadan BOŞ liste</b> olarak değerlendirir → grid'in "Tümü"
/// seçeneği sessizce 0 satır gösterir. Sessiz olduğu için code review'da da kaçar; bu yüzden derlemeye değil
/// TESTE bağlandı.</para>
///
/// <para><b>Tarihçe (2026-07-25):</b> tam tersi de yaşandı — 61 çağrı yeri <c>MaxResultCount = 1000</c> yazıyordu,
/// sunucu sessizce 200'e kırpıyordu ve 249 ülkenin 49'u hiçbir combo'da görünmüyordu. İki yön de SESSİZ hataydı;
/// bu test o sessizliği kapatır.</para>
///
/// <para>Allow-list = bilinçli istisna: <c>ListRequestDto</c>'dan TÜREMEYEN kendi sözleşmesi olan DTO'lar
/// (ör. <c>VoucherListRequestDto</c>) ile helper'ın kendi tanımı. Listeye ekleme yaparken yanına gerekçe yaz.</para>
/// </summary>
public class PagingConventionTests
{
    // Elle sayfalama: ".Take(<herhangi>.MaxResultCount)" — Skip'li ya da Skip'siz, IQueryable ya da IEnumerable.
    // Yalnız Take'e bakılır: asıl tehlike odur (Take(-1) sessizce boş döner; Skip(-1) zaten istisna atar).
    private static readonly Regex ManualTakeRegex =
        new(@"\.Take\(\s*\w+\.MaxResultCount", RegexOptions.Compiled);

    /// <summary>Bilinçli istisnalar — repo köküne göreli, forward-slash.</summary>
    private static readonly HashSet<string> Allowed = new()
    {
        // Helper'ın KENDİ tanımı: AllPages dalını geçtikten sonra gerçek Take'i burası yapar.
        "src/Integration.Framework.Application.Contracts/Base/Querying/ListQueryableExtensions.cs",

        // VoucherListRequestDto ListRequestDto'dan TÜREMEZ (kendi SkipCount/MaxResultCount'u var) →
        // AllPages semantiği orada geçerli değil, elle sayfalama DOĞRU davranış.
        "src/Integration.TradeXpress.Application/Vouchers/VoucherAppService.cs",
    };

    [Fact]
    public void Services_must_not_page_manually_use_ApplyPaging()
    {
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.cs"))
        {
            var relative = ConventionSource.RelativePath(file);
            if (Allowed.Contains(relative))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (ManualTakeRegex.IsMatch(text))
            {
                violations.Add(relative);
            }
        }

        violations.ShouldBeEmpty(
            "Elle sayfalama YASAK — 'Skip/Take(input.MaxResultCount)' yerine 'query.ApplyPaging(input)' kullan. " +
            "Gerekçe: MaxResultCount <= 0 'TÜM kayıtlar' demektir; elle yazılan Take(-1) istisna ATMADAN boş liste " +
            "döndürür ve grid'in 'Tümü' seçeneği sessizce 0 satır gösterir. Bilinçli istisna ise " +
            $"{nameof(PagingConventionTests)}.{nameof(Allowed)} listesine GEREKÇESİYLE ekle.{System.Environment.NewLine}" +
            string.Join(System.Environment.NewLine, violations));
    }
}
