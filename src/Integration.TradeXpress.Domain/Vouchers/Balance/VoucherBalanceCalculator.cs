using System.Collections.Generic;
using System.Linq;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Satır kümesinin net bakiye etkisini hesaplar. Tüm <see cref="IVoucherLineBalancePoster"/>'ları
/// DI ile toplar, her satırı kendi ProcessType poster'ına yönlendirir, üretilen
/// <see cref="BalanceEffect"/>'leri birim bazında toplar. Poster'ı olmayan tür sessizce atlanır.
/// </summary>
public class VoucherBalanceCalculator : ITransientDependency
{
    private readonly IReadOnlyDictionary<ProcessType, IVoucherLineBalancePoster> _posters;

    public VoucherBalanceCalculator(IEnumerable<IVoucherLineBalancePoster> posters)
    {
        // Aynı ProcessType için birden çok poster olursa ilki kazanır (tasarımda tekil beklenir).
        _posters = posters
            .GroupBy(p => p.ProcessType)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Tek satırın bakiye etkilerini üretir (poster yoksa boş).</summary>
    public IEnumerable<BalanceEffect> Post(VoucherLine line)
        => _posters.TryGetValue(line.Type, out var poster)
            ? poster.Post(line)
            : Enumerable.Empty<BalanceEffect>();

    /// <summary>Satır kümesinin birim bazında net bakiyesini (UnitId → toplam) döndürür.</summary>
    public IReadOnlyDictionary<System.Guid, decimal> Aggregate(IEnumerable<VoucherLine> lines)
    {
        var net = new Dictionary<System.Guid, decimal>();
        foreach (var line in lines)
        {
            foreach (var effect in Post(line))
            {
                net.TryGetValue(effect.UnitId, out var current);
                net[effect.UnitId] = current + effect.Amount;
            }
        }
        return net;
    }
}
