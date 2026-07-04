using System;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Bir şirkete <b>kesin sahiplikle</b> bağlı kayıt işaretçisi — çok-şirket <b>GÜVENLİK SINIRI</b>
/// (kullanıcı onaylı tam izolasyon). Finansal çekirdek (Account, Voucher türevleri) burada yer alır:
/// kayıt DAİMA tek bir şirkete aittir, "tüm şirketlere açık" (holding-host) bir hâli YOKTUR.
///
/// <para><b><see cref="ICompanyScoped"/>'tan farkı — kasıtlı ve güvenlik-kritik:</b>
/// <see cref="ICompanyScoped"/> bir GÖRÜNÜM filtresidir; <c>CompanyId</c>'si <c>Guid?</c>'dir ve
/// <c>null</c> = holding-host = HERKESE görünür (katalog paylaşımı). Bu "null-görünür" kolu bir
/// güvenlik sınırında sızıntı olurdu. <see cref="ICompanyOwned"/>'da <c>CompanyId</c> non-nullable
/// <c>Guid</c>'dir: null-görünür kol YOKTUR, kayıt yalnız kendi şirketine görünür. Ayrıca finansal
/// entity'ler <c>CompanyId</c>'yi zaten non-nullable <c>Guid</c> tutar; onlara <c>Guid?</c> taşıyan
/// <see cref="ICompanyScoped"/>'u dayatmak EF query-filter'da tip uyuşmazlığı verirdi.</para>
///
/// <para>Filtre ABP <c>IDataFilter&lt;ICompanyScoped&gt;</c> anahtarını PAYLAŞIR (iki marker tek anahtar):
/// mevcut <c>DataFilter.Disable&lt;ICompanyScoped&gt;()</c> çağrıları her ikisini birden konsolide açar.
/// Konsolide mod (working şirket yok = <c>CurrentCompanyId == null</c>) PERMISSIVE'dir (filtre açılmaz —
/// DbMigrator/seeder/host/rapor kırılmasın); güvenlik, YAZMA yollarındaki fail-closed guard ile tamamlanır.</para>
/// </summary>
public interface ICompanyOwned
{
    Guid CompanyId { get; }
}
