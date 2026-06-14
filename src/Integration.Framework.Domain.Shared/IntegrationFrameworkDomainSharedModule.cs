using Volo.Abp.Modularity;
using Volo.Abp.Validation;

namespace Integration.Framework;

/// <summary>
/// Integration <b>Framework</b> modülü (Domain.Shared katmanı). En alttaki, bağımlılığı
/// en az katman: vendor-agnostik liste-sorgu değer tipleri (<c>SortField</c>,
/// <c>FilterField</c>, <c>ListFilterOperator</c>), aksan/harf katlayan <c>SearchNormalizer</c>,
/// <c>ListQueryException</c> ve <c>FrameworkErrorCodes</c> burada yaşar — her üst katman
/// (Contracts/Application/UI) ve her yeni proje bunları tekrar yazmadan kullanır.
/// </summary>
[DependsOn(
    typeof(AbpValidationModule)
)]
public class IntegrationFrameworkDomainSharedModule : AbpModule
{
}
