using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// PAYLAŞILAN bileşenlere TANIMSIZ parametre verilmesini yakalayan mekanik ağ.
///
/// <para><b>Neden var:</b> Blazor'da bir bileşene olmayan bir parametreyi vermek DERLEME hatası DEĞİLDİR —
/// çalışma anında <c>"Object of type 'X' does not have a property matching the name 'Y'"</c> ile patlar ve
/// sayfa açılır açılmaz circuit düşer. Bu tuzağa iki kez düşüldü (2026-07-27 <c>GridLinkColumn.MinWidth</c>,
/// 2026-07-28 <c>NumericSpinEdit.ReadOnly</c>); ikisi de derlemeden temiz geçip kullanıcının ekranında
/// patladı. Üçüncüsü olmasın diye kural mekanikleşti.</para>
///
/// <para><b>Kapsam:</b> yalnız BİZİM yazdığımız paylaşılan bileşenler (Framework + TradeXpress ortak
/// bileşenleri). DevExpress/ABP bileşenleri taranmaz — kaynakları repoda yok, parametre listeleri
/// çıkarılamaz.</para>
///
/// <para><b>Yöntem:</b> bileşenin <c>.razor</c> + <c>.razor.cs</c> dosyalarından <c>[Parameter]</c> ve
/// <c>@typeparam</c> adları toplanır; sonra tüm <c>.razor</c> markup'ında o bileşenin kullanımları taranıp
/// verilen attribute adları bu kümeyle karşılaştırılır. Blazor yönergeleri (<c>@ref</c>, <c>@key</c>,
/// <c>@attributes</c>, <c>@on*</c>, <c>Context</c>) hariç tutulur.</para>
/// </summary>
public class RazorComponentParameterTests
{
    // [Parameter] public T Name { get; set; } — nitelik ile bildirim aynı satırda ya da ayrı satırlarda olabilir.
    private static readonly Regex ParameterDeclarationRegex = new(
        @"\[\s*Parameter[^\]]*\][\s\S]{0,200}?\bpublic\s+[\w\.\<\>\?\[\],\s]+?\s+(\w+)\s*\{",
        RegexOptions.Compiled);

    // @typeparam TItem  → jenerik tip argümanı, markup'ta attribute gibi verilir (TItem="Foo").
    private static readonly Regex TypeParamRegex = new(@"^\s*@typeparam\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled);

    // @inherits Ns.EntryPanelBase<Foo> → TABAN SINIFIN parametreleri de meşrudur. Jenerik argüman ve
    // namespace ön-eki atılıp yalın sınıf adı alınır (katalog sınıf ADIYLA anahtarlı).
    private static readonly Regex InheritsRegex = new(@"^\s*@inherits\s+([\w\.]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    // C# sınıf bildirimi + taban tipi: "public abstract class EntryPanelBase<TItem> : CrudComponentBase".
    // Taban zinciri boyunca yürünür — parametre iki kuşak yukarıda tanımlı olabilir.
    private static readonly Regex ClassDeclarationRegex = new(
        @"\bclass\s+(\w+)(?:\s*<[^>]*>)?\s*(?::\s*([\w\.]+))?",
        RegexOptions.Compiled);

    // Markup attribute adı: büyük harfle başlayan, '=' ile değer alan ad (bind/yönerge olmayanlar).
    private static readonly Regex AttributeNameRegex = new(@"(?<![\w@\-.])([A-Z]\w*)\s*=", RegexOptions.Compiled);

    private static readonly Regex RazorCommentRegex = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Blazor'ın kendi yönergeleri — bileşen parametresi DEĞİL, taramadan muaf.</summary>
    private static readonly HashSet<string> BlazorDirectives = new(StringComparer.Ordinal)
    {
        "Context",   // RenderFragment bağlam adı (@context yeniden adlandırma)
    };

    [Fact]
    public void Shared_components_must_not_receive_unknown_parameters()
    {
        var root = FindRepositoryRoot();
        var razorFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var baseClasses = BuildBaseClassCatalog(root);
        var components = BuildComponentCatalog(razorFiles, baseClasses);
        components.ShouldNotBeEmpty("Paylaşılan bileşen kataloğu boş — tarama yolu bozulmuş olabilir.");

        var violations = new List<string>();

        foreach (var file in razorFiles)
        {
            // Yorumlar SİLİNMEZ, boşlukla MASKELENİR: silmek satır sayısını kaydırır ve ihlal satırı
            // yanlış raporlanırdı (geliştirici olmayan bir satıra bakar).
            var text = MaskComments(File.ReadAllText(file));
            var rel = Path.GetRelativePath(root, file);

            foreach (var (componentName, allowedParameters) in components)
            {
                foreach (var usage in FindOpeningTags(text, componentName))
                {
                    foreach (Match attribute in AttributeNameRegex.Matches(usage.Body))
                    {
                        var name = attribute.Groups[1].Value;
                        if (BlazorDirectives.Contains(name) || allowedParameters.Contains(name))
                        {
                            continue;
                        }

                        var line = text.Take(usage.Index).Count(c => c == '\n') + 1;
                        violations.Add(
                            $"{rel}:{line}: <{componentName}> '{name}' parametresini TANIMIYOR "
                            + "(Blazor bunu derlemede değil ÇALIŞMA ANINDA reddeder → sayfa açılır açılmaz çöker). "
                            + "Bileşenin gerçek parametre listesine bak; gerçekten gerekiyorsa bileşene ekle.");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor dosyaları paylaşılan bileşenlere TANIMSIZ parametre veriyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Distinct()));
    }

    /// <summary>
    /// C# sınıf adı → (o sınıfın kendi <c>[Parameter]</c>'ları, taban sınıf adı).
    ///
    /// <para><b>Neden gerekli:</b> bir bileşen parametrelerini TABAN SINIFTAN devralabilir
    /// (<c>@inherits EntryPanelBase&lt;T&gt;</c>). Katalog yalnız bileşenin kendi dosyalarına bakarsa
    /// devralınan her parametre "tanımsız" görünür ve test DOĞRU kodu kırmızıya düşürür — bu tam olarak
    /// <c>ProductRecipePanel.OnChanged</c>'de yaşandı: parametre gerçekten vardı, ağ yanlış öttü.
    /// Yanlış pozitif üreten bir ağ, gerçek ihlali de inandırıcılıktan düşürür.</para>
    /// </summary>
    private static Dictionary<string, (HashSet<string> Parameters, string? BaseName)> BuildBaseClassCatalog(string root)
    {
        var catalog = new Dictionary<string, (HashSet<string>, string?)>(StringComparer.Ordinal);

        var csFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("[Parameter", StringComparison.Ordinal) && !text.Contains("class ", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match declaration in ClassDeclarationRegex.Matches(text))
            {
                var className = declaration.Groups[1].Value;
                var baseName = declaration.Groups[2].Success ? ShortTypeName(declaration.Groups[2].Value) : null;

                // Dosyadaki TÜM [Parameter]'lar bu sınıfa yazılır. Bir dosyada birden çok sınıf olması
                // bu depoda istisnadır; olsa bile fazladan parametre tanımak YANLIŞ POZİTİF üretmez
                // (yalnız yakalayamama riski taşır) — ters yön kabul edilemezdi.
                var parameters = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match parameter in ParameterDeclarationRegex.Matches(text))
                {
                    parameters.Add(parameter.Groups[1].Value);
                }

                if (parameters.Count > 0 || baseName is not null)
                {
                    catalog[className] = (parameters, baseName);
                }
            }
        }

        return catalog;
    }

    /// <summary>Taban zinciri boyunca devralınan parametreleri toplar. Döngüye karşı ziyaret kümesi tutar
    /// (kısmi/partial bildirimlerde kendine dönen bir zincir testi sonsuz döngüye sokabilirdi).</summary>
    private static void AddInheritedParameters(
        string? baseName,
        Dictionary<string, (HashSet<string> Parameters, string? BaseName)> baseClasses,
        HashSet<string> target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (baseName is not null && visited.Add(baseName) && baseClasses.TryGetValue(baseName, out var entry))
        {
            foreach (var parameter in entry.Parameters)
            {
                target.Add(parameter);
            }

            baseName = entry.BaseName;
        }
    }

    /// <summary>Namespace ön-ekini ve jenerik argümanı atar: <c>A.B.Base&lt;T&gt;</c> → <c>Base</c>.</summary>
    private static string ShortTypeName(string typeName)
    {
        var withoutGenerics = typeName.Split('<')[0];
        var lastDot = withoutGenerics.LastIndexOf('.');
        return lastDot >= 0 ? withoutGenerics[(lastDot + 1)..] : withoutGenerics;
    }

    /// <summary>Bileşen adı → kabul ettiği attribute adları (parametreler + jenerik tip argümanları).</summary>
    private static Dictionary<string, HashSet<string>> BuildComponentCatalog(
        List<string> razorFiles,
        Dictionary<string, (HashSet<string> Parameters, string? BaseName)> baseClasses)
    {
        var catalog = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in razorFiles)
        {
            // Sayfa bileşenleri (@page) markup'ta etiket olarak kullanılmaz; yalnız yeniden kullanılabilir
            // bileşenler taranır — sayfaları katmak gereksiz gürültü olurdu.
            var markup = File.ReadAllText(file);
            if (markup.Contains("@page ", StringComparison.Ordinal))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            var parameters = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in TypeParamRegex.Matches(markup))
            {
                parameters.Add(match.Groups[1].Value);
            }

            foreach (Match match in ParameterDeclarationRegex.Matches(markup))
            {
                parameters.Add(match.Groups[1].Value);
            }

            var codeBehind = file + ".cs";
            if (File.Exists(codeBehind))
            {
                var codeBehindText = File.ReadAllText(codeBehind);
                foreach (Match match in ParameterDeclarationRegex.Matches(codeBehindText))
                {
                    parameters.Add(match.Groups[1].Value);
                }

                // Code-behind'daki taban sınıf (partial sınıf ": Base" ile bildirilmiş olabilir).
                var declaration = ClassDeclarationRegex.Match(codeBehindText);
                if (declaration.Success && declaration.Groups[2].Success)
                {
                    AddInheritedParameters(ShortTypeName(declaration.Groups[2].Value), baseClasses, parameters);
                }
            }

            // @inherits ile bildirilen taban — devralınan parametreler de MEŞRUDUR.
            var inherits = InheritsRegex.Match(markup);
            if (inherits.Success)
            {
                AddInheritedParameters(ShortTypeName(inherits.Groups[1].Value), baseClasses, parameters);
            }

            if (parameters.Count == 0)
            {
                continue;   // parametresiz bileşen: yanlış pozitif üretmemek için taramaya alınmaz
            }

            // @bind-X kullanımı X + XChanged + XExpression üretir; üçü de meşru attribute'tur.
            foreach (var parameter in parameters.ToList())
            {
                parameters.Add(parameter + "Changed");
                parameters.Add(parameter + "Expression");
            }

            catalog[name] = parameters;
        }

        return catalog;
    }

    /// <summary>Verilen bileşenin açılış etiketlerini bulur (tırnak farkında; çok satırlı etiketler dahil).</summary>
    private static IEnumerable<(int Index, string Body)> FindOpeningTags(string text, string componentName)
    {
        var token = "<" + componentName;
        var position = text.IndexOf(token, StringComparison.Ordinal);

        while (position >= 0)
        {
            // "<Foo" ile "<FooBar" karışmasın: etiket adından sonra sınır karakteri gelmeli.
            var after = position + token.Length;
            if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            {
                position = text.IndexOf(token, position + token.Length, StringComparison.Ordinal);
                continue;
            }

            var body = new StringBuilder();
            var inQuotes = false;
            var index = after;

            while (index < text.Length)
            {
                var c = text[index];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == '>' && !inQuotes)
                {
                    break;
                }

                // Tırnak İÇİ atlanır: lambda/ifade gövdesindeki "Foo=" benzeri metinler attribute sanılmasın.
                body.Append(inQuotes ? ' ' : c);
                index++;
            }

            yield return (position, body.ToString());
            position = text.IndexOf(token, index, StringComparison.Ordinal);
        }
    }

    /// <summary>Razor yorumlarını satır sonlarını KORUYARAK boşlukla değiştirir (satır numarası kaymasın).</summary>
    private static string MaskComments(string text)
    {
        return RazorCommentRegex.Replace(text, m => new string(
            m.Value.Select(c => c == '\n' || c == '\r' ? c : ' ').ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Depo kökü bulunamadı (src klasörü yok).");
        return directory!.FullName;
    }
}
