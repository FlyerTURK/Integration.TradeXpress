using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DevExpress.Blazor;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Conventions;

/// <summary>
/// <b>DevExpress bileşenlerine geçirdiğimiz her parametre GERÇEKTEN var mı?</b>
///
/// <para><b>Neden bu ağ var (2026-08-03):</b> Razor, bir bileşene bilinmeyen parametre geçmeyi
/// DERLEME ZAMANINDA yakalamaz — kod temiz derlenir, hata ancak o bileşen ÇİZİLİRKEN patlar:
/// <c>"Object of type 'DevExpress.Blazor.DxGridDataColumn' does not have a property matching the
/// name 'AllowResize'"</c>. Blazor Server'da bu istisna circuit'i düşürür; kullanıcı tarafında görüntü
/// şudur: <i>sayfa tepkisiz kalır, kaydet çalışmaz ve HİÇBİR hata görünmez.</i></para>
///
/// <para>Gerçek vaka: <c>AllowResize</c> DevExpress 25.2.5 döneminde CrudLayout'un seçici kolonuna
/// eklendi, 25.2.8'de bu parametre yok. Kolon yalnız SEÇİCİ modda çizildiği için günlerce görünmedi;
/// sonunda "yeni şirket kaydedilmiyor, hata da yok" olarak ortaya çıktı. bUnit render testi de
/// yakalayamadı çünkü DevExpress iç içe ayar bileşenlerini test render'ında materyalize etmiyor.</para>
///
/// <para>Bu yüzden ağ RENDER değil <b>METİN + REFLEKSİYON</b> tabanlı: tüm .razor dosyalarındaki
/// <c>&lt;Dx...&gt;</c> etiketlerinin parametre adları çıkarılır ve gerçek DevExpress tipinde böyle bir
/// <c>[Parameter]</c> var mı diye doğrulanır. Sürüm yükseltmesinde silinen/yeniden adlandırılan her
/// parametre KIRMIZI döner.</para>
/// </summary>
public class DevExpressParameterExistenceTests
{
    // Razor'ın kendi direktifleri ve jenerik tip argümanları parametre DEĞİLDİR — bunlar atlanır.
    private static readonly HashSet<string> RazorDirectives = new(StringComparer.Ordinal)
    {
        "Context", "ChildContent",
    };

    /// <summary>Bilinen jenerik tip parametresi adları (<c>TData</c>, <c>TValue</c>…) — Razor bunları tip
    /// argümanı olarak çözer, bileşende property karşılığı olmayabilir.</summary>
    private static bool IsGenericTypeArgument(string name)
    {
        return name.Length >= 2 && name[0] == 'T' && char.IsUpper(name[1]);
    }

    [Fact]
    public void Every_DevExpress_parameter_used_in_razor_markup_should_exist_on_the_component()
    {
        var srcRoot = FindSourceRoot();
        var razorFiles = Directory
            .EnumerateFiles(srcRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        razorFiles.ShouldNotBeEmpty("Hiç .razor dosyası bulunamadı — kaynak kökü yanlış çözülmüş olabilir.");

        var dxAssembly = typeof(DxGrid).Assembly;
        var ihlaller = new List<string>();
        var taranan = 0;

        foreach (var file in razorFiles)
        {
            var markup = File.ReadAllText(file);
            foreach (Match tag in Regex.Matches(markup, @"<(Dx[A-Za-z0-9_]+)((?:[^<>""]|""[^""]*"")*?)/?>"))
            {
                var componentName = tag.Groups[1].Value;
                var type = dxAssembly.GetType("DevExpress.Blazor." + componentName);
                if (type is null)
                {
                    continue;   // bizim kendi bileşenimiz ya da başka namespace — bu ağın konusu değil
                }

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                // Bileşen "yakalanmamış öznitelikleri" topluyorsa (CaptureUnmatchedValues) bilinmeyen ad
                // MEŞRUDUR — style/tabindex/data-* gibi ham HTML öznitelikleri oraya akar, çalışma anında
                // patlamaz. Böyle tiplerde bu ağın söyleyeceği bir şey yok.
                var capturesUnmatched = properties.Any(p =>
                    p.GetCustomAttribute<Microsoft.AspNetCore.Components.ParameterAttribute>() is { CaptureUnmatchedValues: true });
                if (capturesUnmatched)
                {
                    continue;
                }

                taranan++;

                // [Parameter] işaretine BAKMIYORUZ bilerek: DevExpress'in iç içe ayar bileşenleri (kolon,
                // özet kalemi…) parametrelerini kendi mekanizmasıyla tanımlıyor ve hepsi attribute taşımıyor.
                // Bizi ilgilendiren tek şey Blazor'ın ada göre property bulup bulamayacağı.
                var parameterNames = properties
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (Match attr in Regex.Matches(tag.Groups[2].Value, @"(?:^|\s)(@?[A-Za-z][A-Za-z0-9_\-:]*)\s*="))
                {
                    var raw = attr.Groups[1].Value;
                    if (raw.StartsWith('@'))
                    {
                        // @bind-Value → Value, @bind-Value:event / @ref / @key / @attributes → atla
                        if (!raw.StartsWith("@bind-", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        raw = raw["@bind-".Length..];
                        if (raw.Contains(':', StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    if (raw.Contains('-', StringComparison.Ordinal) || raw.Contains(':', StringComparison.Ordinal))
                    {
                        continue;   // data-* / aria-* gibi HTML öznitelikleri (splatting ile geçer)
                    }

                    if (RazorDirectives.Contains(raw) || IsGenericTypeArgument(raw) || parameterNames.Contains(raw))
                    {
                        continue;
                    }

                    ihlaller.Add($"{Path.GetFileName(file)} → <{componentName} {raw}=...>  (bu parametre {componentName} üzerinde YOK)");
                }
            }
        }

        taranan.ShouldBeGreaterThan(0, "Hiç DevExpress etiketi eşleşmedi — regex ya da assembly çözümü bozulmuş olabilir.");

        ihlaller.ShouldBeEmpty(
            "DevExpress bileşenlerine VAR OLMAYAN parametre geçiliyor. Bunlar derlenir ama ÇİZİM ANINDA "
            + "circuit'i düşürür ve kullanıcı hiçbir hata görmez. Parametreyi kaldırın ya da sürümdeki doğru "
            + "karşılığıyla değiştirin:" + Environment.NewLine
            + string.Join(Environment.NewLine, ihlaller.Distinct().OrderBy(x => x, StringComparer.Ordinal)));
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Depo kökü (içinde 'src' olan dizin) bulunamadı.");
        return Path.Combine(dir!.FullName, "src");
    }
}
