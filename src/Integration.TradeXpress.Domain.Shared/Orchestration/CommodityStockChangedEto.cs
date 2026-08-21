using System;
using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// EMTİA STOĞU DEĞİŞTİ olayı (ADR-PRODUCT-ORCHESTRATION durum board'u sinyali #1). VoucherLine yazan/silen
/// her yol, UoW COMMIT SONRASI bunu yayımlar; <c>ProductOrchestrationManager</c> dinler → etkilenen ürünleri
/// (ters-endeks) yeniden hesaplar → kanala push.
/// <para><b>Neden commit sonrası:</b> push/hesap voucher transaction'ına bağlanırsa dış HTTP (N11 60 sn timeout)
/// voucher'ı kilitler; hata → rollback → stok hareketi kaybı. Kabul edilemez (ADR "senkronluk sözleşmesi").</para>
/// <para><b>Anahtar:</b> stok-taşıyan satırın ailesi + CommodityId + VariantId — ÖDEME TİPİNDEN BAĞIMSIZ
/// (Peşin/Rezervasyon ledger'a yazmaz ama stoğu değiştirir; ledger CommodityId taşımadığından tetik
/// ledger'a BAĞLANMAZ).</para>
/// <para><b>Aile anahtara 2026-08-06'da girdi</b> (eski ad <c>MetalStockChangedEto</c>): tek bir olay tipi
/// TÜM aileleri taşır. İkinci bir olay tipi açmak, iki handler + iki ters-endeksi sonsuza dek senkron tutmak
/// demekti — ilk sapma sessiz olurdu.</para>
/// <para>Taşıma ABP <c>IDistributedEventBus</c> — bugün local/in-memory, production'da RabbitMQ paketiyle
/// sıfır kod değişikliği (ADR kuyruk kararı).</para>
/// </summary>
[Serializable]
public class CommodityStockChangedEto
{
    public Guid? TenantId { get; set; }

    /// <summary>Stok kapsamı — emtia per-company (CLAUDE.md §6); handler bu şirket bağlamında koşar.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Etkilenen emtia(+varyant) kümesi: mutasyondan ÖNCEKİ ∪ SONRAKİ anahtarlar — satır silme /
    /// emtia değiştirme eski emtiayı kaçırmasın (araştırma bulgusu: sil+yeniden-yaz yalnız yeni hâli görür).</summary>
    public List<CommodityStockKeyEto> Keys { get; set; } = new();
}

/// <summary>Değişen emtia anahtarı. <c>CommodityVariantId</c> null = varyantsız satır (ana varyant stoğu).</summary>
[Serializable]
public class CommodityStockKeyEto
{
    /// <summary>Emtia ailesi (Metal / Good / ...). <c>CommodityId</c> FK'sız snapshot olduğundan aile OLMADAN
    /// anahtar tekil değildir — aynı Guid başka ailede başka emtiayı gösterebilir.</summary>
    public ProcessType Family { get; set; }

    public Guid CommodityId { get; set; }

    public Guid? CommodityVariantId { get; set; }
}
