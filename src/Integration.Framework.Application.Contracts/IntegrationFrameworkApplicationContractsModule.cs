using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Integration.Framework;

/// <summary>
/// Integration <b>Framework</b> modülü (Application.Contracts katmanı).
/// Liste sorgu DTO sözleşmesini (<c>ListRequestDto</c>) ve uygulayıcısını
/// (<c>ListQueryableExtensions</c>) + generic CRUD DTO arayüzlerini taşır.
/// Değer tipleri (SortField/FilterField/SearchNormalizer/ListQueryException)
/// Domain.Shared'da; bu modül onu transitive getirir.
///
/// <para>Yeni bir ABP projesine Framework'ü eklemek için ilgili modül
/// <c>[DependsOn(typeof(IntegrationFrameworkApplicationContractsModule))]</c> der;
/// statik altyapı (whitelist'li <c>ApplyListRequest</c>, fold araması) hazır gelir.</para>
/// </summary>
[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(IntegrationFrameworkDomainSharedModule)
)]
public class IntegrationFrameworkApplicationContractsModule : AbpModule
{
}
