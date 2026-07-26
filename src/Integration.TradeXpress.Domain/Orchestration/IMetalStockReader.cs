using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Maden/varyant KULLANILABİLİR stoğu okuma soyutlaması (ADR: mock-first test stratejisi — 2026-07-25 Hakan:
/// "tüm sistemler önce mock data ile test edilsin").
/// <para>Gerçek implementasyon <c>IMetalReportAppService.GetStockAsync</c> sarmalayıcısıdır
/// (AvailableQuantity = Net − RezerveÇıkış; ICurrentCompany ZORUNLU — şirket bağlamsız BOŞ döner).
/// Test implementasyonu sahte sözlüktür — DB'siz, deterministik.</para>
/// <para><b>İşaret kuralı BURADA KURULMAZ:</b> giriş/çıkış yönü tek kaynaktan
/// (<c>ProcessDirectionTypeExtensions.IsInflow</c>) türetilmiş hazır net değer okunur — 2026-07-25'te ad-hoc
/// sorguda yön varsayımıyla işaretin ters okunması bu soyutlamanın varlık sebebini kanıtladı.</para>
/// </summary>
public interface IMetalStockReader
{
    /// <summary>(MetalId, MetalVariantId) → kullanılabilir GRAM. Varyantsız toplam için MetalVariantId=null
    /// anahtarı da döner. Negatif net mümkündür (fazla çıkış) — tüketici sıfıra kırpar.</summary>
    Task<IReadOnlyDictionary<(Guid MetalId, Guid? MetalVariantId), decimal>> GetAvailableAsync(
        IReadOnlyCollection<Guid> metalIds);
}
