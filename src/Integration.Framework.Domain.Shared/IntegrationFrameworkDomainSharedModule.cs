using Integration.Framework.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.Validation;
using Volo.Abp.Validation.Localization;
using Volo.Abp.VirtualFileSystem;

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
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<IntegrationFrameworkDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<IntegrationFrameworkResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/IntegrationFramework");
        });

        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("Integration.Framework", typeof(IntegrationFrameworkResource));
        });
    }
}
