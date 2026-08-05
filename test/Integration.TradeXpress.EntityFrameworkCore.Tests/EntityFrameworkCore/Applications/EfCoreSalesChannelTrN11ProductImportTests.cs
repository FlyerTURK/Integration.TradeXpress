using Integration.TradeXpress.EntityFrameworkCore;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 mağaza içe aktarımının GERÇEK EF Core sağlayıcısıyla koşumu — unique index'ler, owned JSON
/// kolonları (Skus) ve soft-delete filtreleri yalnız burada gerçek davranışlarını gösterir.</summary>
public class EfCoreSalesChannelTrN11ProductImportTests : SalesChannelTrN11ProductImportTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
