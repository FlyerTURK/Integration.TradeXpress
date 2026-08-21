using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// EMTİA STOK TETİĞİNİN TEK KAYNAĞI — fiş yazan her yol, commit SONRASI
/// <see cref="CommodityStockChangedEto"/> kuyruklar.
///
/// <para><b>Neden tek yer:</b> aynı kod üç sınıfa birebir kopyalanmıştı (fiş app service'i, teyit
/// materializer'ı, rezervasyon materializer'ı — biri yorumunda kopya olduğunu itiraf ediyordu) ve DÖRDÜNCÜSÜ
/// eksikti: rezervasyonun serbest bırakılması hiç tetik yayımlamıyordu. Kopya sayısı arttıkça "hangi yol
/// tetikliyor?" sorusu kaynağa bakmadan cevaplanamaz hâle geliyordu; eksik olan da tam bu yüzden yıllarca
/// görünmedi — hata üretmiyor, yalnız kanal stoğu bir sonraki tam turu bekliyor.</para>
///
/// <para><b>COMMIT SONRASI publish</b> (transaction İÇİNDE değil): handler kanala HTTP push tetikler
/// (N11 60 sn timeout) — fiş dış servise kilitlenemez. Rollback'te olay YAYIMLANMAZ; stok değişmediyse tetik
/// de doğmamalıdır.</para>
///
/// <para><b>Ödeme tipinden BAĞIMSIZ:</b> Peşin/Rezervasyon ledger'a yazmaz ama stoğu değiştirir; stok raporu
/// da yalnız <c>Type</c>'a bakar. Virman ikizleri emtia bacağı taşımaz → kapsam dışı kalır (bilinçli).</para>
/// </summary>
public class CommodityStockChangeQueuer : ITransientDependency
{
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public CommodityStockChangeQueuer(
        IDistributedEventBus distributedEventBus,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant)
    {
        _distributedEventBus = distributedEventBus;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
    }

    /// <summary>Fişin CANLI (silinmemiş) stok-taşıyan satırlarının anahtarları.
    /// <para>Kapsam <see cref="CommodityStockFamilies.Tracked"/>. <b>Aile anahtarın parçasıdır</b> —
    /// <c>CommodityId</c> FK'sız bir snapshot'tır ve aynı Guid farklı ailede çakışabilir.</para></summary>
    public static List<CommodityStockKeyEto> CollectKeys(Voucher voucher)
    {
        return voucher.Lines
            .Where(l => !l.IsDeleted && CommodityStockFamilies.IsTracked(l.Type) && l.CommodityId != null)
            .Select(l => new CommodityStockKeyEto
            {
                Family             = l.Type,
                CommodityId        = l.CommodityId!.Value,
                CommodityVariantId = l.VariantId,
            })
            .GroupBy(k => (k.Family, k.CommodityId, k.CommodityVariantId))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Fişin etkilediği emtiaları commit SONRASINA kuyruklar.</summary>
    /// <param name="beforeKeys">Değişiklikten ÖNCEKİ anahtarlar. <b>Şart olduğu yer:</b> satır SİLİNDİĞİNDE ya da
    /// başka bir emtiaya TAŞINDIĞINDA, eski emtia artık fişte görünmez — yalnız sonraki duruma bakan bir tetik
    /// onu ATLAR ve eski emtianın kanal stoğu sonsuza kadar bayat kalırdı.</param>
    public void QueueForVoucher(Voucher voucher, IReadOnlyList<CommodityStockKeyEto>? beforeKeys = null)
    {
        var keys = (beforeKeys ?? Array.Empty<CommodityStockKeyEto>())
            .Concat(CollectKeys(voucher))
            .GroupBy(k => (k.Family, k.CommodityId, k.CommodityVariantId))
            .Select(g => g.First())
            .ToList();

        if (keys.Count == 0)
        {
            return;
        }

        var eto = new CommodityStockChangedEto
        {
            TenantId  = _currentTenant.Id,
            CompanyId = voucher.CompanyId,
            Keys      = keys,
        };

        var uow = _unitOfWorkManager.Current;
        if (uow is null)
        {
            // [UnitOfWork]'lü yollarda ambient DAİMA vardır; savunma amaçlı doğrudan yayım.
            _ = _distributedEventBus.PublishAsync(eto);
            return;
        }

        uow.OnCompleted(async () => await _distributedEventBus.PublishAsync(eto));
    }
}
