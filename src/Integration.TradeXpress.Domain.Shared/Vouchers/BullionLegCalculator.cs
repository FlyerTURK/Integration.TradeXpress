using Integration.TradeXpress.Bullions;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Takoz (Bullion) satırının bakiye bacaklarını hesaplayan saf motor — ERPPRO <c>Cari.AcceptTransaction</c>
/// TAKOZ bloğunun C# çevirisi. <b>İşaret:</b> + = alacak, − = borç; sonuç bacakları YÖN İŞARETLİDİR (poster ek
/// işaret UYGULAMAZ). GİRİŞ'te <c>qty = Amount + AssayAmount</c> (çeşni numunesi cari alacağına dahil), ÇIKIŞ'ta
/// çeşni EKLENMEZ. İşçilik fiyatları 1000 birim başına girilir. ⚠ ERPPROV3'ten FARK: 4 metal de işçilikli
/// (PT/PD işçilik EKLENDİ — ERPPROV3'te yalnız altın+gümüş vardı).
/// </summary>
public static class BullionLegCalculator
{
    /// <summary>Sıfır-güvenli bölme.</summary>
    private static decimal Div(decimal value, decimal by) => by == 0m ? 0m : value / by;

    public static BullionLegResult ComputeBullion(BullionLegInput i)
    {
        var isInflow = ((int)i.Direction % 2) == 0;   // Inbound (çift) = giriş
        var sign     = isInflow ? 1m : -1m;

        // GİRİŞ'te çeşni miktarı bakiyeye dahil; ÇIKIŞ'ta numune dükkânda kalır.
        var qty = i.Amount + (isInflow ? i.AssayAmount : 0m);

        // ── RAPORSUZ: tek pseudo bacak ("TAKOZ" birimi = MainUnit) ──
        if (!i.IsReport)
            return new BullionLegResult(0m, 0m, 0m, 0m, 0m, sign * qty, 0m);

        // ── RAPORLU: metal + işçilik bacakları ──
        var gold      = qty * i.GoldFactor;
        var silver    = 0m;
        var platinum  = 0m;
        var palladium = 0m;

        // İşçilik brüt — (rate/1000 × metalHas × rateBirimKuru) / işçilikTahsilBirimKuru. 4 metal.
        var grossLabor =
            Div(i.GoldLaborRate      / 1000m * (qty * i.GoldFactor)      * i.GoldLaborUnitRate,      i.PayUnitRate) +
            Div(i.SilverLaborRate    / 1000m * (qty * i.SilverFactor)    * i.SilverLaborUnitRate,    i.PayUnitRate) +
            Div(i.PlatinumLaborRate  / 1000m * (qty * i.PlatinumFactor)  * i.PlatinumLaborUnitRate,  i.PayUnitRate) +
            Div(i.PalladiumLaborRate / 1000m * (qty * i.PalladiumFactor) * i.PalladiumLaborUnitRate, i.PayUnitRate);
        var laborDeduction = 0m;

        // Yan metal dağıtımları (Madeni Ver / Altına Çevir / İşçilikten Düş / Madeni Bırak).
        Apply(i.SilverMode,    qty * i.SilverFactor,    i.SilverRate);
        Apply(i.PlatinumMode,  qty * i.PlatinumFactor,  i.PlatinumRate,  isPlatinum: true);
        Apply(i.PalladiumMode, qty * i.PalladiumFactor, i.PalladiumRate, isPalladium: true);

        var netLabor = grossLabor - laborDeduction;

        return new BullionLegResult(
            GoldTotal:       sign * gold,
            SilverTotal:     sign * silver,
            PlatinumTotal:   sign * platinum,
            PalladiumTotal:  sign * palladium,
            LaborTotal:      -sign * netLabor,   // girişte işçilik cariyi BORÇLANDIRIR
            UnreportedTotal: 0m,
            GrossLabor:      grossLabor);

        void Apply(MetalDisposition? mode, decimal metalHas, decimal metalRate,
                   bool isPlatinum = false, bool isPalladium = false)
        {
            switch (mode ?? MetalDisposition.Deliver)
            {
                case MetalDisposition.Deliver:
                    if (isPlatinum)       platinum  += metalHas;
                    else if (isPalladium) palladium += metalHas;
                    else                  silver    += metalHas;
                    break;
                case MetalDisposition.ConvertToGold:
                    gold += Div(metalHas * metalRate, i.GoldRate);
                    break;
                case MetalDisposition.DeductFromLabor:
                    laborDeduction += Div(metalHas * metalRate, i.PayUnitRate);
                    break;
                // Keep (Madeni Bırak): bakiyeye yansımaz.
            }
        }
    }
}

/// <summary>Takoz bacak hesabı girdisi — satırın ham takoz alanları + kayıt anı kur snapshot'ları.
/// İşçilik fiyatları 1000 birim başına. (PT/PD işçilik ERPPROV3'te yok — eklendi.)</summary>
public sealed record BullionLegInput(
    ProcessDirectionType Direction,
    bool                 IsReport,
    decimal              Amount,
    decimal              AssayAmount,
    decimal              GoldFactor,
    decimal              SilverFactor,
    decimal              PlatinumFactor,
    decimal              PalladiumFactor,
    MetalDisposition?    SilverMode,
    MetalDisposition?    PlatinumMode,
    MetalDisposition?    PalladiumMode,
    decimal              GoldLaborRate,
    decimal              SilverLaborRate,
    decimal              PlatinumLaborRate,
    decimal              PalladiumLaborRate,
    decimal              GoldRate,
    decimal              SilverRate,
    decimal              PlatinumRate,
    decimal              PalladiumRate,
    decimal              PayUnitRate,
    decimal              GoldLaborUnitRate,
    decimal              SilverLaborUnitRate,
    decimal              PlatinumLaborUnitRate,
    decimal              PalladiumLaborUnitRate);

/// <summary>Takoz bacak sonuçları — YÖN İŞARETLİ (+ alacak / − borç). Poster ek işaret uygulamadan birimlere dağıtır.</summary>
public sealed record BullionLegResult(
    decimal GoldTotal,
    decimal SilverTotal,
    decimal PlatinumTotal,
    decimal PalladiumTotal,
    decimal LaborTotal,
    decimal UnreportedTotal,
    decimal GrossLabor);
