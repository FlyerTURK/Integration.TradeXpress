using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// SATIŞA-HAZIRLIK İŞARETLERİNİN KONVANSİYON TESTİ (2026-08-19). İki kural burada tutulur; ikisi de sessizce bozulan
/// türden olduğu için (ekran hata vermez, yalnız YANLIŞ renk gösterir) insan dikkatine bırakılamaz.
///
/// <para><b>1) Ürün formunun satışa hazırlık paneli MOUNT kalmalı.</b> Issue endeksini üreten <c>ProductSaleReadinessPanel</c> ilk
/// sekmenin içeriğidir. DxTabs varsayılan kipinde sekme değişince içerik YIKILIR: kullanıcı Varyantlar sekmesinde
/// eksiği tamamlayıp kaydettiğinde panel artık ayakta olmadığı için endeks tazelenmez ve sekme başlıkları
/// DÜZELTİLMİŞ bir issue'yu kırmızı göstermeye devam eder. <c>RenderMode="TabsRenderMode.OnDemand"</c> ziyaret
/// edilen sekmeyi ayakta tutar — kaldırılırsa bu test KIRMIZI olur.</para>
///
/// <para><b>2) İşaretin çizimi TEK bileşende.</b> "Ağırlık → renk + ikon + rozet" kuralı üç yerde (ürün formu
/// sekmeleri, varyant grid satırı, kanal ürünü formları) tekrarlanacaktı ve bir kez GERÇEKTEN tekrarlandı: aynı
/// Error ağırlığı bir grid'de "dur", diğerinde "ünlem" ikonuyla çizildi. Paletin ÇİZİM üyeleri
/// (<c>ColorOf</c>/<c>BadgeStyle</c>/<c>IconOf</c>) bu yüzden yalnız ortak işaret bileşeninden okunur; kural
/// sorguları (<c>IsActionable</c>/<c>HeadingColorOf</c>) serbesttir — onlar çizim değil KARAR verir
/// (ör. "bu kolon görünsün mü").</para>
/// </summary>
public class SaleReadinessMarkConventionTests
{
    private const string ProductLayoutPath = "src/Integration.TradeXpress.Blazor.Client/Pages/Products/ProductLayout.razor";

    // Paletin ÇİZİM üyeleri: doğrudan DOM'a giden renk/ikon/rozet değerleri.
    private static readonly Regex RenderingPaletteMemberRegex =
        new(@"SaleReadinessPalette\.(ColorOf|BadgeStyle|IconOf)\b", RegexOptions.Compiled);

    // İzinli okuyucular. Yeni bir dosya eklemek = "işareti bir kez daha elle çiziyorum" demektir; önce
    // ReadinessMark'ın yetmediğini göster (kip ekle), listeyi genişletmeyi SON çare say.
    private static readonly HashSet<string> RenderingPaletteAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ortak işaret bileşeni — kuralın tek uygulaması.
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/ReadinessMark.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/ReadinessMark.razor.cs",

        // Satışa hazırlık panelinin ISSUE LİSTESİ: işaret değil, tam issue tablosu (ağırlık sütunu ikon + metin taşır). İkon
        // eşlemesini paletten okur; kendi kopyasını taşımadığı için sapma üretmez.
        "src/Integration.TradeXpress.Blazor.Client/Pages/Products/ProductSaleReadinessPanel.razor.cs",
    };

    [Fact]
    public void The_product_form_must_keep_the_readiness_cockpit_mounted_across_tabs()
    {
        var path = Path.Combine(ConventionSource.RepoRoot, ProductLayoutPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue($"{ProductLayoutPath} bulunamadı — dosya taşındıysa bu testin yolu güncellenmeli.");

        var markup = File.ReadAllText(path);

        markup.ShouldContain(
            "RenderMode=\"TabsRenderMode.OnDemand\"",
            Case.Sensitive,
            "Ürün formunun DrillTabs'i OnDemand kipinde OLMALI: aksi hâlde satışa hazırlık sekmesi terk edilince yıkılır, "
            + "bulgu endeksi bayat kalır ve düzeltilmiş bir bulgu sekme başlığında kırmızı görünmeye devam eder.");
    }

    [Fact]
    public void Readiness_marks_must_be_drawn_only_by_the_shared_mark_component()
    {
        var violations = new List<string>();

        foreach (var pattern in new[] { "*.razor", "*.cs" })
        {
            foreach (var file in ConventionSource.EnumerateSource(pattern))
            {
                var rel = ConventionSource.RelativePath(file);
                if (RenderingPaletteAllowList.Contains(rel))
                {
                    continue;
                }

                if (RenderingPaletteMemberRegex.IsMatch(File.ReadAllText(file)))
                {
                    violations.Add(
                        $"{rel}: satışa-hazırlık işareti elle çiziliyor (SaleReadinessPalette.ColorOf/BadgeStyle/IconOf) "
                        + "→ <ReadinessMark> kullan; kural tek yerde kalsın.");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki dosyalar işaret çizimini kopyalıyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
