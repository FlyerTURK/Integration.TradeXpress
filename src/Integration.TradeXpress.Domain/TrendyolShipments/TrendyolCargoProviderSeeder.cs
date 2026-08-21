using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>
/// Trendyol kargo firmalarını kurar — <b>HOST-GLOBAL</b> referans, yalnız host pass'inde.
///
/// <para><b>Neden seed, neden sync değil:</b> N11'de firma listesi canlı servisten gelir
/// (<c>GetShipmentCompanies</c>); Trendyol'da böyle bir uç YOKTUR. Resmî doküman listeyi statik tablo olarak
/// yayınlar (developers.trendyol.com — "Trendyol Kargo Şirketleri Listesi (getProviders)"), ki adı uç sanılmaya
/// açıktır: bizim endpoint envanterimizde de öyle sanılmıştı ve doğrulanmış URL taşımadığı için yakalanabildi.
/// Liste on satır ve seyrek değişiyor → kaynağa sadık seed en dürüst çözüm.</para>
///
/// <para><b>İdempotent + koruyucu:</b> mevcut satırın KODU/ADI/VERGİ NO'su tazelenir, satır SİLİNMEZ ve
/// <c>IsActive</c>'e DOKUNULMAZ — kullanıcı bir firmayı bilinçli pasifleştirmiş olabilir, seed onu geri açmamalı.
/// Listede olmayan satırlar da silinmez (geçmiş referanslar yaşasın).</para>
///
/// <para><b>Kaynak (2026-08-04 çekimi):</b> Id · Kod · Ad · Vergi No. Trendyol yeni firma yayınlarsa buraya
/// eklenir; uydurma satır GİRİLMEZ — <c>cargoCompanyId</c> yanlışsa ürün body'si Trendyol'da reddedilir.</para>
/// </summary>
public class TrendyolCargoProviderSeeder : IDataSeedContributor, ITransientDependency
{
    // Resmî tablo — (ExternalId, Code, Name, TaxNumber). SIRA = Trendyol'un yayınladığı id sırası değil,
    // okunabilirlik için id'ye göre artan.
    private static readonly (string ExternalId, string Code, string Name, string TaxNumber)[] Providers =
    {
        ("4",  "YKMP",          "Yurtiçi Kargo Marketplace",       "3130557669"),
        ("6",  "HOROZMP",       "Horoz Kargo Marketplace",         "4630097122"),
        ("7",  "ARASMP",        "Aras Kargo Marketplace",          "720039666"),
        ("9",  "SURATMP",       "Sürat Kargo Marketplace",         "7870233582"),
        ("10", "DHLECOMMP",     "DHL eCommerce Marketplace",       "6080712084"),
        ("17", "TEXMP",         "Trendyol Express Marketplace",    "8590921777"),
        ("19", "PTTMP",         "PTT Kargo Marketplace",           "7320068060"),
        ("20", "CEVAMP",        "CEVA Marketplace",                "8450298557"),
        ("30", "CEVATEDARIK",   "Ceva Tedarik Marketplace",        "1800038254"),
        ("38", "KOLAYGELSINMP", "Kolay Gelsin Marketplace",        "2910804196"),
    };

    private readonly IRepository<TrendyolCargoProvider, Guid> _repository;

    public TrendyolCargoProviderSeeder(IRepository<TrendyolCargoProvider, Guid> repository)
    {
        _repository = repository;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId is not null)
        {
            return;   // host-global veri → tenant pass'inde çalıştırma
        }

        var existing = (await _repository.GetListAsync())
            .ToDictionary(p => p.ExternalId, StringComparer.Ordinal);

        foreach (var (externalId, code, name, taxNumber) in Providers)
        {
            if (existing.TryGetValue(externalId, out var current))
            {
                await RefreshAsync(current, code, name, taxNumber);
                continue;
            }

            await _repository.InsertAsync(new TrendyolCargoProvider(externalId, code, name, taxNumber), autoSave: true);
        }
    }

    /// <summary>Mevcut satırı resmî listeyle hizalar — yalnız DEĞİŞMİŞSE yazar (gereksiz audit gürültüsü yok).
    /// <c>IsActive</c>'e dokunmaz: kullanıcının pasifleştirme kararı seed'den güçlüdür.</summary>
    private async Task RefreshAsync(TrendyolCargoProvider current, string code, string name, string? taxNumber)
    {
        var changed = !string.Equals(current.Code, code, StringComparison.Ordinal)
                      || !string.Equals(current.Name, name, StringComparison.Ordinal)
                      || !string.Equals(current.TaxNumber, taxNumber, StringComparison.Ordinal);
        if (!changed)
        {
            return;
        }

        current.SetCode(code);
        current.SetName(name);
        current.SetTaxNumber(taxNumber);
        await _repository.UpdateAsync(current, autoSave: true);
    }
}
