using System;
using System.Collections.Generic;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// 8GR.IAR.995 UÇTAN-UCA SENARYOSU — mock-first (ADR; 2026-07-25 Hakan onaylı senaryo). GERÇEK ürün/grup
/// kurulumuyla birebir: hedef 8.00 gr, grup madenleri G1.0/G2.5/G5.0/... GR 995. Bu testler zincirin SAF
/// hesap katmanını (satılabilir adet) sahte stok sözlükleriyle sürer — DB yok, N11 yok.
/// <para>Stok AKIŞI senaryosu: VoucherLine stok düşürür → aynı reçetenin satılabilir adedi ANINDA düşer →
/// tükenişte 0 → alışla geri açılır. Kanal push'unun kendisi entegrasyon dilimidir (dirty-check N11 servisinde);
/// buradaki sözleşme "kanala GİDECEK adet"tir.</para>
/// </summary>
public class OversellScenarioTests
{
    private static readonly Guid G1 = SimpleGuidGenerator.Instance.Create();   // G1.0 GR 995 (1 gr/parça)
    private static readonly Guid G25 = SimpleGuidGenerator.Instance.Create();  // G2.5 GR 995 (2.5 gr/parça)
    private static readonly Guid G5 = SimpleGuidGenerator.Instance.Create();   // G5.0 GR 995 (5 gr/parça)

    // 8 gr ürünün "5.0×1 + 1.0×3" kombinasyon reçetesi: 1 birim = 5 gr G5 + 3 gr G1.
    private static readonly RecipeCommodityRequirement[] Recipe_5x1_1x3 =
    {
        Maden(G5, null, 5.0m),
        Maden(G1, null, 3.0m),
    };

    // "2.5×2 + 1.0×3" reçetesi: 1 birim = 5 gr G2.5 + 3 gr G1.
    private static readonly RecipeCommodityRequirement[] Recipe_25x2_1x3 =
    {
        Maden(G25, null, 5.0m),
        Maden(G1, null, 3.0m),
    };

    [Fact]
    public void Adim1_baslangic_stogu_iki_paket_uretir()
    {
        // Başlangıç: G5=12gr (senaryo gerçek stoğu), G1=18gr → 5.0×1+1.0×3 için min(12/5, 18/3) = min(2,6) = 2.
        var stok = Stok((G5, null, 12m), (G1, null, 18m));

        SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok).ShouldBe(2);
    }

    [Fact]
    public void Adim2_satis_stogu_dusurunce_paket_aninda_duser()
    {
        // VoucherLine: 2 adet G5.0 çıkışı (12gr → 2gr kaldı... hayır: 2 parça = 10gr çıkış → 2gr).
        // min(2/5, 18/3) = min(0,6) = 0 — bir önceki "2 paket" ilanı ANINDA geçersiz; kanala 0 gitmeli.
        var stok = Stok((G5, null, 2m), (G1, null, 18m));

        SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok).ShouldBe(0);
    }

    [Fact]
    public void Adim2b_alternatif_recete_ayni_stokta_hala_satilabilir()
    {
        // Aynı an: G2.5 reçetesi bağımsız — G25=8gr, G1=18gr → min(8/5, 18/3) = min(1,6) = 1.
        // Çoklu modda müşteri seçeneği yaşamaya devam eder; ana varyant el değiştirir (Rank yeniden).
        var stok = Stok((G25, null, 8m), (G1, null, 18m));

        SellableStockCalculator.Calculate(Recipe_25x2_1x3, stok).ShouldBe(1);
    }

    [Fact]
    public void Adim3_tukenis_tum_receteler_sifir()
    {
        // Tükeniş: G5=0, G25=1gr (yarım parça bile değil), G1=2gr → iki reçete de 0. Oversell İMKÂNSIZ.
        var stok = Stok((G5, null, 0m), (G25, null, 1m), (G1, null, 2m));

        SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok).ShouldBe(0);
        SellableStockCalculator.Calculate(Recipe_25x2_1x3, stok).ShouldBe(0);
    }

    [Fact]
    public void Adim4_alis_stogu_geri_acar()
    {
        // Alış fişi: G5'e 15gr giriş → min(15/5, 18/3) = 3 paket. Satış kanalı yeniden açılır.
        var stok = Stok((G5, null, 15m), (G1, null, 18m));

        SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok).ShouldBe(3);
    }

    [Fact]
    public void Kenar_negatif_stok_kanala_asla_eksi_gitmez()
    {
        // 2026-07-25 dersi: işaret ters okunursa stok eksi görünebilir; hesap eksiyi 0'a kırpar.
        var stok = Stok((G5, null, -10m), (G1, null, 18m));

        SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok).ShouldBe(0);
    }

    [Fact]
    public void Kenar_ayni_hesap_iki_kez_ayni_sonuc_idempotent()
    {
        // Aynı event iki kez işlense sonuç değişmez (job idempotency'sinin saf çekirdeği).
        var stok = Stok((G5, null, 12m), (G1, null, 18m));

        var birinci = SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok);
        var ikinci = SellableStockCalculator.Calculate(Recipe_5x1_1x3, stok);

        ikinci.ShouldBe(birinci);
    }

    [Fact]
    public void Kenar_varyant_granul_stok_dogru_recete_satirina_gider()
    {
        // G5'in belirli bir varyantına bağlı reçete satırı: varyant stoğu 5gr (1 paket), toplam 15gr olsa bile.
        var v5 = SimpleGuidGenerator.Instance.Create();
        var recete = new[] { Maden(G5, v5, 5.0m), Maden(G1, null, 3.0m) };
        var stok = Stok((G5, v5, 5m), (G5, null, 15m), (G1, null, 18m));

        SellableStockCalculator.Calculate(recete, stok).ShouldBe(1);
    }

    /// <summary>Maden reçete satırı — maden GRAMLA kısıtlar (adet boyutu bilinçli 0).</summary>
    private static RecipeCommodityRequirement Maden(Guid metalId, Guid? variantId, decimal gram)
    {
        return new RecipeCommodityRequirement(ProcessType.Metal, metalId, variantId, gram, 0m);
    }

    private static Dictionary<CommodityStockKey, CommodityAvailability> Stok(
        params (Guid MetalId, Guid? VariantId, decimal Gram)[] rows)
    {
        var dict = new Dictionary<CommodityStockKey, CommodityAvailability>();
        foreach (var (metalId, variantId, gram) in rows)
        {
            dict[new CommodityStockKey(ProcessType.Metal, metalId, variantId)] = new CommodityAvailability(gram, 0m);
        }

        return dict;
    }
}
