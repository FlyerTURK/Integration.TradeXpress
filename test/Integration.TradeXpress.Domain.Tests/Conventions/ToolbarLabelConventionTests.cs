using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// ARAÇ ÇUBUĞU ETİKETİ DAR EKRANDA DA VERİLİR — <c>Text</c> asla ekran genişliğine göre boşaltılmaz.
///
/// <para><b>Sabitlenen hata:</b> iki araç çubuğu düğmesi <c>Text = IsMobile ? null : L[...]</c> yazıyordu.
/// Amaç dar çubukta uzun etiketin diğer öğeleri ezmesini önlemekti; sonuç, öğe TAŞMA MENÜSÜNE düştüğünde
/// orada da yazısız kalmasıydı — kullanıcı ne yaptığı belirsiz ikonlara bakıyordu ve tooltip dokunmatikte
/// açılmadığı için başka ipucu da yoktu.</para>
///
/// <para><b>Neden gereksizdi:</b> dar çubukta etiketi gizleme işini DevExpress KENDİ yapıyor
/// (<c>ToolbarRenderer</c>'da <c>AdaptivityAutoCollapseItemsToIcons=true</c>). Kanıtı, <c>Text</c> taşıyan
/// çerçeve düğmelerinin (Dışa Aktar · Yenile) çubukta ikona düşüp menüde YAZIYLA çıkması. Yani etiketi
/// kaynağında silmek, çözülmüş bir sorunu ikinci kez çözmeye çalışırken menüyü bozuyordu.</para>
///
/// <para><b>Kapsam DAR:</b> yalnız <c>Text</c>/<c>AdaptiveText</c> atamaları. <c>IsMobile</c>'ın kendisi
/// yasak DEĞİLDİR — yerleşim (sütun sayısı, genişlik, gizlenen panel) dar ekranda meşru biçimde değişir;
/// yasaklanan tek şey öğenin ADINI ekrandan silmektir.</para>
/// </summary>
public class ToolbarLabelConventionTests
{
    // Text = IsMobile ? null : ... / Text = !IsMobile ? ... : null / AdaptiveText aynı kural.
    // Boşluk ve satır sonlarına toleranslı; string.Empty de null ile aynı sonucu verir.
    private static readonly Regex BlankedLabelRegex = new(
        @"\b(Adaptive)?Text\s*=\s*[^;,\r\n]*\bIsMobile\b[^;,\r\n]*\b(null|string\.Empty|"""")",
        RegexOptions.Compiled);

    [Fact]
    public void A_toolbar_label_is_never_blanked_out_on_narrow_screens()
    {
        var violations = new List<string>();

        foreach (var pattern in new[] { "*.cs", "*.razor" })
        {
            foreach (var file in ConventionSource.EnumerateSource(pattern))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("IsMobile", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in BlankedLabelRegex.Matches(text))
                {
                    var line = CountLines(text, match.Index);
                    violations.Add($"{ConventionSource.RelativePath(file)}:{line}: {match.Value.Trim()}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Araç çubuğu etiketi dar ekranda BOŞALTILMIŞ. Etiketi kaynağında silmek öğeyi taşma menüsünde de "
            + "yazısız bırakır (dokunmatikte tooltip açılmaz → kullanıcı ne yaptığı belirsiz bir ikon görür). "
            + "Dar çubukta etiketi gizleme işini DevExpress zaten yapıyor: ToolbarRenderer'da "
            + "AdaptivityAutoCollapseItemsToIcons=true. Text'i KOŞULSUZ ver."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static int CountLines(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
