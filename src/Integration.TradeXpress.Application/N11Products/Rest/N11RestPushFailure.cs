using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// SKU bazlı red gerekçelerini tek bir dostane hataya çevirir — ORTAK gövde: app service'in push/senkron
/// çözümü ile <see cref="N11StockWithdrawer"/> aynı sınıflandırmayı görmeli (iki kopya zamanla ayrışır,
/// fahiş-fiyat ayrımı birinde unutulurdu).
///
/// <para><b>FAHİŞ FİYAT BANDI ayrı ele alınır:</b> N11 ürün başına bir alt/üst fiyat bandı uygular ve aşan
/// isteği reddeder (resmî hata sözlüğü, makale 10433). Kuyumda bu doğrudan zarar demektir — altın sıçradığında
/// otomatik fiyat güncellemesi bandı aşıp reddedilir ve ürün ESKİ (düşük) fiyatta satışta KALIR. Genel "push
/// başarısız" mesajına gömülürse operasyon farkı göremez, o yüzden kendi kodu var.</para>
/// </summary>
public static class N11RestPushFailure
{
    public static BusinessException Build(IReadOnlyList<string> failures)
    {
        var joined = string.Join(" | ", failures);

        if (failures.Any(f => f.Contains("fahiş fiyat", StringComparison.OrdinalIgnoreCase)))
        {
            return new BusinessException("TradeXpress:N11:Rest:PriceOutOfBand").WithData("Reasons", joined);
        }

        return new BusinessException("TradeXpress:N11:Rest:PushRejected").WithData("Reasons", joined);
    }
}
