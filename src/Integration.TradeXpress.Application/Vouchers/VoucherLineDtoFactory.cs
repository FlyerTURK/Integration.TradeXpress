namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherLine ↔ DTO eşlemesinin TEK kaynağı. Bilinçli olarak ELLE yazılır, Mapperly'ye ÇEVRİLMEZ:
/// <see cref="VoucherLineDto"/> kompozit/çok-kaynaklı — entity'nin yalnız PERSISTED alt-kümesi +
/// voucher-header bağlamı (VoucherDate/VoucherNumber, çağıran doldurur) + okuma-anı çözülen *UnitCode +
/// bullion + running-balance. Mapperly'ye zorlamak ~55 MapperIgnoreTarget / zayıf strateji getirir,
/// netlik kazandırmaz; üstelik name-match, MapLine'ın BİLEREK atladığı alanları map'leyip davranış
/// değiştirebilir. <see cref="MapLine"/> ve <see cref="ToLineInput"/> alan listeleri birbirinin
/// eşleniğidir — satır alanı eklerken İKİSİNİ birden güncelle.
/// </summary>
public static class VoucherLineDtoFactory
{
    /// <summary>Entity → DTO. Header alanlarını (VoucherDate/VoucherNumber/stamp) çağıran doldurur.</summary>
    public static VoucherLineDto MapLine(VoucherLine l)
    {
        return new VoucherLineDto
        {
            Id               = l.Id,
            VoucherId        = l.VoucherId,
            Type             = l.Type,
            Direction        = l.Direction,
            PaymentType      = l.PaymentType,
            CommodityId      = l.CommodityId,
            CommodityCode    = l.CommodityCode,
            VariantId        = l.VariantId,
            VariantCode      = l.VariantCode,
            Quantity         = l.Quantity,
            Amount           = l.Amount,
            Factor           = l.Factor,
            Total            = l.Total,
            MainUnitId       = l.MainUnitId,
            PayCommodityId   = l.PayCommodityId,
            PayCommodityCode = l.PayCommodityCode,
            PayUnitId        = l.PayUnitId,
            PayFactor        = l.PayFactor,
            MarketPrice      = l.MarketPrice,
            PayTotal         = l.PayTotal,
            PayUnitRate      = l.PayUnitRate,
            Profit           = l.Profit,
            DueDate          = l.DueDate,
            Description      = l.Description,

            // ── VİRMAN (Transfer) alanları — düzenleme akışı karşı hesabı/bağı geri okur ──
            CounterAccountId = l.CounterAccountId,
            LinkId           = l.LinkId,

            // ── TAKOZ (Bullion) alanları — DÜZELT akışı bunlarsız paneli default'larla açıyordu
            //    (raporsuz/Gold/milyemler 0): kaydetme yönü (ToLineInput) tamdı, okuma yönü eksikti. ──
            BullionType            = l.BullionType,
            AssayOfficeId          = l.AssayOfficeId,
            ReportNo               = l.ReportNo,
            IsReport               = l.IsReport,
            IsExtra                = l.IsExtra,
            AssayAmount            = l.AssayAmount,
            SilverFactor           = l.SilverFactor,
            PlatinumFactor         = l.PlatinumFactor,
            PalladiumFactor        = l.PalladiumFactor,
            SilverMode             = l.SilverMode,
            PlatinumMode           = l.PlatinumMode,
            PalladiumMode          = l.PalladiumMode,
            LaborMode              = l.LaborMode,
            SilverLaborRate        = l.SilverLaborRate,
            PlatinumLaborRate      = l.PlatinumLaborRate,
            PalladiumLaborRate     = l.PalladiumLaborRate,
            GoldLaborUnitId        = l.GoldLaborUnitId,
            SilverLaborUnitId      = l.SilverLaborUnitId,
            PlatinumLaborUnitId    = l.PlatinumLaborUnitId,
            PalladiumLaborUnitId   = l.PalladiumLaborUnitId,
            SilverUnitId           = l.SilverUnitId,
            PlatinumUnitId         = l.PlatinumUnitId,
            PalladiumUnitId        = l.PalladiumUnitId,
            GoldRate               = l.GoldRate,
            SilverRate             = l.SilverRate,
            PlatinumRate           = l.PlatinumRate,
            PalladiumRate          = l.PalladiumRate,
            GoldLaborUnitRate      = l.GoldLaborUnitRate,
            SilverLaborUnitRate    = l.SilverLaborUnitRate,
            PlatinumLaborUnitRate  = l.PlatinumLaborUnitRate,
            PalladiumLaborUnitRate = l.PalladiumLaborUnitRate,

            CreationTime     = l.CreationTime,
            CreatorId        = l.CreatorId,
        };
    }

    /// <summary>DTO → domain satır girdisi (WYSIWYG: ekranda görünen değerler AYNEN taşınır).</summary>
    public static VoucherLineInput ToLineInput(VoucherLineDto i)
    {
        return new VoucherLineInput(
            Type:             i.Type,
            Direction:        i.Direction,
            PaymentType:      i.PaymentType,
            CommodityId:      i.CommodityId,
            CommodityCode:    i.CommodityCode,
            VariantId:        i.VariantId,
            VariantCode:      i.VariantCode,
            Quantity:         i.Quantity,
            Amount:           i.Amount,
            Factor:           i.Factor,
            Total:            i.Total,
            MainUnitId:       i.MainUnitId,
            PayFactor:        i.PayFactor,
            MarketPrice:      i.MarketPrice,
            PayTotal:         i.PayTotal,
            Profit:           i.Profit,
            PayCommodityId:   i.PayCommodityId,
            PayCommodityCode: i.PayCommodityCode,
            PayUnitId:        i.PayUnitId,
            PayUnitRate:      i.PayUnitRate,
            DueDate:          i.DueDate,
            Description:      i.Description,
            CounterAccountId: i.CounterAccountId,
            LinkId:           i.LinkId,
            BullionType:            i.BullionType,
            AssayOfficeId:          i.AssayOfficeId,
            ReportNo:               i.ReportNo,
            IsReport:               i.IsReport,
            IsExtra:                i.IsExtra,
            AssayAmount:            i.AssayAmount,
            SilverFactor:           i.SilverFactor,
            PlatinumFactor:         i.PlatinumFactor,
            PalladiumFactor:        i.PalladiumFactor,
            SilverMode:             i.SilverMode,
            PlatinumMode:           i.PlatinumMode,
            PalladiumMode:          i.PalladiumMode,
            LaborMode:              i.LaborMode,
            SilverLaborRate:        i.SilverLaborRate,
            PlatinumLaborRate:      i.PlatinumLaborRate,
            PalladiumLaborRate:     i.PalladiumLaborRate,
            GoldLaborUnitId:        i.GoldLaborUnitId,
            SilverLaborUnitId:      i.SilverLaborUnitId,
            PlatinumLaborUnitId:    i.PlatinumLaborUnitId,
            PalladiumLaborUnitId:   i.PalladiumLaborUnitId,
            SilverUnitId:           i.SilverUnitId,
            PlatinumUnitId:         i.PlatinumUnitId,
            PalladiumUnitId:        i.PalladiumUnitId,
            GoldRate:               i.GoldRate,
            SilverRate:             i.SilverRate,
            PlatinumRate:           i.PlatinumRate,
            PalladiumRate:          i.PalladiumRate,
            GoldLaborUnitRate:      i.GoldLaborUnitRate,
            SilverLaborUnitRate:    i.SilverLaborUnitRate,
            PlatinumLaborUnitRate:  i.PlatinumLaborUnitRate,
            PalladiumLaborUnitRate: i.PalladiumLaborUnitRate);
    }
}
