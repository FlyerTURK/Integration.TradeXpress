using System;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Metals;

public class MetalVariantLookupTests : TradeXpressApplicationTestBase
{
    private readonly IMetalAppService _metalAppService;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;

    public MetalVariantLookupTests()
    {
        _metalAppService = GetRequiredService<IMetalAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Should_Get_Variant_Lookup_For_Company()
    {
        // Arrange
        // We will use the Tenant and Company that we just seeded.
        // FMS Tenant: 04EE976E-207F-F97D-846C-3A223EBF4167
        // FMS Company: 9BC09D32-377B-AA13-596D-3A223EBF4A5B
        var tenantId = new Guid("04EE976E-207F-F97D-846C-3A223EBF4167");
        var companyId = new Guid("9BC09D32-377B-AA13-596D-3A223EBF4A5B");

        using (_currentTenant.Change(tenantId))
        using (_currentCompany.Change(companyId))
        {
            // Act
            var list = await _metalAppService.GetVariantLookupAsync();

            // Assert
            list.ShouldNotBeEmpty();
        }
    }
}
