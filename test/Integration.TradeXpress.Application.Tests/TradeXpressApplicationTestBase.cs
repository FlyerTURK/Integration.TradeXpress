using System;
using System.Threading.Tasks;
using Integration.TradeXpress.ProductCategories;
using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

public abstract class TradeXpressApplicationTestBase<TStartupModule> : TradeXpressTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    /// <summary>
    /// Ürün kuran testler için kategori açar. Ürün kategorisi ZORUNLU olduğundan (kanal kategorisi ve komisyon
    /// bu bağdan çözülür) kategorisiz <c>CreateAsync</c> iş hatası verir; testlerin konusu kategori olmadığından
    /// bu gürültü tek yere toplandı — her test dosyasında ayrı kurulum kopyalanmasın.
    ///
    /// <para>Çağıran şirket kapsamı (<c>ICurrentCompany.Change</c>) içinde olmalıdır: kategori company-owned'dır
    /// ve ürünle AYNI şirkete ait olmayan kategori sunucuda reddedilir.</para>
    /// </summary>
    protected async Task<Guid> CreateTestProductCategoryAsync(string name = "Test Kategori")
    {
        var categoryAppService = GetRequiredService<IProductCategoryAppService>();
        var category = await categoryAppService.CreateAsync(new ProductCategoryCreateDto { Name = name });
        return category.Id;
    }
}
