using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherLine ekle/güncelle/sil işlemlerinin GÖLGE günlüğü — "kayıt Application seviyesinde tutulsun"
/// (2026-07-15 kullanıcı isteği). Çekirdek posting/bakiye motoruna DOKUNMAZ; çağıranın AYNI UoW/transaction'ı
/// içinde, satır işleminden HEMEN SONRA (satırın Id'si kesinleştikten sonra) çağrılır.
/// </summary>
public class VoucherLineHistoryRecorder : ITransientDependency
{
    private readonly IRepository<VoucherLineHistory, Guid> _repository;
    private readonly VoucherCodeResolver _codeResolver;

    public VoucherLineHistoryRecorder(
        IRepository<VoucherLineHistory, Guid> repository,
        VoucherCodeResolver codeResolver)
    {
        _repository   = repository;
        _codeResolver = codeResolver;
    }

    /// <summary>Bir satırın o anki hâlini tarihçeye yazar. <paramref name="line"/> her zaman satırın GEÇERLİ/
    /// SON durumunu taşımalıdır (silmede: <c>RemoveLine</c> çağrısından ÖNCEKİ hâli — soft-delete olduğundan
    /// hâlâ okunabilir, ama anlamlı bir anlık görüntü için işaretlemeden ÖNCE snapshot alınmalı).</summary>
    public async Task RecordAsync(Voucher voucher, VoucherLine line, VoucherLineChangeType changeType)
    {
        var dto = VoucherLineDtoFactory.MapLine(line);

        // Fiş başlığı: MapLine yalnız satırı doldurur — popup/log gösterimi için başlık alanları da taşınır.
        dto.VoucherId          = voucher.Id;
        dto.VoucherNumber      = voucher.VoucherNumber;
        dto.CompanyId          = voucher.CompanyId;
        dto.BranchId           = voucher.BranchId;
        dto.VaultId            = voucher.VaultId;
        dto.AccountId          = voucher.AccountId;
        dto.SubAccountId       = voucher.SubAccountId;
        dto.VoucherDate        = voucher.VoucherDate;
        dto.VoucherDescription = voucher.Description;

        // Okuma-anı denormalize kodlar (MainUnitCode/PayUnitCode/CounterAccountCode) — popup'ta ham GUID görünmesin.
        var list = new List<VoucherLineDto> { dto };
        await _codeResolver.ResolveUnitCodesAsync(list);
        await _codeResolver.ResolveCounterAccountCodesAsync(list);

        var snapshotJson = VoucherLineHistorySerializer.Serialize(dto);

        var history = new VoucherLineHistory(
            line.Id,
            voucher.Id,
            voucher.CompanyId,
            changeType,
            voucher.VoucherNumber.ToString(),
            voucher.VoucherDate,
            line.Type,
            dto.ProcessCode,
            line.CommodityCode,
            line.Quantity,
            line.Amount,
            line.Total,
            dto.MainUnitCode,
            line.Description,
            voucher.SubAccountId,
            snapshotJson);

        await _repository.InsertAsync(history, autoSave: false);
    }
}
