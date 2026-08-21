using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>Trendyol kargo firması (host-global referans).</summary>
public class TrendyolCargoProviderDto
{
    public Guid Id { get; set; }

    /// <summary>Trendyol kargo firması id'si — ürün body'sindeki <c>cargoCompanyId</c> bununla eşleşir.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Trendyol kısa kodu (ör. "ARASMP").</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? TaxNumber { get; set; }

    public bool IsActive { get; set; }

    public override string ToString()
    {
        return $"{Code} — {Name}";
    }
}

/// <summary>
/// Trendyol kargo firmaları — host-global, SALT OKUMA.
///
/// <para><b>Neden sync ucu yok:</b> N11'in aksine Trendyol'da kargo firmalarını döndüren bir HTTP ucu YOKTUR —
/// resmî doküman listeyi statik tablo olarak yayınlar. Liste <c>TrendyolCargoProviderSeeder</c> ile kurulur;
/// yenilenmesi kod değişikliğidir (yeni firma yayınlanırsa seeder'a eklenir).</para>
/// </summary>
public interface ITrendyolCargoProviderAppService : IApplicationService
{
    /// <summary>Kargo firmaları (host-global okuma; varsayılan yalnız aktifler).</summary>
    Task<List<TrendyolCargoProviderDto>> GetListAsync(bool includeInactive = false);

    /// <summary>Yeni kanala konacak VARSAYILAN firmanın kimliği; hiç aktif firma yoksa <c>null</c>.
    /// <para>Seçim kuralı sunucudadır (<c>TrendyolDefaultCargoProviderResolver</c>) — kanal oluşturma, edit
    /// formunun ilk açılışı ve sihirbaz aynı cevabı görsün diye. İstemcide "listenin ilkini seç" demek,
    /// kullanıcının gördüğü firma ile kaydın aldığı firmanın ayrışmasına açık kapı bırakırdı.</para></summary>
    Task<Guid?> GetDefaultIdAsync();
}
