using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Integration.TradeXpress.Products;

/// <summary>Stamp hesabının girdisi — bir reçete satırının KİMLİĞİNİ ve MİKTARINI özetler.
/// Entity'ye bağımlı değildir ki hesap saf kalsın ve doğrudan test edilebilsin.</summary>
public readonly record struct RecipeStampLine(
    int LineOrder,
    int ComponentType,
    int? CommodityFamily,
    Guid? CommodityId,
    Guid? CommodityVariantId,
    decimal Quantity,
    decimal Amount,
    decimal Factor,
    DateTime? LastChangedUtc);

/// <summary>
/// REÇETE DOĞRULAMA STAMP'İ — "bu varyantın onayı hâlâ geçerli mi?" sorusunun saf hesabı.
///
/// <para><b>Neden var:</b> varyant doğrulaması bir kereye mahsus tik olursa emniyet değil SÜS olur — reçete
/// sonradan değişir, ürün "onaylı" görünmeye devam eder ve yanlış fiyatla satılır. Stamp onay anında
/// saklanır, push anında yeniden hesaplanır; tutmuyorsa varyant doğrulanmamış sayılır. Böylece reçeteye
/// dokunan herkes onayı düşürmüş olur ve <b>ayrı bir olay/tetik altyapısı gerekmez</b>.</para>
///
/// <para><b>İKİ KADEMELİ</b> (2026-08-05 kararı) — biçim: <c>"{ticks}|{hash}"</c>.
/// <list type="bullet">
///   <item><b>Zaman kısmı:</b> satırların en son değişim anı. Ucuz, her push'ta bakılır.</item>
///   <item><b>İçerik kısmı:</b> (sıra, tür, aile, emtia, varyant, miktar, tutar, milyem) alanlarının
///   sıralı hash'i. Yalnız zaman kısmı değiştiğinde kıyaslanır.</item>
/// </list>
/// <b>Neden ikisi birden:</b> salt timestamp, dokunulup aynı bırakılan satırda YANLIŞ POZİTİF üretir
/// (onay boşuna düşer, kullanıcı bıkar). Salt içerik hash'i ise sıralama/yuvarlama/null detaylarında
/// sessizce yanlış olabilir — ve sessiz yanlış, bu projede en pahalı hata sınıfıdır. Birlikte kullanınca
/// biri diğerinin zayıflığını kapatır: içerik aynıysa hash aynı çıkar, zaman değişse bile onay AYAKTA kalır.</para>
///
/// <para><b>Kültür/biçim tuzağı:</b> ondalıklar <see cref="CultureInfo.InvariantCulture"/> ile ve SABİT
/// biçimde yazılır. Aksi halde aynı reçete Türkçe kültürde (virgül) başka, İngilizce kültürde (nokta) başka
/// hash üretir → onay makinede geçerli, sunucuda geçersiz olurdu.</para>
/// </summary>
public static class RecipeVerificationStamp
{
    /// <summary>Reçetesiz varyantın stamp'i — boş liste ile onay verilirse de tutarlı kıyaslansın.</summary>
    public const string EmptyRecipe = "0|-";

    private const char SectionSeparator = '|';
    private const char FieldSeparator = ';';
    private const char LineSeparator = '\n';

    /// <summary>Miktar alanlarının SABİT biçimi — N5 milyem hassasiyetini korur, kültürden bağımsızdır.</summary>
    private const string DecimalFormat = "0.#####";

    /// <summary>Verilen reçete satırlarından stamp üretir. Satır sırası GİRDİDEN bağımsızdır —
    /// aynı reçete farklı sırada gelse de aynı stamp çıkar (aksi halde salt yeniden sıralama onayı düşürürdü).</summary>
    public static string Compute(IEnumerable<RecipeStampLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var ordered = lines
            .OrderBy(l => l.LineOrder)
            .ThenBy(l => l.CommodityId)
            .ThenBy(l => l.CommodityVariantId)
            .ToList();

        if (ordered.Count == 0)
        {
            return EmptyRecipe;
        }

        var ticks = ordered
            .Select(l => l.LastChangedUtc?.Ticks ?? 0L)
            .DefaultIfEmpty(0L)
            .Max();

        var builder = new StringBuilder();
        foreach (var line in ordered)
        {
            builder
                .Append(line.LineOrder).Append(FieldSeparator)
                .Append(line.ComponentType).Append(FieldSeparator)
                .Append(line.CommodityFamily?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(FieldSeparator)
                .Append(line.CommodityId?.ToString("N") ?? string.Empty).Append(FieldSeparator)
                .Append(line.CommodityVariantId?.ToString("N") ?? string.Empty).Append(FieldSeparator)
                .Append(line.Quantity.ToString(DecimalFormat, CultureInfo.InvariantCulture)).Append(FieldSeparator)
                .Append(line.Amount.ToString(DecimalFormat, CultureInfo.InvariantCulture)).Append(FieldSeparator)
                .Append(line.Factor.ToString(DecimalFormat, CultureInfo.InvariantCulture))
                .Append(LineSeparator);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        return ticks.ToString(CultureInfo.InvariantCulture) + SectionSeparator + hash;
    }

    /// <summary>İki stamp aynı reçeteyi mi anlatıyor.
    /// <para><b>Kademeli kıyas:</b> zaman kısmı aynıysa içerik de aynıdır (kısa devre). Zaman değişmişse
    /// İÇERİK kısmına bakılır — satıra dokunulup aynı bırakıldıysa onay AYAKTA kalır. Bu, salt
    /// timestamp'in yanlış pozitifini kapatan yerdir.</para></summary>
    public static bool Matches(string? stored, string? current)
    {
        if (stored is null || current is null)
        {
            return false;
        }

        if (string.Equals(stored, current, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(ContentOf(stored), ContentOf(current), StringComparison.Ordinal);
    }

    /// <summary>Stamp'in içerik (hash) kısmı; ayraç yoksa tamamı içerik sayılır (ileri uyumluluk).</summary>
    private static string ContentOf(string stamp)
    {
        var index = stamp.IndexOf(SectionSeparator);
        return index < 0 ? stamp : stamp[(index + 1)..];
    }
}
