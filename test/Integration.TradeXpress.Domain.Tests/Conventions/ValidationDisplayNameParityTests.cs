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
/// Doğrulama (validation) alan-adı lokalizasyonunun MEKANİK güvenlik ağı (governance Katman 2).
/// Application.Contracts (TradeXpress + Framework) kaynaklarında <c>[Required]/[StringLength]/[Range]/
/// [MaxLength]/[MinLength]/[EmailAddress]</c> taşıyan HER public property'nin adı, kullanıcıya çevrilmiş
/// görünebilmelidir — <c>FieldNameLocalizer</c> zinciri: <c>[Display(Name)]</c> → <c>DisplayName:X</c> →
/// <c>X</c> (bare) → ham ad (fail-open). Bare/DisplayName anahtarı tr.json VE en.json'un İKİSİNDE de yoksa
/// ve property <c>[Display(Name=...)]</c> de taşımıyorsa, ihlal toast'ında ham İngilizce property adı
/// görünür ("CommodityCode alanı zorunludur.") — bu test o sessiz bozulmayı KIRMIZI yapar.
///
/// <para><b>Neden disk-tarama, reflection değil:</b> Domain.Tests yalnız Domain + TestBase referanslar;
/// Contracts assembly'lerine derleme-zamanı referansı YOK (Domain, Contracts'ı görmez — ters katman).
/// Referans eklemek katman bağımlılığı kurar ve bu dosyanın kapsamını aşar; aynı klasördeki
/// <see cref="LocalizationParityTests"/> de disk-tarama yaklaşımını kullanır — ona birebir uyulur.</para>
///
/// <para><b>Neden yalnız TradeXpress tr/en.json:</b> CRUD bileşenleri <c>CrudComponentBase
/// .DefaultLocalizationResource</c> üzerinden UYGULAMA resource'una (TradeXpressResource) bağlanır ve
/// TradeXpressResource, IntegrationFrameworkResource'tan KALITIM ALMAZ — Framework json'undaki bir anahtar
/// runtime'da form için çözülmez. Framework Contracts alanları (bugün yalnız <c>ListRequestDto
/// .MaxResultCount</c>) teknik olduğundan allow-list'tedir; kalıtım kurulursa burada union'a genişletilir.</para>
/// </summary>
public class ValidationDisplayNameParityTests
{
    private const string LocDir = "src/Integration.TradeXpress.Domain.Shared/Localization/TradeXpress";

    // Kapsam: yalnız sözleşme (Contracts) projeleri — form modelleri (DTO/Input) burada yaşar.
    private static readonly string[] ContractRoots =
    {
        "src/Integration.TradeXpress.Application.Contracts/",
        "src/Integration.Framework.Application.Contracts/",
    };

    // MEŞRU İSTİSNA — 2026-07-31 taramasının anlık görüntüsü: bugün anahtarı OLMAYAN adlar. İki grup:
    //
    // (a) KALICI-TEKNİK: form caption'ı üretilmeyen / sayfalama-serileştirme alanları — anahtar beklenmez.
    // (b) GEÇİCİ: kullanıcı-görünür alanlar — lokalizasyon anahtarı Paket A (validation UX işi) ile
    //     eklendikçe buradan SİLİNMELİDİR (bayatlayan girdiler Allow_list_* raporunda görünür; FAIL değil).
    //
    // İŞ anahtarı buraya kalıcı olarak GİREMEZ; yeni DTO alanı anahtarıyla birlikte gelir (§8 deseni).
    private static readonly HashSet<string> KnownMissingDisplayNames = new(StringComparer.Ordinal)
    {
        // ---- kalıcı-teknik (form alanı değil; kullanıcı ham adı görmez) ----
        "MaxResultCount",       // Framework ListRequestDto — sayfalama kısıtı, asla form alanı olmaz
        "RecurrenceInfo",       // DevExpress Scheduler serileştirme alanı (kullanıcı ham görmez)
        "EntityName",           // SpecialCodeDtos — sistem tanımlayıcısı (tip adı), çevirisi yanıltıcı olur
        "PropertyName",         // SpecialCodeDtos — sistem tanımlayıcısı (property adı)

        // NOT (2026-08-01): İlk taramanın 35 "geçici" girdisi, bare anahtarları tr+en'e eklendiği için
        // listeden BUDANDI — bu adlar artık mekanik ağın korumasında: anahtar silinirse test KIRMIZI yanar.
    };

    // Attribute satırı doğrulama ailesi mi? "[Required]"/"[StringLength(..)]"/"[Required, StringLength(..)]"
    // — attribute adı '[' ya da ',' sonrasında BAŞLAMALI (\b yetmez: "CustomRequired" yanlış-pozitif olurdu).
    private static readonly Regex ValidationAttributeRegex = new(
        @"[\[,]\s*(?:Required|StringLength|Range|MaxLength|MinLength|EmailAddress)\s*[(\],]",
        RegexOptions.Compiled);

    // [Display(Name = ...)] — FieldNameLocalizer zincirinin ilk halkası; varsa bare anahtar aranmaz.
    private static readonly Regex DisplayNameAttributeRegex = new(
        @"[\[,]\s*Display\s*\(\s*Name\s*=",
        RegexOptions.Compiled);

    // Auto-property bildirimi: "public <tip> <Ad> { get". Bilinçli olarak '{ get' zorunlu — sınıf/metot
    // satırları ('{ get' yok) ve expression-bodied hesaplanmış property'ler ('=>') kapsam dışı kalır.
    // Anchor YOK: corpus'ta "[StringLength(..)] public string? Model { get; set; }" tek-satır stili de var.
    private static readonly Regex PropertyDeclarationRegex = new(
        @"\bpublic\s.*\s(\w+)\s*\{\s*get",
        RegexOptions.Compiled);

    private readonly ITestOutputHelper _output;

    public ValidationDisplayNameParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Validated_contract_properties_must_have_localized_display_names()
    {
        // Kural: doğrulama attribute'lu her public property adı için tr VE en sözlüğünde
        // bare anahtar ("City") ya da "DisplayName:City" bulunmalı — ya da property [Display(Name=...)] taşımalı.
        var tr = ReadKeys("tr.json");
        var en = ReadKeys("en.json");
        var (properties, withDisplayAttribute) = CollectValidatedProperties();

        var violations = new List<string>();
        foreach (var (name, exampleFile) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // *Id alanları muaf: formda ham id caption'ı gösterilmez (picker/combo kendi başlığını taşır)
            // ve id alanı için çeviri anahtarı üretmek gürültü olur — LocalizationParityTests §8 deseni.
            if (name.EndsWith("Id", StringComparison.Ordinal))
            {
                continue;
            }

            // Herhangi bir bildirimi [Display(Name=...)] taşıyorsa ad-düzeyinde muaf (zincirin ilk halkası;
            // Display anahtarının kendisinin çevirisi ayrı konudur — bugün corpus'ta hiç Display kullanımı yok).
            if (withDisplayAttribute.Contains(name))
            {
                continue;
            }

            if (KnownMissingDisplayNames.Contains(name))
            {
                continue;
            }

            var trOk = tr.Contains(name) || tr.Contains("DisplayName:" + name);
            var enOk = en.Contains(name) || en.Contains("DisplayName:" + name);
            if (trOk && enOk)
            {
                continue;
            }

            var missing = (trOk, enOk) switch
            {
                (false, false) => "tr+en",
                (false, true) => "tr",
                _ => "en",
            };
            violations.Add($"{name} [eksik: {missing}] (örnek: {exampleFile})");
        }

        violations.ShouldBeEmpty(
            "Doğrulama attribute'lu property adları çevirisiz — kullanıcı toast'ta HAM İngilizce ad görür. "
            + "Çözüm: tr.json + en.json'a bare anahtar ('City') ya da 'DisplayName:City' ekle "
            + "(alan-özel sapma gerekiyorsa DisplayName: bare'i ezer); teknikse gerekçesiyle allow-list'e al:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Stale_allow_list_entries_are_reported_as_warnings()
    {
        // Allow-list hijyeni (FAIL DEĞİL — Paket A anahtar ekledikçe girdiler doğal olarak bayatlar):
        // anahtarı artık MEVCUT olan ya da artık hiçbir validated property'ye karşılık gelmeyen girdileri
        // raporla ki liste küçülerek gerçek istisnalara insin.
        var tr = ReadKeys("tr.json");
        var en = ReadKeys("en.json");
        var (properties, _) = CollectValidatedProperties();

        foreach (var name in KnownMissingDisplayNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!properties.ContainsKey(name))
            {
                _output.WriteLine($"BAYAT (artık validated property değil — listeden sil): {name}");
                continue;
            }

            var trOk = tr.Contains(name) || tr.Contains("DisplayName:" + name);
            var enOk = en.Contains(name) || en.Contains("DisplayName:" + name);
            if (trOk && enOk)
            {
                _output.WriteLine($"BAYAT (anahtarı eklenmiş — listeden sil): {name}");
            }
        }
    }

    /// <summary>
    /// Contracts kaynaklarını satır-bazlı durum makinesiyle tarar: attribute satırları biriktirilir
    /// (yorum/boşluk zinciri BOZMAZ), doğrulama attribute'u taşıyan ilk property bildirimi kaydedilir.
    /// Tek-satır "[Attr] public T X { get; set; }" stili de yakalanır. Dönen sözlük: ad → örnek dosya
    /// (repo-köküne göreli); küme: [Display(Name=...)] taşıyan adlar.
    /// </summary>
    private static (Dictionary<string, string> Properties, HashSet<string> WithDisplayAttribute) CollectValidatedProperties()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var withDisplay = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in ContractFiles())
        {
            var pending = new List<string>();
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    pending.Add(line);
                    if (!PropertyDeclarationRegex.IsMatch(line))
                    {
                        continue;   // saf attribute satırı — property bildirimi sonraki satırlarda
                    }
                }
                else if (line.Length == 0
                    || line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith("*", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;       // yorum/boşluk attribute-property zincirini BOZMAZ
                }

                var match = PropertyDeclarationRegex.Match(line);
                if (match.Success && pending.Any(a => ValidationAttributeRegex.IsMatch(a)))
                {
                    var name = match.Groups[1].Value;
                    if (!properties.ContainsKey(name))
                    {
                        properties[name] = ConventionSource.RelativePath(file);
                    }

                    if (pending.Any(a => DisplayNameAttributeRegex.IsMatch(a)))
                    {
                        withDisplay.Add(name);
                    }
                }

                pending.Clear();
            }
        }

        return (properties, withDisplay);
    }

    private static IEnumerable<string> ContractFiles()
    {
        return ConventionSource
            .EnumerateSource("*.cs")
            .Where(f =>
            {
                var relative = ConventionSource.RelativePath(f);
                return ContractRoots.Any(root => relative.StartsWith(root, StringComparison.Ordinal));
            });
    }

    // LocalizationParityTests.ReadKeys ile aynı okuma: kök property 'texts'/'Texts' (case-insensitive).
    private static HashSet<string> ReadKeys(string fileName)
    {
        var path = Path.Combine(ConventionSource.RepoRoot, LocDir, fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var textsProp = doc.RootElement
            .EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, "texts", StringComparison.OrdinalIgnoreCase));

        if (textsProp.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{fileName}: 'texts' nesnesi bulunamadı (lokalizasyon dosyası bozuk).");
        }

        return textsProp.Value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }
}
