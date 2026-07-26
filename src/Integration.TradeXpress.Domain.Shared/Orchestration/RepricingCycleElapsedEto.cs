using System;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// 15-DK FİYAT DÖNGÜSÜ DOLDU olayı (ADR-PRODUCT-ORCHESTRATION durum tahtası sinyali #2 — Dilim 2).
/// <see cref="RepricingCycleWorker"/> periyodik yayımlar; <c>ProductOrchestrationManager</c> dinler →
/// kanal listelemesi OLAN ürünlere ürün-başına senkron job kuyruklar.
/// <para><b>Neden gerekli:</b> kanal fiyatı push ANINDA türetilir (NetCost×(1+Marj) — canlı kurla); kur/maliyet
/// değişince kanaldaki fiyat BAYATLAR ama hiçbir tetik onu tazelemiyordu (araştırma bulgusu: repoda kanal
/// repricing döngüsü YOKTU; tek 15-dk şey kur beslemesiydi). N11 dirty-check değişmeyen fiyatı zaten elemez —
/// yalnız SAPMIŞ fiyat gönderilir, döngü ucuz kalır.</para>
/// <para>Şirket-başına bir olay: handler o şirketin bağlamında koşar (per-company emtia + working-company stok).</para>
/// </summary>
[Serializable]
public class RepricingCycleElapsedEto
{
    public Guid? TenantId { get; set; }
    public Guid CompanyId { get; set; }
}
