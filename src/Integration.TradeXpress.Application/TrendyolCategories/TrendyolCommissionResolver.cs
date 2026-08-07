using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.TrendyolProducts;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol komisyon oranını KATEGORİ AĞACINDAN çözer — <b>kalıtımlı</b> (2026-08-06 Hakan kararı:
/// <i>"Kategori ağacının en belirgin parentlerine bu genel oranları geç. Child kategoriler bu parentlerden
/// inherit yararlansın."</i>).
///
/// <para><b>Çözüm sırası:</b> yaprak kategori → üst → … → kök. İlk DOLU oran kazanır. Hiçbir seviyede oran
/// yoksa <see cref="TrendyolCommissionDefaults.PlaceholderRate"/> devreye girer — böylece komisyon HİÇBİR
/// ZAMAN sessizce sıfır olmaz. Önceden <c>resolvedCommissionRate</c> sabit <c>null</c> geçiliyordu ve
/// komisyon fiyata hiç girmiyordu; kimse fark etmiyordu.</para>
///
/// <para><b>Neden kalıtım:</b> Trendyol ağacı binlerce yaprak taşır ama oranlar ana gruplar düzeyinde
/// yayınlanır. Her yaprağa oran girmek bakımı imkânsız kılardı; üst düğüme bir kez girmek yeter.</para>
///
/// <para><b>Kategori HOST-GLOBAL</b> (tenant taşımaz) — okuma host bağlamına sabitlenir, tenant'lar aynı
/// ağacı paylaşır.</para>
///
/// <para><b>Döngü koruması:</b> bozuk/dairesel bir parent zinciri sonsuz döngü yapmasın diye yürüyüş hem
/// ziyaret edilen düğümleri hem sabit bir derinlik tavanını kontrol eder.</para>
///
/// <para>⚠ <b>AMBIENT UoW gerektirir:</b> <c>GetQueryableAsync</c> kendi UoW'unda DbContext üretir; o UoW kapanmışsa
/// <c>ToListAsync</c> DISPOSE EDİLMİŞ context'te koşar (<c>ObjectDisposedException</c>). Bugünkü çağıranlar
/// AppService metotlarıdır — ABP onları zaten UoW ile sarar. Bir background worker'dan çağrılacaksa çağıran
/// TAZE bir UoW açmalıdır (<c>ProductOrchestrationManager</c> emsali).</para>
///
/// <para><b>Ağaç bir kez okunur</b> (<see cref="_snapshot"/>): çözücü transient'tir, yani ömrü onu enjekte eden
/// AppService'in isteği kadardır. İçe aktarım 100+ ürünü tek istekte gezdiği için düğüm başına sorgu N+1 üretirdi;
/// tek okuma + bellekte yürüyüş bunu kapatır. Aynı istek içinde oran değişirse bayat kalır — pratikte oran yazımı
/// ayrı bir isteğe aittir.</para>
/// </summary>
public class TrendyolCommissionResolver : ITransientDependency
{
    /// <summary>Ağaç derinliği tavanı — Trendyol ağacı 4-5 seviye; 16 fazlasıyla yeterli ve bozuk zincirde
    /// yürüyüşü sonlandırır.</summary>
    private const int MaxDepth = 16;

    private readonly IRepository<TrendyolCategory, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    /// <summary>İstek-ömürlü ağaç anlık görüntüsü (ExternalId → düğüm). Bkz. sınıf notu.</summary>
    private Dictionary<string, CategoryNode>? _snapshot;

    public TrendyolCommissionResolver(
        IRepository<TrendyolCategory, Guid> repository,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository     = repository;
        _currentTenant  = currentTenant;
        _asyncExecuter  = asyncExecuter;
    }

    /// <summary>Verilen kategori için geçerli komisyon oranını (%) döner. Kategori bilinmiyorsa ya da ağaçta
    /// hiç oran tanımlı değilse varsayılan yer tutucu döner — <c>null</c> DÖNMEZ (sessiz sıfır olmaması için).</summary>
    public virtual async Task<decimal> ResolveAsync(string? categoryExternalId)
    {
        if (string.IsNullOrWhiteSpace(categoryExternalId))
        {
            return TrendyolCommissionDefaults.PlaceholderRate;
        }

        var byExternalId = await GetSnapshotAsync();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = categoryExternalId.Trim();

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!visited.Add(current) || !byExternalId.TryGetValue(current, out var node))
            {
                break;   // dairesel zincir ya da kayıp düğüm → varsayılana düş
            }

            if (node.CommissionRate is { } rate)
            {
                return rate;
            }

            if (string.IsNullOrWhiteSpace(node.ParentExternalId))
            {
                break;   // köke ulaşıldı, oran bulunamadı
            }

            current = node.ParentExternalId!;
        }

        return TrendyolCommissionDefaults.PlaceholderRate;
    }

    /// <summary>Ağacı bir kez okur ve istek boyunca saklar. Kategori tablosu HOST-GLOBAL → okuma host bağlamına
    /// sabitlenir; tenant bağlamında okumak boş küme döndürürdü.</summary>
    private async Task<Dictionary<string, CategoryNode>> GetSnapshotAsync()
    {
        if (_snapshot is not null)
        {
            return _snapshot;
        }

        using (_currentTenant.Change(null))
        {
            var nodes = await _asyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Select(c => new CategoryNode(c.ExternalId, c.ParentExternalId, c.CommissionRate)));

            // Aynı ExternalId'nin iki satırı olamaz (benzersiz) ama bozuk veride ToDictionary patlar → gruplayıp ilkini al.
            _snapshot = nodes
                .GroupBy(n => n.ExternalId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return _snapshot;
        }
    }

    /// <summary>Yürüyüş için gereken üç alan — tam entity çekmeye gerek yok.</summary>
    private sealed record CategoryNode(string ExternalId, string? ParentExternalId, decimal? CommissionRate);
}
