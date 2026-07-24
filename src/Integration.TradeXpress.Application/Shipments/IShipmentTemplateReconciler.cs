using System;
using System.Threading.Tasks;
using Integration.Framework.Addressing;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Kanal (N11/Etsy/Trendyol) kargo şablonundan <b>ters</b> çekirdek <see cref="ShipmentTemplate"/> üretir/bağlar —
/// K1 köprüsünün REVERSE ayağı. Kanal şablonu kaydedilince, henüz bir çekirdeğe bağlı DEĞİLSE, çalışılan şirket
/// kapsamında <c>Code == NormalizeCode(templateName)</c> olan çekirdeği BULUR ya da kısmî/taslak bir çekirdek
/// OLUŞTURUR ve id'sini döner. <b>Idempotent</b>: aynı ad ikinci kez yeni çekirdek yaratmaz (Code eşleşmesi bulur).
/// Kanal-nötr (SRP): yalnız (şirket, şablon adı, depo adresi) alır — çağıran kanal servisi dönen id'yi kendi
/// entity'sine bağlar; kanal servisi çekirdek deposunu doğrudan tutmaz.
/// </summary>
public interface IShipmentTemplateReconciler
{
    /// <summary>Çalışılan şirket (<paramref name="companyId"/>) kapsamında <paramref name="templateName"/>'in normalize
    /// koduna sahip çekirdek şablonu bulur; yoksa <paramref name="warehouseAddress"/>'i gönderim (ÖZEL) adresi yapıp
    /// (1,1) hazırlık günüyle TASLAK çekirdek oluşturur. Bulunan/oluşturulan çekirdeğin <c>Id</c>'sini döner.</summary>
    Task<Guid> FindOrCreateFromChannelAsync(Guid companyId, string templateName, Address warehouseAddress);
}
