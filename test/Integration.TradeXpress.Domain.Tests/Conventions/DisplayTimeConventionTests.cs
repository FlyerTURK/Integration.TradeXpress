using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// TIMESTAMP HAM UTC GÖSTERİLEMEZ (governance Katman 2).
///
/// <para><b>Kural (CLAUDE.md §6):</b> kayıt UTC, GÖRÜNTÜ kullanıcının yerel saati — dönüşüm MERKEZÎDİR
/// (<c>UtcLocalText</c> / <c>IDisplayTimeConverter</c>), sayfa-başı elle çeviri yoktur.</para>
///
/// <para><b>Neden konvansiyon testi (2026-08-07):</b> kural yazılıydı ama merkezî bileşen yalnız 2 dosyada, 4 yerde
/// kullanılıyordu; sipariş listesi/formu ve medya kütüphanesi ham UTC basıyordu. Hata SESSİZ: ekranda hiçbir
/// yerde "UTC" yazmaz, yalnız saat 3 saat geride görünür. Müşteri "23:40'ta sipariş verdim" derken panoda
/// 20:40 yazar ve kimse bunu bir hata olarak okumaz. Kural insan dikkatine bırakıldığı sürece her yeni sayfa
/// aynı sapmayı tekrarlar.</para>
///
/// <para><b>Date-only iş tarihleri KAPSAM DIŞI</b> (<c>VoucherDate</c>/<c>DueDate</c>/<c>AsOfDate</c>...):
/// bunlar wall-clock semantiktir, çevrilirlerse GÜN KAYAR — ayrı ve daha ağır bir hata. Alt taraftaki
/// <c>DateOnlyFields</c> listesi bilinçli muafiyet.</para>
/// </summary>
public class DisplayTimeConventionTests
{
    /// <summary>AN (timestamp) taşıyan alanlar — UTC saklanır, çevrilerek gösterilmeli.</summary>
    private static readonly string[] TimestampFields =
    {
        "CreationTime", "LastModificationTime", "DeletionTime",
        "OrderDate", "FetchedAt", "MatchedAt", "ActionAt", "ReservedAt", "ReleasedAt",
        "LastSentAt", "PushedAt", "SyncedAt", "LastSyncedAt",
        "FirstSeenAt", "OccurredAt", "RateDate",
    };

    /// <summary>DATE-ONLY iş tarihleri — kullanıcının seçtiği gün; çevrilmez (gün kayması yasak).
    /// Bu liste testin kapsamını daraltmak için DEĞİL, okuyucuya ayrımı hatırlatmak için burada durur.</summary>
    private static readonly string[] DateOnlyFields =
    {
        "VoucherDate", "DueDate", "AsOfDate", "ProfitResetDate",
    };

    /// <summary>Grid kolonunda ham tarih formatı YOK — hücre <c>UtcLocalText</c> ile çizilmeli.</summary>
    [Fact]
    public void Timestamp_grid_columns_must_not_render_with_a_raw_display_format()
    {
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var relative = ConventionSource.RelativePath(file);
            foreach (var line in File.ReadAllLines(file).Select((text, i) => (text, no: i + 1)))
            {
                if (!line.text.Contains("DisplayFormat", StringComparison.Ordinal))
                {
                    continue;
                }

                var field = TimestampFields.FirstOrDefault(
                    f => line.text.Contains($"FieldName=\"{f}\"", StringComparison.Ordinal));
                if (field is not null)
                {
                    violations.Add($"{relative}:{line.no} — {field}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Zaman damgası kolonu ham DisplayFormat ile çiziliyor (UTC görünür). CellDisplayTemplate + " +
            "<UtcLocalText Value=\"...\" /> kullan; FieldName'i KORU ki sıralama/filtre bozulmasın:\n"
            + string.Join("\n", violations));
    }

    /// <summary>Timestamp elle <c>ToString</c> ile basılamaz (ne .razor'da ne code-behind'da).</summary>
    [Fact]
    public void Timestamps_must_not_be_formatted_by_hand()
    {
        var violations = new List<string>();
        var patterns = TimestampFields.ToDictionary(
            f => f,
            f => new Regex($@"\b{Regex.Escape(f)}\s*\??\s*\.ToString\s*\(", RegexOptions.Compiled));

        foreach (var file in ConventionSource.EnumerateSource("*.razor")
                     .Concat(ConventionSource.EnumerateSource("*.razor.cs")))
        {
            var relative = ConventionSource.RelativePath(file);

            // Yalnız GÖRÜNTÜ katmanı taranır; Framework'ün merkezî dönüştürücüsü zaten formatlamak zorundadır.
            if (relative.Contains("/Components/Shared/UtcLocalText", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var (field, regex) in patterns)
            {
                if (regex.IsMatch(text))
                {
                    violations.Add($"{relative} — {field}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Zaman damgası elle formatlanıyor → kullanıcı sunucunun/veritabanının saatini görür. Merkezî " +
            $"<UtcLocalText /> kullan. (Date-only iş tarihleri bu kuralın DIŞINDA: {string.Join(", ", DateOnlyFields)})\n"
            + string.Join("\n", violations));
    }
}
