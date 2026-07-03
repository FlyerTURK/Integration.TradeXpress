using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Lokalizasyon konvansiyonlarının MEKANİK güvenlik ağı (governance Katman 2). tr.json ⇄ en.json ANAHTAR
/// paritesi bozulursa (yeni bir anahtar tek dosyaya eklenirse) <c>dotnet test</c> KIRMIZI olur — çeviri
/// unutulması sessiz geçmesin. Ayrıca kodda referanssız (öksüz) anahtarları rapor eder (UYARI; FAIL DEĞİL —
/// dinamik <c>L[$"...{x}"]</c> kurguları yalancı-pozitif üretir).
/// <para>JSON kök property'si case-insensitive okunur (tr.json 'texts', en.json 'Texts' — ABP toleransı).</para>
/// </summary>
public class LocalizationParityTests
{
    private const string LocDir = "src/Integration.TradeXpress.Domain.Shared/Localization/TradeXpress";

    // MEŞRU İSTİSNA — bilinen parite boşlukları: ABP startup-template default anahtarları (tr'de var, en'de
    // çevrilmemiş). Golden YEŞİL için allow-list'lendi; İŞ anahtarları buraya GİREMEZ (yeni özellik anahtarı
    // her iki dosyaya da eklenmeli). en'e çevrildikçe buradan çıkarılır. Kanıt: DENETIM-2026-07-02 + parite taraması.
    private static readonly HashSet<string> KnownParityGaps = new(StringComparer.Ordinal)
    {
        "Bullion", "ChoosePhoto", "Dark", "Dashboard", "Date", "DeviceTheme",
        "ExternalProvider:Google", "ExternalProvider:Google:ClientId", "ExternalProvider:Google:ClientSecret",
        "ExternalProvider:Microsoft", "ExternalProvider:Microsoft:ClientId", "ExternalProvider:Microsoft:ClientSecret",
        "ExternalProvider:Twitter", "ExternalProvider:Twitter:ConsumerKey", "ExternalProvider:Twitter:ConsumerSecret",
        "Home", "Language", "Light", "LoadMore",
        "Menu:ArticleSample", "Menu:ContactUs", "Menu:Dashboard", "Menu:HomePage",
        "NewsletterHeader", "NewsletterInfo", "NewsletterPreference_Default", "NewsletterPrivacyAcceptMessage",
        "Permission:Dashboard", "SeeAllUsers", "TakePhoto", "Theme", "Unspecified",
    };

    private static readonly Regex StringLiteralRegex = new("\"([^\"\\\\\r\n]+)\"", RegexOptions.Compiled);

    private readonly ITestOutputHelper _output;

    public LocalizationParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Tr_and_en_localization_keys_must_be_in_parity()
    {
        // Kural: tr.json ile en.json aynı anahtar kümesini taşımalı (KnownParityGaps hariç).
        var tr = ReadKeys("tr.json");
        var en = ReadKeys("en.json");

        var trOnly = tr.Except(en).Where(k => !KnownParityGaps.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var enOnly = en.Except(tr).Where(k => !KnownParityGaps.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        var messages = new List<string>();
        if (trOnly.Count > 0)
        {
            messages.Add("Yalnız tr.json'da (en.json'a eklenmeli): " + string.Join(", ", trOnly));
        }

        if (enOnly.Count > 0)
        {
            messages.Add("Yalnız en.json'da (tr.json'a eklenmeli): " + string.Join(", ", enOnly));
        }

        messages.ShouldBeEmpty(
            "tr.json ⇄ en.json anahtar paritesi bozuk (yeni anahtar iki dosyaya da eklenmeli):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, messages));
    }

    [Fact]
    public void Orphan_localization_keys_are_reported_as_warnings()
    {
        // Kodda referansı bulunamayan anahtarlar RAPORLANIR (FAIL DEĞİL — dinamik kurgular yalancı-pozitif).
        var keys = ReadKeys("tr.json");
        keys.UnionWith(ReadKeys("en.json"));

        // Tüm kaynakta geçen string literal token'larını tek geçişte topla → O(N + anahtar) membership.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles())
        {
            foreach (Match m in StringLiteralRegex.Matches(File.ReadAllText(file)))
            {
                referenced.Add(m.Groups[1].Value);
            }
        }

        var orphans = keys.Where(k => !referenced.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        _output.WriteLine($"Öksüz (kodda literal referansı bulunamayan) anahtar: {orphans.Count} / {keys.Count}");
        _output.WriteLine("NOT: FAIL değil — dinamik L[$\"...{x}\"] kurguları burada görünebilir. Gerçek ölüyse tr+en'den sil.");
        foreach (var k in orphans)
        {
            _output.WriteLine("  - " + k);
        }
    }

    private static HashSet<string> ReadKeys(string fileName)
    {
        var path = Path.Combine(ConventionSource.RepoRoot, LocDir, fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        // Kök property 'texts' / 'Texts' (case-insensitive) — ABP culture dosyası toleransı.
        var textsProp = doc.RootElement
            .EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, "texts", StringComparison.OrdinalIgnoreCase));

        if (textsProp.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{fileName}: 'texts' nesnesi bulunamadı (lokalizasyon dosyası bozuk).");
        }

        return textsProp.Value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> SourceFiles() =>
        ConventionSource.EnumerateSource("*.cs").Concat(ConventionSource.EnumerateSource("*.razor"));
}
