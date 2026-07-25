using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.Framework.Addressing;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary><see cref="IAddressEditModel"/> için salt-görüntü yardımcıları — <c>ValueObjectEdit</c> özet
/// projeksiyonu + boşluk yüklemi. Birden çok layout (ShipmentTemplate gönderim/iade, Branch) AYNI özet/boşluk
/// mantığını paylaşır → tek kaynak (DRY); biri değişip diğerinin bayatlaması engellenir.</summary>
public static class AddressDisplay
{
    /// <summary>Türk adres formatında özet — SPESİFİKTEN GENELE, boş parçalar atlanır. İki mantıksal blok virgülle
    /// birleşir: (1) sokak detayı = "Mahalle Mh. Cadde No:.. D:.. K:.." , (2) yerellik = "PostaKodu İlçe İl ÜlkeKodu".
    /// Ör: "Caferağa Mh. Moda Cd. No:5 D:3, 34710 Kadıköy İstanbul TR". Tümü boşsa boş string döner (placeholder
    /// gösterilsin diye ValueObjectEdit tarafından yorumlanır).</summary>
    public static string Summary(IAddressEditModel model)
    {
        // Yapısal ekler ADRESİN ÜLKESİNE göre seçilir (UI diline göre DEĞİL): Türk adresi Türkçe kısaltmayla,
        // yabancı adres kendi/İngilizce karşılığıyla okunur. Bir Türk kullanıcı ABD adresine baktığında
        // "No:10 D:8 K:2" değil "No 10 Apt 8 Floor 2" görmeli — kısaltmalar adresin kültürüne aittir.
        var affixes = AffixesFor(model.CountryCode);

        // Blok 1 — sokak/kapı detayı. Bina numarasının YERİ kültüre göre değişir:
        //   TR  → sokak sonra numara:  "553. Sk. No: 10/G"
        //   EN  → numara sonra sokak:  "10 Downing Street"  (ön ek YOK — "Nr:" İngilizce'de kullanılmaz)
        var streetBlock = affixes.BuildingNumberLeadsLine
            ? JoinNonEmpty(" ",
                AppendSuffix(model.Neighborhood, affixes.NeighborhoodSuffix),
                model.BuildingNumber?.Trim(),
                model.Line?.Trim(),
                StructuralDetail(model, affixes))
            : JoinNonEmpty(" ",
                AppendSuffix(model.Neighborhood, affixes.NeighborhoodSuffix),
                model.Line?.Trim(),
                Prefix(affixes.BuildingNumber, model.BuildingNumber),
                StructuralDetail(model, affixes));

        // Blok 2 — yerellik: "21070 Kayapınar / Diyarbakır / TÜRKİYE" (kullanıcı isteği).
        // Üç coğrafi seviye EĞİK ÇİZGİ ile ayrılır (boşluk ayırıcı "Kayapınar Diyarbakır Türkiye" diye okunup
        // nerede bittiği belirsizleşiyordu); posta kodu ilçenin ÖNÜNDE kalır (Türk adres yazım geleneği).
        // Ülke BÜYÜK HARF — uluslararası posta geleneği; ad yoksa koda düşer.
        var localityBlock = JoinNonEmpty(" / ",
            JoinNonEmpty(" ", model.PostalCode?.Trim(), model.District?.Trim()),
            model.City?.Trim(),
            UpperOrNull(FirstNonEmpty(model.CountryName, model.CountryCode)));

        return JoinNonEmpty(", ", streetBlock, localityBlock);
    }

    // Bina adı + daire + kat (kapı NUMARASI burada DEĞİL — yeri kültüre göre değiştiğinden Summary'de yerleşir).
    private static string StructuralDetail(IAddressEditModel model, AddressAffixes affixes)
    {
        return JoinNonEmpty(" ",
            model.BuildingName?.Trim(),
            Prefix(affixes.Room, model.Room),
            Prefix(affixes.Floor, model.Floor));
    }

    /// <summary>Adres yapısal eklerinin ülkeye göre kümesi. Kayıtlı ülke yoksa İngilizce varsayılana düşer
    /// (kullanıcı kuralı: "varsa o ülkenin kültürüne göre, yoksa İngilizce").</summary>
    private static AddressAffixes AffixesFor(string? countryCode)
    {
        var code = countryCode?.Trim();
        return !string.IsNullOrEmpty(code) && AffixesByCountry.TryGetValue(code, out var affixes)
            ? affixes
            : DefaultAffixes;
    }

    /// <summary>Ülkeye özel adres yazım kuralı. Yeni ülke = bu sözlüğe bir satır (şema/migration GEREKMEZ —
    /// bunlar dil kuralı, veri değil).</summary>
    /// <param name="BuildingNumber">Kapı no ön eki; null → ön ek yazılmaz (İngilizce'de numara çıplak gelir).</param>
    /// <param name="NeighborhoodSuffix">null → o ülkede mahalle son eki yok.</param>
    /// <param name="BuildingNumberLeadsLine">true → numara sokak adının ÖNÜNDE ("10 Downing Street");
    /// false → sokaktan SONRA, ön ekle ("553. Sk. No: 10/G").</param>
    private sealed record AddressAffixes(
        string? BuildingNumber,
        string Room,
        string Floor,
        string? NeighborhoodSuffix,
        bool BuildingNumberLeadsLine);

    // İngilizce varsayılan — kayıtlı olmayan HER ülke bunu kullanır. Numara sokaktan ÖNCE ve ÖN EKSİZ:
    // İngilizce adreste "Nr:" diye bir kalıp yoktur, numara doğrudan sokak adının başına yazılır.
    // Daire/kat ise İngilizce'de gerçekten etiketlidir ("Apt 4B", "3rd Floor") → onlarda ön ek KALIR.
    private static readonly AddressAffixes DefaultAffixes =
        new(BuildingNumber: null, Room: "Apt ", Floor: "Floor ", NeighborhoodSuffix: null, BuildingNumberLeadsLine: true);

    private static readonly Dictionary<string, AddressAffixes> AffixesByCountry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // TR: "Fırat Mh. 553. Sk. No: 10/G Daire: 8 Kat: 2" — numara sokaktan SONRA, etiketli.
            // Mahalle son eki "Mh." KISA kalır: o bir ad soneki (yer adının parçası), alan etiketi değil —
            // hem N11 hem kargo çıktıları böyle yazıyor.
            ["TR"] = new("No: ", "Daire: ", "Kat: ", " Mh.", BuildingNumberLeadsLine: false),
        };

    // Değer doluysa ülkeye özel son eki ekler (ör. "Caferağa" + " Mh." → "Caferağa Mh."); boşsa null.
    // suffix null → o ülkede mahalle son eki YOK (ör. ABD) → ad olduğu gibi.
    // Değer ZATEN son eki taşıyorsa ("Şerifali Mh." — N11 böyle gönderir) İKİNCİ kez eklenmez ("… Mh. Mh." olmaz).
    private static string? AppendSuffix(string? value, string? suffix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(suffix) || EndsWithNeighborhoodSuffix(trimmed))
        {
            return trimmed;
        }

        return trimmed + suffix;
    }

    // "Mahallesi/Mah./Mh." (noktalı/noktasız) ile bitiyor mu — çift son ek koruması.
    private static bool EndsWithNeighborhoodSuffix(string value)
    {
        return value.EndsWith("Mahallesi", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("Mah.", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("Mh.", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(" Mah", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(" Mh", StringComparison.OrdinalIgnoreCase);
    }

    // Değer doluysa ön ek ekler (ör. "No:" + "5" → "No:5"); boşsa null.
    private static string? Prefix(string? prefix, string? value)
    {
        // Ön ek null olabilir: o kültürde bina numarası ön-eksiz yazılır ("10 Downing Street" — "Nr:" YOK).
        return string.IsNullOrWhiteSpace(value) ? null : prefix + value.Trim();
    }

    /// <summary>Büyük harfe çevirir (boşsa null). KÜLTÜRE DUYARLI (<c>CurrentCulture</c>) — invariant çevrim
    /// Türkçe'de "Türkiye" → "TÜRKIYE" (noktasız I) üretirdi; doğrusu "TÜRKİYE".</summary>
    private static string? UpperOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpper(CultureInfo.CurrentCulture);
    }

    // İlk dolu değeri döner (hepsi boşsa null) — ad varsa ad, yoksa koda düşme.
    private static string? FirstNonEmpty(params string?[] candidates)
    {
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
    }

    // Boş/whitespace parçaları atlayıp kalanları ayraçla birleştirir (hepsi boşsa boş string).
    private static string JoinNonEmpty(string separator, params string?[] parts)
    {
        return string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>Adres "boş" mu — İl (<see cref="IAddressEditModel.City"/>) VE Açık Adres
    /// (<see cref="IAddressEditModel.Line"/>) boşsa boş sayılır (zorunlu iki alan).</summary>
    public static bool IsEmpty(IAddressEditModel? model)
    {
        return model is null
            || (string.IsNullOrWhiteSpace(model.City) && string.IsNullOrWhiteSpace(model.Line));
    }
}
