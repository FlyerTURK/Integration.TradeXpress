using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Integration.Framework;

/// <summary>
/// Integration <b>Framework</b> modülü (Application katmanı). Sunucu-tarafı CRUD
/// standardını taşır: <c>FrameworkCrudAppService</c> — ABP <c>CrudAppService</c>'i
/// bizim whitelist'li + fold'lu <c>ApplyListRequest</c> motoruyla birleştirir.
/// Yeni bir entity'nin AppService'i bunu miras alıp yalnız izinli alanları verir;
/// server-side list/filtre/sıralama/arama her seferinde sıfırdan yazılmaz.
/// </summary>
[DependsOn(
    typeof(IntegrationFrameworkApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class IntegrationFrameworkApplicationModule : AbpModule
{
}
