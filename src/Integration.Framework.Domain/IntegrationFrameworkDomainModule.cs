using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Integration.Framework;

/// <summary>
/// Integration <b>Framework</b> modülü (Domain katmanı). Yeniden-kullanılabilir DOMAIN yapı taşları — value
/// object'ler (ör. <see cref="Integration.Framework.Addressing.Address"/>), ileride base entity'ler — burada
/// yaşar; TradeXpress + yeni projeler bunları tekrar yazmadan devralır. Domain.Shared ile Application arasındaki
/// (önceden eksik olan) Domain katmanını tamamlar.
/// </summary>
[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(IntegrationFrameworkDomainSharedModule)
)]
public class IntegrationFrameworkDomainModule : AbpModule
{
}
