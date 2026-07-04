using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.AssayOffices;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Takoz (Bullion) stok + çeşni (Assay) havuz okumaları ve takoz-ÇIKIŞ satırının sunucu-otoriter
/// hazırlanışı. Company scope parametreyle gelir — guard çağıran AppService'te kalır.
/// </summary>
public class VoucherBullionStockService : ITransientDependency
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IRepository<AssayOffice, Guid> _assayOfficeRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public VoucherBullionStockService(
        IRepository<Voucher, Guid> repository,
        IRepository<AssayOffice, Guid> assayOfficeRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository            = repository;
        _assayOfficeRepository = assayOfficeRepository;
        _subAccountRepository  = subAccountRepository;
        _asyncExecuter         = asyncExecuter;
    }

    /// <summary>Külçe stok listesi: aktif GİRİŞ satırları; stok = çıkışı olmayan giriş.</summary>
    public async Task<List<BullionStockItemDto>> GetBullionStockAsync(Guid companyId, bool? inStock = null)
    {
        var q = await _repository.GetQueryableAsync();

        // Külçeler = aktif GİRİŞ satırları (fiş başlığındaki SubAccountId ile — VoucherLine'da yok).
        var entries = await _asyncExecuter.ToListAsync(
            from v in q
            from l in v.Lines
            where v.CompanyId == companyId
               && l.Type == ProcessType.Bullion
               && l.Direction == ProcessDirectionType.Inbound
               && !l.IsDeleted
            select new BullionStockItemDto
            {
                EntryLineId     = l.Id,
                Code            = l.CommodityCode,
                BullionType     = l.BullionType,
                IsReport        = l.IsReport ?? false,
                IsExtra         = l.IsExtra ?? false,
                Amount          = l.Amount,
                AssayAmount     = l.AssayAmount ?? 0m,
                GoldFactor      = l.Factor,
                SilverFactor    = l.SilverFactor ?? 0m,
                PlatinumFactor  = l.PlatinumFactor ?? 0m,
                PalladiumFactor = l.PalladiumFactor ?? 0m,
                ReportNo        = l.ReportNo,
                AssayOfficeId   = l.AssayOfficeId,
                EntryDate       = l.CreationTime,
                SubAccountId    = v.SubAccountId,
            });

        if (entries.Count == 0)
        {
            return entries;
        }

        // Aktif çıkışlar → külçe başına son çıkış zamanı (stok = çıkışı olmayan giriş).
        var exits = await _asyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.CompanyId == companyId)
                .SelectMany(v => v.Lines)
                .Where(l => l.Type == ProcessType.Bullion
                         && l.Direction == ProcessDirectionType.Outbound
                         && !l.IsDeleted
                         && l.CommodityId != null)
                .Select(l => new { l.CommodityId, l.CreationTime }));
        var exitByEntry = exits
            .GroupBy(x => x.CommodityId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreationTime));

        foreach (var e in entries)
        {
            e.InStock  = !exitByEntry.ContainsKey(e.EntryLineId);
            e.ExitDate = exitByEntry.TryGetValue(e.EntryLineId, out var d) ? d : null;
        }

        if (inStock is { } stockFilter)
        {
            entries = entries.Where(e => e.InStock == stockFilter).ToList();
        }

        await ResolveBullionStockDisplayAsync(entries);

        return entries.OrderByDescending(e => e.EntryDate).ToList();
    }

    /// <summary>Çeşni stoğu özeti — SQL-side toplama (satır çekmeden): takoz GİRİŞ satırlarının AssayAmount
    /// havuzu (raporsuzda da cari alacağına dahil — BullionLegCalculator giriş kuralı) MİNUS çeşni ÇIKIŞ
    /// satırlarının Amount toplamı. Milyemler ağırlıklı ortalama (Has/Miktar — legacy Cesni paritesi).
    /// Not: külçenin takoz-çıkışı numuneyi düşürmez (numune dükkânda kalır — legacy kural).</summary>
    public async Task<AssayStockDto> GetAssayStockAsync(Guid companyId)
    {
        var q = await _repository.GetQueryableAsync();

        // Giriş havuzu: takoz GİRİŞ satırlarının numunesi (miktar + metal içerikleri).
        var entry = await _asyncExecuter.FirstOrDefaultAsync(
            (from v in q
             where v.CompanyId == companyId
             from l in v.Lines
             where l.Type == ProcessType.Bullion
                && l.Direction == ProcessDirectionType.Inbound
                && !l.IsDeleted
             select l)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Amount = g.Sum(l => l.AssayAmount ?? 0m),
                Has    = g.Sum(l => (l.AssayAmount ?? 0m) * l.Factor),
                Gum    = g.Sum(l => (l.AssayAmount ?? 0m) * (l.SilverFactor ?? 0m)),
            }));

        // Çıkışlar: çeşni satırları (yön daima ÇIKIŞ) havuzdan düşer.
        var exit = await _asyncExecuter.FirstOrDefaultAsync(
            (from v in q
             where v.CompanyId == companyId
             from l in v.Lines
             where l.Type == ProcessType.Assay
                && l.Direction == ProcessDirectionType.Outbound
                && !l.IsDeleted
             select l)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Amount = g.Sum(l => l.Amount),
                Has    = g.Sum(l => l.Amount * l.Factor),
                Gum    = g.Sum(l => l.Amount * (l.SilverFactor ?? 0m)),
            }));

        var amount = (entry?.Amount ?? 0m) - (exit?.Amount ?? 0m);
        var has    = (entry?.Has ?? 0m) - (exit?.Has ?? 0m);
        var gum    = (entry?.Gum ?? 0m) - (exit?.Gum ?? 0m);

        return new AssayStockDto
        {
            Amount   = amount,
            Has      = has,
            Gum      = gum,
            AuMilyem = amount == 0m ? 0m : has / amount,
            AgMilyem = amount == 0m ? 0m : gum / amount,
        };
    }

    /// <summary>Takoz ÇIKIŞ satırının metal verisini (miktar/milyem/rapor/ayar evi/yan-birimler) seçilen GİRİŞ
    /// külçesinden KOPYALAR — client bu alanlara güvenilmez (yalnız işçilik + dağıtım durumlarını gönderir).
    /// Kısmi çıkış YOK: külçe bütünüyle çıkar (Amount girişten aynen). CommodityId = giriş satırı Id'si.</summary>
    public async Task PrepareBullionExitLineAsync(VoucherLineDto input, Guid companyId)
    {
        if (input.CommodityId is not { } entryLineId || entryLineId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Bullion:ExitEntryRequired");
        }

        var entry = await FindBullionEntryLineAsync(entryLineId, companyId)
            ?? throw new BusinessException("TradeXpress:Bullion:ExitEntryNotFound");

        // Külçe kimliği + metal ölçüleri (giriş otoritedir).
        input.CommodityCode = entry.CommodityCode;
        input.BullionType   = entry.BullionType;
        input.AssayOfficeId = entry.AssayOfficeId;
        input.ReportNo      = entry.ReportNo;
        input.IsReport      = entry.IsReport;
        input.IsExtra       = entry.IsExtra;
        input.Amount        = entry.Amount;
        input.AssayAmount   = entry.AssayAmount;
        input.Factor          = entry.Factor;          // altın milyemi
        input.SilverFactor    = entry.SilverFactor;
        input.PlatinumFactor  = entry.PlatinumFactor;
        input.PalladiumFactor = entry.PalladiumFactor;

        // Ana + yan metal bacak birimleri (poster bunlara postlar) girişten kopyalanır.
        input.MainUnitId      = entry.MainUnitId;
        input.SilverUnitId    = entry.SilverUnitId;
        input.PlatinumUnitId  = entry.PlatinumUnitId;
        input.PalladiumUnitId = entry.PalladiumUnitId;
    }

    /// <summary>Bir takoz GİRİŞ satırını (külçeyi) Id ile bulur (silinmemiş, Bullion+Inbound) —
    /// yalnız verilen şirketin fişlerinde arar (company scope; yabancı külçe YOKMUŞ gibi davranılır).</summary>
    public async Task<VoucherLine?> FindBullionEntryLineAsync(Guid entryLineId, Guid companyId)
    {
        return await _asyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(v => v.CompanyId == companyId)
                .SelectMany(v => v.Lines)
                .Where(l => l.Id == entryLineId
                         && l.Type == ProcessType.Bullion
                         && l.Direction == ProcessDirectionType.Inbound
                         && !l.IsDeleted));
    }

    /// <summary>Takoz stoğu satırlarının denormalize gösterim alanlarını (ayar evi adı + getiren cari) doldurur.</summary>
    private async Task ResolveBullionStockDisplayAsync(List<BullionStockItemDto> entries)
    {
        var assayIds = entries.Where(e => e.AssayOfficeId.HasValue)
                              .Select(e => e.AssayOfficeId!.Value).Distinct().ToList();
        if (assayIds.Count > 0)
        {
            var names = (await _asyncExecuter.ToListAsync(
                    (await _assayOfficeRepository.GetQueryableAsync())
                        .Where(a => assayIds.Contains(a.Id))
                        .Select(a => new { a.Id, a.Name })))
                .ToDictionary(x => x.Id, x => x.Name);
            foreach (var e in entries)
            {
                if (e.AssayOfficeId is { } aid && names.TryGetValue(aid, out var n))
                {
                    e.AssayOfficeName = n;
                }
            }
        }

        var subIds = entries.Where(e => e.SubAccountId.HasValue)
                            .Select(e => e.SubAccountId!.Value).Distinct().ToList();
        if (subIds.Count > 0)
        {
            var subs = (await _asyncExecuter.ToListAsync(
                    (await _subAccountRepository.GetQueryableAsync())
                        .Where(s => subIds.Contains(s.Id))
                        .Select(s => new { s.Id, s.Code, s.Name })))
                .ToDictionary(x => x.Id, x => $"{x.Code} — {x.Name}");
            foreach (var e in entries)
            {
                if (e.SubAccountId is { } sid && subs.TryGetValue(sid, out var disp))
                {
                    e.SubAccountDisplay = disp;
                }
            }
        }
    }
}
