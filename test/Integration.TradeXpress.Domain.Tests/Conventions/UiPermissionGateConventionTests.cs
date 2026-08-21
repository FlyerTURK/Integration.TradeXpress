using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// UI İZİN KATMANI SESSİZCE AÇILAMAZ (governance Katman 2).
///
/// <para><b>Kapatılan açık (2026-08-07):</b> <c>CrudPageBase.SetPermissionsAsync</c> içinde
/// <c>!OperatingSystem.IsBrowser()</c> koşullu bir baypas vardı ve üç izin bayrağını da koşulsuz <c>true</c>
/// yazıyordu. Gerekçesi "Blazor Server'da ABP principal accessor deadlock edebilir" idi — ama bu uygulama
/// Blazor SERVER'dır, yani dal HER ZAMAN koşuyordu: UI izin katmanı fiilen ÖLÜYDÜ. Yıllarca hiçbir hata
/// üretmedi, çünkü "herkese her düğmeyi göstermek" bir istisna fırlatmaz.</para>
///
/// <para><b>Bu test neden metin tarıyor:</b> baypas bir DAVRANIŞ değil bir KISA DEVREYDİ — geri eklendiğinde
/// izin testleri de yeşil kalırdı (her şey izinli görünür). Yakalanabilecek tek yer kaynağın kendisidir.
/// Aynı gerekçe <c>RazorConventionTests</c>'in metin taramasıyla aynı.</para>
///
/// <para><b>Hatırlatma:</b> bu bir güvenlik katmanı DEĞİL. Sunucu tarafı <c>[Authorize]</c> ile korunur
/// (<c>AppServiceConventionTests</c> onu ayrıca zorlar). Buradaki kural dürüstlükle ilgilidir: kullanıcı
/// yapamayacağı işin düğmesini görmesin.</para>
/// </summary>
public class UiPermissionGateConventionTests
{
    private const string CrudPageBasePath =
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudPageBase.cs";

    /// <summary>Ortam kontrolüyle izin bayraklarını koşulsuz açan kısa devre GERİ GELEMEZ.</summary>
    [Fact]
    public void Crud_page_base_must_not_short_circuit_permission_flags_by_environment()
    {
        var source = ReadCrudPageBase();
        var body = ExtractSetPermissionsBody(source);

        body.ShouldNotContain(
            "OperatingSystem.IsBrowser",
            Case.Sensitive,
            "SetPermissionsAsync ortam kontrolüyle kısa devre yapamaz: bu uygulama Blazor SERVER olduğu için " +
            "o dal HER ZAMAN koşar ve UI izin katmanını tamamen devre dışı bırakır (yetkisiz kullanıcı da tüm " +
            "düğmeleri görür). Deadlock endişesi geçerli değil — LookupComboBox aynı AuthorizationService " +
            "çağrısını Server modda sorunsuz yapıyor.");
    }

    /// <summary>Üç bayrak da GERÇEK izin sorgusundan gelmeli — sabit <c>true</c> atanamaz.</summary>
    [Fact]
    public void Crud_page_base_must_resolve_all_three_flags_from_the_authorization_service()
    {
        var body = ExtractSetPermissionsBody(ReadCrudPageBase());

        foreach (var flag in new[] { "IsGrantedCreate", "IsGrantedUpdate", "IsGrantedDelete" })
        {
            var assignment = Regex.Match(body, $@"{flag}\s*=\s*(?<rhs>[^;]+);");
            assignment.Success.ShouldBeTrue($"{flag} ataması bulunamadı — izin kapısı eksik.");

            var rhs = assignment.Groups["rhs"].Value.Trim();
            rhs.ShouldNotBe("true", $"{flag} sabit true atanamaz — izin sorgusundan gelmeli.");
            rhs.ShouldContain(
                "IsGranted",
                Case.Sensitive,
                $"{flag} bir izin sorgusundan türemeli (doğrudan ya da IsGrantedAsync yardımcısıyla).");
        }
    }

    private static string ReadCrudPageBase()
    {
        var path = Path.Combine(ConventionSource.RepoRoot, CrudPageBasePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue(
            $"{CrudPageBasePath} bulunamadı — dosya taşındıysa bu testin yolu da güncellenmeli.");
        return File.ReadAllText(path);
    }

    /// <summary>Yalnız <c>SetPermissionsAsync</c> gövdesini alır: dosyanın geri kalanındaki (ör. doc-comment'teki)
    /// tarihçe metni testi yanlışlıkla kırmasın.</summary>
    private static string ExtractSetPermissionsBody(string source)
    {
        var start = source.IndexOf("Task SetPermissionsAsync", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "SetPermissionsAsync bulunamadı — izin kapısı kaldırılmış olabilir.");

        var open = source.IndexOf('{', start);
        open.ShouldBeGreaterThan(-1);

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') { depth++; }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) { return source[open..(i + 1)]; }
            }
        }

        throw new InvalidOperationException("SetPermissionsAsync gövdesi kapanmadı (dengesiz süslü parantez).");
    }
}
