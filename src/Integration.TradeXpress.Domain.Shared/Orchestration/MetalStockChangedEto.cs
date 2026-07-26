using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// MADEN STOĞU DEĞİŞTİ olayı (ADR-PRODUCT-ORCHESTRATION durum tahtası sinyali #1). VoucherLine yazan/silen
/// her yol, UoW COMMIT SONRASI bunu yayımlar; <c>ProductOrchestrationManager</c> dinler → etkilenen ürünleri
/// (ters-endeks) yeniden hesaplar → kanala push.
/// <para><b>Neden commit sonrası:</b> push/hesap voucher transaction'ına bağlanırsa dış HTTP (N11 60 sn timeout)
/// voucher'ı kilitler; hata → rollback → stok hareketi kaybı. Kabul edilemez (ADR "senkronluk sözleşmesi").</para>
/// <para><b>Anahtar:</b> Type==Metal satırın CommodityId+VariantId'si — ÖDEME TİPİNDEN BAĞIMSIZ (Peşin/Rezervasyon
/// ledger'a yazmaz ama stoğu değiştirir; ledger CommodityId taşımadığından tetik ledger'a BAĞLANMAZ).</para>
/// <para>Taşıma ABP <c>IDistributedEventBus</c> — bugün local/in-memory, production'da RabbitMQ paketiyle
/// sıfır kod değişikliği (ADR kuyruk kararı).</para>
/// </summary>
[Serializable]
public class MetalStockChangedEto
{
    public Guid? TenantId { get; set; }

    /// <summary>Stok kapsamı — emtia per-company (CLAUDE.md §6); handler bu şirket bağlamında koşar.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Etkilenen maden(+varyant) kümesi: mutasyondan ÖNCEKİ ∪ SONRAKİ anahtarlar — satır silme /
    /// maden değiştirme eski madeni kaçırmasın (araştırma bulgusu: sil+yeniden-yaz yalnız yeni hâli görür).</summary>
    public List<MetalStockKeyEto> Keys { get; set; } = new();
}

/// <summary>Değişen maden anahtarı. <c>MetalVariantId</c> null = varyantsız satır (ana varyant stoğu).</summary>
[Serializable]
public class MetalStockKeyEto
{
    public Guid MetalId { get; set; }
    public Guid? MetalVariantId { get; set; }
}
