using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Volo.Abp;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 komisyon TSV'sinin (embedded resource <c>N11Categories/n11-commission.tsv</c>; kaynak:
/// .claude/research/n11-taxonomy) SAF parse + eşleme çekirdeği — DB'siz, test edilir. TSV'de ExternalId YOK
/// (panelden ad yolu kopyalanmış) → eşleme AD YOLUYLA yapılır: TSV yolu (Agac4→Agac1_leaf, bitişik tekrarlar
/// düşürülmüş) DB yaprak yolunun (mega sentetik katman dahil kökten) <b>SONEKİ</b> olmalı. Yol eşleşmezse tek
/// isimli yaprak fallback'i denenir; hâlâ çözülmezse satır RAPORLANIR (sessiz geçilmez — görev kuralı).
/// Uygulama <c>N11CategoryAppService.ImportCommissionsAsync</c>'te (host-only) — orada SetCommission çağrılır.
/// </summary>
public static class N11CategoryCommissionImporter
{
    /// <summary>Embedded TSV'yi okur (deploy'da .claude klasörü yok → kaynak paketle taşınır).</summary>
    public static string ReadEmbeddedTsv()
    {
        var assembly = typeof(N11CategoryCommissionImporter).Assembly;
        const string resourceName = "Integration.TradeXpress.N11Categories.n11-commission.tsv";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new BusinessException("TradeXpress:N11:CommissionTsvMissing").WithData("resource", resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>TSV içeriğini satırlara ayrıştırır. Başlık atlanır; yol kolonları (Agac4..Agac1_leaf) trimlenir,
    /// bitişik tekrarlar düşürülür (sığ dallarda yaprak adı üst kolonda da tekrarlanıyor). Oranlardaki '%' ve
    /// '+ KDV' ekleri temizlenir; parse edilemeyen oran null kalır (komisyon kolonu parse edilemezse satır atlanır
    /// ve raporlanır).</summary>
    public static N11CommissionParseResult ParseTsv(string content)
    {
        var rows = new List<N11CommissionRow>();
        var invalid = new List<string>();
        var lines = content.Split('\n');

        for (var i = 1; i < lines.Length; i++)   // 0 = başlık
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length < 5)
            {
                invalid.Add($"satır {i + 1}: kolon sayısı yetersiz ({cells.Length})");
                continue;
            }

            var path = new List<string>();
            for (var c = 0; c < 4 && c < cells.Length; c++)
            {
                var name = cells[c].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                // Bitişik tekrarları düşür ("A > B > B" → "A > B").
                if (path.Count == 0 || !string.Equals(path[^1], name, StringComparison.Ordinal))
                {
                    path.Add(name);
                }
            }

            if (path.Count == 0)
            {
                invalid.Add($"satır {i + 1}: kategori yolu boş");
                continue;
            }

            var commission = ParseRate(cells[4]);
            if (commission is null)
            {
                invalid.Add($"satır {i + 1} ({string.Join(" > ", path)}): komisyon oranı çözülemedi '{cells[4].Trim()}'");
                continue;
            }

            var marketing = cells.Length > 5 ? ParseRate(cells[5]) : null;
            var marketplace = cells.Length > 6 ? ParseRate(cells[6]) : null;
            var payoutDays = cells.Length > 7 && int.TryParse(cells[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
                ? days
                : (int?)null;

            rows.Add(new N11CommissionRow(path, commission.Value, marketing, marketplace, payoutDays, i + 1));
        }

        return new N11CommissionParseResult(rows, invalid);
    }

    /// <summary>TSV satırlarını DB yapraklarına eşler (SAF — repository yok, liste alır). Dönen eşleşmelerde aynı
    /// yaprağa birden çok satır düşerse oranlar AYNIYSA tek kabul, farklıysa çakışma raporu. Eşleşmeyen/muğlak
    /// satırlar nedenleriyle döner (sessiz geçilmez).</summary>
    public static N11CommissionMatchResult Match(IReadOnlyList<N11CommissionRow> rows, IReadOnlyList<N11Category> categories)
    {
        var byExternalId = categories.ToDictionary(c => c.ExternalId, StringComparer.Ordinal);

        // Yaprak yolları (kökten; mega sentetik katman dahil — sonek eşleşmesi üst katmanları umursamaz).
        var leaves = categories
            .Where(c => c.IsLeaf)
            .Select(c => new LeafPath(c, BuildNormalizedPath(c, byExternalId)))
            .ToList();
        var leavesByName = leaves
            .GroupBy(l => l.Path[^1], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Anahtar = ExternalId (kalıcı doğal kimlik) — Guid Id DEĞİL: yeni ctor'lanmış (henüz persist edilmemiş)
        // entity'de Id boş olabilir (entity-convention: ctor'da id atanmaz) → Id'yle keylemek sahte çakışma üretir.
        var matchesByCategory = new Dictionary<string, (N11Category Category, N11CommissionRow Row)>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        var conflicts = new List<string>();

        foreach (var row in rows)
        {
            var normalizedPath = row.Path.Select(N11NameNormalizer.Normalize).ToList();
            var leafName = normalizedPath[^1];

            if (!leavesByName.TryGetValue(leafName, out var candidates))
            {
                unmatched.Add($"{string.Join(" > ", row.Path)} — yaprak adı DB'de yok");
                continue;
            }

            // Önce TAM yol soneki; tek adaysa ada-göre fallback (yol yeniden örgütlenmiş olabilir).
            var pathMatches = candidates.Where(l => EndsWith(l.Path, normalizedPath)).ToList();
            if (pathMatches.Count == 0 && candidates.Count == 1)
            {
                pathMatches = candidates;
            }

            if (pathMatches.Count == 0)
            {
                unmatched.Add($"{string.Join(" > ", row.Path)} — ad var ama yol uyuşmuyor ({candidates.Count} aday)");
                continue;
            }

            if (pathMatches.Count > 1)
            {
                unmatched.Add($"{string.Join(" > ", row.Path)} — MUĞLAK: {pathMatches.Count} yaprak eşleşti");
                continue;
            }

            var leaf = pathMatches[0].Category;
            if (matchesByCategory.TryGetValue(leaf.ExternalId, out var existing))
            {
                if (existing.Row.CommissionRate != row.CommissionRate)
                {
                    conflicts.Add(
                        $"{string.Join(" > ", row.Path)} — aynı yaprağa çakışan oran (%{existing.Row.CommissionRate} ↔ %{row.CommissionRate})");
                }

                continue;   // aynı yaprak + aynı oran = tekrar; ilk eşleşme kalır
            }

            matchesByCategory[leaf.ExternalId] = (leaf, row);
        }

        return new N11CommissionMatchResult(
            matchesByCategory.Values.ToList(),
            unmatched,
            conflicts,
            leaves.Count);
    }

    /// <summary>N11 Pazarlama/Pazaryeri hizmet bedellerinin KDV brüt çarpanı — TSV'de bu bedeller "+ KDV" ifadeli
    /// (oran KDV HARİÇ tutulur), satıcının fiyatından kesilen ise KDV DAHİL yüktür (araştırma SSOT
    /// .claude/research/channel-commissions/ozet.md: %21 + %1×1,2 + %0,67×1,2 ≈ %23 yük). Komisyon oranının kendisi
    /// zaten KDV DAHİL etiketli → çarpan yalnız hizmet bedellerine uygulanır.</summary>
    public const decimal ServiceFeeVatMultiplier = 1.20m;

    /// <summary>Kanal komisyon oranı çözümü (SSOT): kategori oranı → yoksa kanal varsayılanı → yoksa null
    /// (komisyon satırı üretilmez).</summary>
    public static decimal? ResolveCommissionRate(decimal? categoryRate, decimal? channelDefaultRate)
    {
        return categoryRate ?? channelDefaultRate;
    }

    /// <summary>Fiyatlamada kullanılacak EFEKTİF GrossUp oranı (SSOT): çözülmüş komisyon oranı (kategori ?? kanal
    /// varsayılanı) + N11'in TÜM kategorilerde ZORUNLU Pazarlama + Pazaryeri hizmet bedelleri (KDV brütüyle —
    /// <see cref="ServiceFeeVatMultiplier"/>). Bedeller kesintiye komisyonla birlikte girdiğinden tek GrossUp
    /// satırında toplanır; hiçbir bileşen yoksa null (komisyon satırı üretilmez).</summary>
    public static decimal? ResolveEffectiveCommissionRate(
        decimal? categoryRate, decimal? marketingFeeRate, decimal? marketplaceFeeRate, decimal? channelDefaultRate)
    {
        var baseRate = ResolveCommissionRate(categoryRate, channelDefaultRate);
        var serviceFees = ((marketingFeeRate ?? 0m) + (marketplaceFeeRate ?? 0m)) * ServiceFeeVatMultiplier;
        if (baseRate is null && serviceFees == 0m)
        {
            return null;
        }

        return (baseRate ?? 0m) + serviceFees;
    }

    // "%1 + KDV" / "19" / "0.67" → decimal (invariant; virgül ondalığı noktaya çevrilir). Çözülmezse null.
    private static decimal? ParseRate(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        var kdvIndex = cleaned.IndexOf("KDV", StringComparison.OrdinalIgnoreCase);
        if (kdvIndex >= 0)
        {
            cleaned = cleaned[..kdvIndex];
        }

        cleaned = cleaned.Replace("%", string.Empty).Replace("+", string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // Yaprağın kökten normalize ad yolu (parent zinciri; döngü guard'lı — SearchLeafCategoriesAsync.BuildPath paritesi).
    private static List<string> BuildNormalizedPath(N11Category leaf, Dictionary<string, N11Category> byExternalId)
    {
        var parts = new List<string>();
        var current = leaf;
        var guard = 0;
        while (current is not null && guard++ < 20)
        {
            parts.Add(N11NameNormalizer.Normalize(current.Name));
            current = current.ParentExternalId is { } parentId && byExternalId.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        parts.Reverse();
        return parts;
    }

    // sequence sonek karşılaştırması: full [..., a, b, c] suffix [b, c] → true.
    private static bool EndsWith(IReadOnlyList<string> full, IReadOnlyList<string> suffix)
    {
        if (suffix.Count > full.Count)
        {
            return false;
        }

        var offset = full.Count - suffix.Count;
        for (var i = 0; i < suffix.Count; i++)
        {
            if (!string.Equals(full[offset + i], suffix[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record LeafPath(N11Category Category, List<string> Path);
}

/// <summary>TSV'nin tek satırı — ad yolu (bitişik tekrarsız) + oranlar (%, KDV ifadeleri temizlenmiş) + valör.</summary>
public sealed record N11CommissionRow(
    IReadOnlyList<string> Path,
    decimal CommissionRate,
    decimal? MarketingFeeRate,
    decimal? MarketplaceFeeRate,
    int? PayoutDays,
    int LineNumber);

/// <summary>Parse çıktısı — geçerli satırlar + geçersiz satır raporu (satır no + neden).</summary>
public sealed record N11CommissionParseResult(
    IReadOnlyList<N11CommissionRow> Rows,
    IReadOnlyList<string> InvalidRows);

/// <summary>Eşleme çıktısı — yaprak→satır eşleşmeleri + eşleşmeyen/muğlak satır raporu + çakışmalar.</summary>
public sealed record N11CommissionMatchResult(
    IReadOnlyList<(N11Category Category, N11CommissionRow Row)> Matches,
    IReadOnlyList<string> Unmatched,
    IReadOnlyList<string> Conflicts,
    int LeafCount);
