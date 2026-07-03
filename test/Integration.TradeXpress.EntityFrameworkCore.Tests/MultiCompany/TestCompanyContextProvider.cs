using System;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Test working-company köprüsü — <see cref="NullCompanyContextProvider"/>'ın (daima null) yerine geçer.
/// Blazor'daki working-context'in test eşdeğeri: test, aktif şirketi <see cref="CompanyId"/> ile belirler;
/// <c>VoucherAppService.EnsureCurrentCompanyId</c> ve BalanceSheet company-scope zorlaması bu değeri görür.
/// Varsayılan null → "company context yok" senaryosu ek kurulum istemez.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ICompanyContextProvider), typeof(TestCompanyContextProvider))]
public class TestCompanyContextProvider : ICompanyContextProvider, ISingletonDependency
{
    /// <summary>Testin belirlediği aktif (working) şirket. Null = context yok.</summary>
    public Guid? CompanyId { get; set; }

    public Guid? GetCurrentCompanyId()
    {
        return CompanyId;
    }
}
