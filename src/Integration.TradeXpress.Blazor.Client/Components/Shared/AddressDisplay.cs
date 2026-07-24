using System;
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
        // Blok 1 — sokak/kapı detayı: mahalle (Mh. son eki) → açık adres → yapısal bina detayı.
        var streetBlock = JoinNonEmpty(" ",
            AppendSuffix(model.Neighborhood, " Mh."),
            model.Line?.Trim(),
            StructuralDetail(model));

        // Blok 2 — yerellik: posta kodu → ilçe → il → ülke kodu.
        var localityBlock = JoinNonEmpty(" ",
            model.PostalCode?.Trim(),
            model.District?.Trim(),
            model.City?.Trim(),
            model.CountryCode?.Trim());

        return JoinNonEmpty(", ", streetBlock, localityBlock);
    }

    // Yapısal bina detayı — bina adı + kapı no (No:) + daire (D:) + kat (K:); boş parçalar atlanır (ör. "No:5 D:3 K:2").
    private static string StructuralDetail(IAddressEditModel model)
    {
        return JoinNonEmpty(" ",
            model.BuildingName?.Trim(),
            Prefix("No:", model.BuildingNumber),
            Prefix("D:", model.Room),
            Prefix("K:", model.Floor));
    }

    // Değer doluysa son ek ekler (ör. "Caferağa" + " Mh." → "Caferağa Mh."); boşsa null (birleştirmede atlanır).
    // Değer ZATEN mahalle son eki taşıyorsa ("Şerifali Mh." — N11 böyle gönderir) İKİNCİ kez eklemez ("… Mh. Mh." olmaz).
    private static string? AppendSuffix(string? value, string suffix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return EndsWithNeighborhoodSuffix(trimmed) ? trimmed : trimmed + suffix;
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
    private static string? Prefix(string prefix, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : prefix + value.Trim();
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
