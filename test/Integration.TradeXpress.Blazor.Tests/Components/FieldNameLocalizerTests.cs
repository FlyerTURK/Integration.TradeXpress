using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// Alan adı çözüm zincirinin (<c>[Display(Name)]</c> → <c>DisplayName:X</c> → <c>X</c> → ham ad) kilidi.
///
/// <para><b>Neden var:</b> bu zincir üç sunum yolunun (client validator · graf gezgini · server hata
/// sunucusu) ortak sözlüğüdür; sırası sessizce değişirse form caption'ı ile hata mesajı FARKLI ada düşer
/// ve kullanıcı hangi alandan bahsedildiğini bulamaz. Zincirin her basamağı burada tek tek sabitlenir.</para>
///
/// <para><b>Neden kendi fake'i var:</b> <c>BlazorComponentTestBase</c>'in pass-through localizer'ı HER
/// anahtarı "bulundu" sayar (<c>ResourceNotFound=false</c>) — fallback basamakları onunla hiç tetiklenmez.
/// Zinciri sınamak için anahtar-bazlı bulundu/bulunamadı ayrımı yapan sözlük tabanlı fake şart.</para>
/// </summary>
public class FieldNameLocalizerTests
{
    [Fact]
    public void DisplayName_key_wins_over_bare_key()
    {
        var localizer = new DictionaryLocalizer(new Dictionary<string, string>
        {
            ["DisplayName:Code"] = "Kod (özel)",
            ["Code"] = "Kod",
        });

        FieldNameLocalizer.Resolve(localizer, "Code").ShouldBe("Kod (özel)");
    }

    [Fact]
    public void Falls_back_to_bare_key_when_displayname_key_is_missing()
    {
        var localizer = new DictionaryLocalizer(new Dictionary<string, string>
        {
            ["Code"] = "Kod",
        });

        FieldNameLocalizer.Resolve(localizer, "Code").ShouldBe("Kod");
    }

    [Fact]
    public void Falls_back_to_raw_name_when_no_key_exists()
    {
        // Fail-open: hiçbir anahtar yoksa boş placeholder DEĞİL ham ad görünür.
        var localizer = new DictionaryLocalizer(new Dictionary<string, string>());

        FieldNameLocalizer.Resolve(localizer, "Code").ShouldBe("Code");
        FieldNameLocalizer.Resolve(localizer: null, "Code").ShouldBe("Code");
    }

    [Fact]
    public void Display_attribute_key_takes_priority_on_property_overload()
    {
        // Tuzak anahtarlar bilerek sözlükte: [Display(Name)] varken zincir onlara HİÇ düşmemeli.
        var localizer = new DictionaryLocalizer(new Dictionary<string, string>
        {
            ["Field:CompanyTitle"] = "Şirket Başlığı",
            ["DisplayName:Title"] = "yanlış-anahtar-1",
            ["Title"] = "yanlış-anahtar-2",
        });
        var property = typeof(DisplayAttributedModel).GetProperty(nameof(DisplayAttributedModel.Title))!;

        FieldNameLocalizer.Resolve(localizer, property).ShouldBe("Şirket Başlığı");
    }

    [Fact]
    public void Property_without_display_attribute_uses_key_chain()
    {
        var localizer = new DictionaryLocalizer(new Dictionary<string, string>
        {
            ["DisplayName:Plain"] = "Düz Alan (özel)",
        });
        var property = typeof(DisplayAttributedModel).GetProperty(nameof(DisplayAttributedModel.Plain))!;

        FieldNameLocalizer.Resolve(localizer, property).ShouldBe("Düz Alan (özel)");
    }

    /// <summary>Yalnız attribute-öncelik testinin taşıyıcısı — üretim DTO'su değildir.</summary>
    private sealed class DisplayAttributedModel
    {
        [Display(Name = "Field:CompanyTitle")]
        public string? Title { get; set; }

        public string? Plain { get; set; }
    }

    /// <summary>Sözlükte olan anahtar → çeviri (<c>ResourceNotFound=false</c>); olmayan → anahtarın kendisi
    /// (<c>ResourceNotFound=true</c>) — gerçek localizer'ın bulunamadı sözleşmesiyle birebir.</summary>
    private sealed class DictionaryLocalizer : IStringLocalizer
    {
        private readonly Dictionary<string, string> _entries;

        public DictionaryLocalizer(Dictionary<string, string> entries)
        {
            _entries = entries;
        }

        public LocalizedString this[string name]
        {
            get
            {
                if (_entries.TryGetValue(name, out var value))
                {
                    return new LocalizedString(name, value, resourceNotFound: false);
                }

                return new LocalizedString(name, name, resourceNotFound: true);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var plain = this[name];
                return new LocalizedString(name, string.Format(plain.Value, arguments), plain.ResourceNotFound);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return _entries.Select(e => new LocalizedString(e.Key, e.Value, resourceNotFound: false));
        }
    }
}
