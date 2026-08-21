using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// LOKALİZASYON DOSYALARINDA ÇİFT-KODLANMIŞ METİN OLMAZ.
///
/// <para><b>Sabitlenen hata:</b> araç çubuğunda "Mağazadan İçe Aktar" yerine "MaÄazadan Ä°Ã§e Aktar"
/// yazıyordu. Sebep bir kod hatası değil DOSYA KODLAMASIYDI: UTF-8 bir dosya bir noktada Latin-1/CP1252
/// olarak okunup yeniden UTF-8 kaydedilmiş, Türkçe harfler ikişer bozuk karaktere dönüşmüştü. tr.json'da
/// 5, en.json'da 54 satır böyleydi ve <b>hepsi commit'lenmişti</b>.</para>
///
/// <para><b>Neden derleme yakalamaz:</b> bozuk metin geçerli bir JSON dizesidir. Anahtar paritesi de
/// yakalamaz — anahtarlar ASCII, bozulan yalnız DEĞERLER. Yani tek fark eden şey, o ekranı açan
/// kullanıcının gözüdür; ekran açılmıyorsa (ör. etiket dar ekranda gizleniyorsa) yıllarca kalabilir.</para>
///
/// <para><b>Yöntem:</b> mojibake'in imza dizileri aranır. Bunlar meşru metinde birlikte GEÇMEZ:
/// <c>Ã</c>/<c>Ä</c>/<c>Å</c> ardından bir kontrol/simge karakteri, ya da <c>â€</c> / <c>Â</c> öbeği.
/// Tarama TÜM kültür dosyalarını kapsar — bugün yalnız tr/en kullanılıyor olması yarın da öyle
/// kalacağı anlamına gelmez.</para>
/// </summary>
public class LocalizationEncodingTests
{
    /// <summary>UTF-8'in CP1252 olarak okunmasının bıraktığı imzalar. Kısa ve ayırt edici tutuldu:
    /// tek başına "Ã" meşru olabilir (Portekizce), ama bu diziler pratikte yalnız bozulmada görülür.</summary>
    private static readonly string[] MojibakeMarkers =
    {
        "â€",   // — … ' " (em dash, ellipsis, tırnaklar)
        "â†",   // → ← ↑ ↓ (ok işaretleri — U+2190 bloğunun CP1252 bozulması)
        "Ã¼", "Ã¶", "Ã§", "Ã–", "Ã‡", "Ãœ",   // ü ö ç Ö Ç Ü
        "Ä±", "Ä°", "ÄŸ", "Ä", "Å", "Å¾",  // ı İ ğ Ğ ş Ş
        "Â·", "Â ",                            // orta nokta, kırılmaz boşluk
    };

    /// <summary>C1 kontrol karakteri (U+0080–U+009F) lokalizasyon metninde HİÇBİR koşulda meşru değildir —
    /// UTF-8'in LATIN-1 (CP1252 değil) okunup yeniden kaydedilmesi tam bu aralığı üretir ve imza listesi onu
    /// göremez: CP1252 bozulması "â€" gibi GÖRÜNÜR çift üretirken Latin-1 bozulması görünmez kontrol
    /// karakteri bırakır (em dash → "â"+U+0080+U+0094). İlk turda 4 satır bu kör noktadan sızdı.</summary>
    private static bool ContainsC1Control(string line)
    {
        foreach (var ch in line)
        {
            if (ch >= '\u0080' && ch <= '\u009F')
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Localization_files_must_not_contain_double_encoded_text()
    {
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ConventionSource.SrcRoot, "*.json", SearchOption.AllDirectories))
        {
            var relative = ConventionSource.RelativePath(file);
            if (!relative.Contains("/Localization/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (ContainsC1Control(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1} [C1 kontrol karakteri]: {lines[i].Trim()}");
                    continue;
                }

                foreach (var marker in MojibakeMarkers)
                {
                    if (lines[i].Contains(marker, StringComparison.Ordinal))
                    {
                        violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "Lokalizasyon dosyasında ÇİFT-KODLANMIŞ metin var (UTF-8 bir yerde Latin-1 olarak okunup yeniden "
            + "kaydedilmiş). Bu satırlar kullanıcıya bozuk karakter olarak görünür. Dosyayı UTF-8 okuyup UTF-8 "
            + "yazan bir araçla düzelt; PowerShell here-string ile yazma (5.1 .ps1 dosyasını ANSI okur ve "
            + "Türkçe harfleri bozar)."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>ÇİFT-karakterli mojibake imzaları — kaynak kodda da meşru değildir (yorumlar Türkçe yazılır;
    /// tek-karakterli "Ä"/"Å" burada KULLANILMAZ: arama normalize tabloları Türkçe harf listesi tutar).</summary>
    private static readonly string[] SourceMojibakeMarkers =
    {
        "â€", "â†", "Ã¼", "Ã¶", "Ã§", "Ã–", "Ã‡", "Ãœ", "Ä±", "Ä°", "ÄŸ", "ÅŸ", "Å¾", "Â·",
    };

    /// <summary>
    /// AYNI KODLAMA KAZASI KAYNAK KODDA DA OLMAZ — yorumlar (Türkçe, CLAUDE.md §4) okunmaz hâle gelir ve
    /// bir yorumun taşıdığı gerekçe kaybolur. İlk tur yalnız lokalizasyon dosyalarını tarıyordu; altı
    /// code-behind dosyasında 137 bozuk yorum satırı yaşamaya devam ediyordu (bağımsız denetim bulgusu,
    /// 2026-08-14 — onarıldı; bu test tekrarını kapatır). Migration/Designer dosyaları üretilmiş koddur, dışarıda.
    /// </summary>
    [Fact]
    public void Source_files_must_not_contain_double_encoded_text()
    {
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.cs").Concat(ConventionSource.EnumerateSource("*.razor")))
        {
            var relative = ConventionSource.RelativePath(file);
            if (relative.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (ContainsC1Control(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1} [C1 kontrol karakteri]");
                    continue;
                }

                foreach (var marker in SourceMojibakeMarkers)
                {
                    if (lines[i].Contains(marker, StringComparison.Ordinal))
                    {
                        violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "Kaynak dosyada ÇİFT-KODLANMIŞ metin var (yorum okunmaz hâle gelmiş). UTF-8 okuyup UTF-8 yazan bir "
            + "araçla düzelt." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
