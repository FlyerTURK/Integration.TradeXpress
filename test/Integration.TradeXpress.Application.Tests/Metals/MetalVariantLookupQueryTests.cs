using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
using Shouldly;
using Xunit.Abstractions;

namespace Integration.TradeXpress.Metals;

public class MetalVariantLookupQueryTests : TradeXpressApplicationTestBase
{
    private readonly ITestOutputHelper _output;

    public MetalVariantLookupQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CheckQueryTranslation()
    {
        var metalRepo = GetRequiredService<IRepository<Metal, Guid>>();
        var variantRepo = GetRequiredService<IRepository<EntityVariant, Guid>>();

        var tenantId = new Guid("04EE976E-207F-F97D-846C-3A223EBF4167");
        var companyId = new Guid("9BC09D32-377B-AA13-596D-3A223EBF4A5B");

        var metalPredicate = CompanyScopedQueryable.CompanyVisiblePredicate<Metal>(tenantId, companyId);
        var variantPredicate = CompanyScopedQueryable.CompanyVisiblePredicate<EntityVariant>(tenantId, companyId);

        var metalsQuery = await metalRepo.GetQueryableAsync();
        var variantsQuery = await variantRepo.GetQueryableAsync();

        var baseQuery = from metal in metalsQuery.Where(metalPredicate)
                        join variant in variantsQuery.Where(variantPredicate) on metal.Id equals variant.EntityId
                        where variant.EntityName == "Metal" && !variant.IsDeleted && !metal.IsDeleted
                        select new
                        {
                            CommodityId    = metal.Id,
                            VariantId      = variant.Id
                        };

        var sql = baseQuery.ToQueryString();
        _output.WriteLine("SQL QUERY:");
        _output.WriteLine(sql);

        // Run the query against test DB just to see if it throws
        var result = await baseQuery.ToListAsync();
        _output.WriteLine($"Result count: {result.Count}");
    }
}
