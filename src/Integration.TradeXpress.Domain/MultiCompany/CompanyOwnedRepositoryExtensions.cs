using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// <see cref="ICompanyOwned"/> kayıtlara TEKİL erişimin güvenli yolu — <b>derinlemesine savunma</b>.
///
/// <para><b>Neden var:</b> tekil <c>GetAsync(id)</c> çağrıları güvenliği tamamen global sorgu filtresine
/// yaslıyordu ("yabancı şirketinki zaten gizli → EntityNotFound"). Bu doğruydu ama <b>tek bir koşula</b>
/// bağlıydı: <c>ICurrentCompany.Id</c> dolu olmalı. HTTP API'de o bağlam hiç kurulmadığından değer
/// <c>null</c> kalıyor ve filtre PERMISSIVE (konsolide) kola düşüyordu — yani koruma sessizce yok oluyordu.
/// Tek koşullu savunma, o koşul kaybolduğunda hiçbir hata üretmez; en tehlikeli sessiz açık türü budur.</para>
///
/// <para>Bu yardımcı ikinci bağımsız guard'ı koyar: şirket bağlamı YOKSA açık <c>BusinessException</c>
/// (filtreye düşmek yerine anında dur), VARSA sorgu <c>CompanyId</c> eşitliğini AÇIKÇA taşır. Filtre ile
/// birlikte çalışır, onun yerine geçmez.</para>
/// </summary>
public static class CompanyOwnedRepositoryExtensions
{
    /// <summary>Kaydı YALNIZ çalışılan şirkete aitse getirir; değilse <see cref="EntityNotFoundException"/>
    /// (var olmayan kayıtla aynı cevap — yabancı kaydın VARLIĞI da sızmaz). Şirket bağlamı yoksa
    /// <c>TradeXpress:MultiCompany:WorkingCompanyRequired</c>.</summary>
    public static async Task<TEntity> GetOwnedAsync<TEntity>(
        this IRepository<TEntity, Guid> repository, ICurrentCompany currentCompany, Guid id)
        where TEntity : class, IEntity<Guid>, ICompanyOwned
    {
        var companyId = CompanyOwnershipGuard.ResolveOwnerCompanyId(currentCompany);

        var entity = await repository.FindAsync(e => e.Id == id && e.CompanyId == companyId);
        if (entity is null)
        {
            throw new EntityNotFoundException(typeof(TEntity), id);
        }

        return entity;
    }
}
