using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Integration.TradeXpress.Blazor.Client.Services;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Conventions;

/// <summary>
/// LOOKUP EKLE/DÜZELT DÜĞMELERİ VARSAYILAN GÖRÜNÜR (governance Katman 2).
///
/// <para><b>Kural (2026-08-07 Hakan):</b> <i>"LookupComboBox'ta aksi belirtilmedikçe add ve edit butonlarının
/// görünmesi gerekir. Bu component zaten bunun için var. Yoksa standard Combo zaten işimizi çok rahat
/// görüyor."</i></para>
///
/// <para><b>Kapatılan açık:</b> düğmeler YALNIZ çağıran <c>EditComponentType</c> yazdığında çiziliyordu ve 69
/// kullanımın ancak 15'i yazıyordu. Geri kalan her lookup sessizce düz bir combo'ya iniyordu — kullanıcı için
/// bu bir hata gibi görünmez, yalnız "buradan yeni birim ekleyemiyorum" diye biten bir iş akışı olur. Artık
/// hedef TİPTEN çözülüyor (<see cref="TradeXpressLookupEditComponents"/>).</para>
///
/// <para><b>Bu test neden metin tarıyor:</b> eksiklik bir HATA değil bir YOKLUKTUR — düğme çizilmez, istisna
/// doğmaz, hiçbir davranış testi kırılmaz. Yeni bir lookup tipi eklenip kayıt defterine yazılmazsa yakalanacak
/// tek yer kaynağın kendisidir.</para>
/// </summary>
public class LookupEditButtonConventionTests
{
    private static readonly Regex ItemTypeRegex = new(
        @"<LookupComboBox\b[^>]*?TItem\s*=\s*""(?<item>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>DÜZENLENEBİLİR BİR KAYIT OLMAYAN lookup tipleri — düğmesiz kalmaları DOĞRUdur.
    /// <para>Bunlar türetilmiş/salt-okuma projeksiyonlardır: "yeni fiyat ekle" ya da "yeni varyant seçeneği
    /// ekle" diye bir işlem yoktur. Listeye ekleme YAPILIRKEN gerekçe yazılır — sessiz istisna olmaz.</para></summary>
    private static readonly Dictionary<string, string> ReadOnlyLookups = new(StringComparer.Ordinal)
    {
        ["CurrentPriceDto"]             = "Türetilmiş kur/fiyat projeksiyonu — düzenlenebilir kayıt değil.",
        ["MetalVariantLookupDto"]       = "Maden varyantı seçeneği; varyantlar madenin kendi formunda üretilir.",
        ["CommodityVariantLookupDto"]   = "Mamül/mücevher varyantı seçeneği (yassı emtia×varyant); varyantlar sahibinin formunda üretilir — reçete paneli EditComponentType'ı açıkça verir (Good/Jewelry host'u).",
        ["CommodityVariantOptionDto"]   = "Emtia varyant seçeneği — sahibinin formunda yönetilir.",
        ["MyVaultDto"]                  = "Kullanıcının erişebildiği kasalar (yetki projeksiyonu) — buradan kasa açılmaz.",
        ["N11ShipmentTemplateDto"]      = "Kanal kargo şablonu; kendi kanal ekranından yönetilir.",
        ["TListDto"]                    = "Generic bileşen parametresi — somut tip değil.",
    };

    [Fact]
    public void Every_lookup_item_type_can_resolve_an_edit_component_or_is_declared_read_only()
    {
        var registry = TradeXpressLookupEditComponents.Build();
        var registered = RegisteredTypeNames(registry);

        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateRazorFiles())
        {
            foreach (Match match in ItemTypeRegex.Matches(File.ReadAllText(file)))
            {
                var item = match.Groups["item"].Value.Trim();
                if (registered.Contains(item) || ReadOnlyLookups.ContainsKey(item))
                {
                    continue;
                }

                missing.Add($"{item}  ({ConventionRelativePath(file)})");
            }
        }

        missing.ShouldBeEmpty(
            "Bu lookup tipleri ekle/düzelt düğmesi ALAMIYOR: ne kayıt defterinde ne salt-okuma listesinde. " +
            "Düğmeler varsayılan görünmeli (yoksa düz DxComboBox yeterdi). Ya " +
            $"{nameof(TradeXpressLookupEditComponents)}'e edit host'unu ekle, ya da salt-okuma listesine " +
            "GEREKÇESİYLE yaz:\n" + string.Join("\n", missing));
    }

    /// <summary>Kayıt defteri gerçekten çözüyor mu — tablo dolu ama <c>Resolve</c> bozuksa yukarıdaki test
    /// yine de yeşil kalırdı (o yalnız anahtar adlarına bakıyor).</summary>
    [Fact]
    public void Registry_resolves_a_known_type_to_its_edit_host()
    {
        var registry = TradeXpressLookupEditComponents.Build();

        registry.Resolve(typeof(Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto))
            .ShouldNotBeNull();

        // Kayıtlı OLMAYAN tip null döner — "her şeye düğme" değil, bilinçli eşleme.
        registry.Resolve(typeof(string)).ShouldBeNull();
    }

    private static HashSet<string> RegisteredTypeNames(
        Integration.Framework.Blazor.Client.Components.Crud.ILookupEditComponentRegistry registry)
    {
        // Kayıt defteri anahtarlarını dışa vermiyor (Resolve tek yüzey) → razor'daki adları TİP ADIYLA
        // eşleştirmek için bilinen adayları çözerek doğruluyoruz. Basit ve kırılgan olmayan yol: razor'da
        // geçen her adı, o adı taşıyan bir tip bulup Resolve'a sorarak sınamak.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(TradeXpressLookupEditComponents).Assembly.GetTypes()
                     .Concat(typeof(Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnitListDto).Assembly.GetTypes()))
        {
            if (registry.Resolve(type) is not null)
            {
                names.Add(type.Name);
            }
        }

        return names;
    }

    private static IEnumerable<string> EnumerateRazorFiles()
    {
        var root = FindRepoRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(p => !Normalize(p).Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !Normalize(p).Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    private static string ConventionRelativePath(string fullPath) =>
        Normalize(Path.GetRelativePath(FindRepoRoot(), fullPath));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Integration.TradeXpress.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo kökü bulunamadı — convention testi kaynak tarayamaz.");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
