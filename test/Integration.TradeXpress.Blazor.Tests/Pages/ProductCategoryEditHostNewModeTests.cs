using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.TrendyolCategories;
using NSubstitute;
using Shouldly;
using Volo.Abp.ObjectMapping;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Kategori edit host'unun YENİ KAYIT modu.
///
/// <para><b>Neden var (2026-08-03):</b> framework konvansiyonu "yeni kayıt" için <c>Guid.Empty</c> kullanır
/// (her edit host <c>CrudEditHost Id="@(Id ?? Guid.Empty)"</c> ile bağlanır). Bu host yalnız <c>null</c>
/// kontrol ediyor, <c>Guid.Empty</c>'yi KAYITLI id sanıp sunucuya gönderiyordu; sonuç:
/// <i>"There is no such an entity ... id: 00000000-0000-0000-0000-000000000000"</i> ve ürünün kendi formunda
/// kategori seçicisi açılamıyordu. Hata DERLEME ile yakalanamaz — yalnız o yol koşunca patlar.</para>
///
/// <para>Bu test iki hâli de kilitler: boş id ile sunucuya HİÇ gidilmemeli, dolu id ile GİDİLMELİ.</para>
/// </summary>
public class ProductCategoryEditHostNewModeTests : BlazorComponentTestBase
{
    private readonly IProductCategoryAppService _categoryService;

    public ProductCategoryEditHostNewModeTests()
    {
        _categoryService = AddSubstitute<IProductCategoryAppService>();
        AddSubstitute<IN11CategoryAppService>();
        AddSubstitute<ITrendyolCategoryAppService>();
        AddSubstitute<IEtsyTaxonomyAppService>();
        AddSubstitute<IObjectMapper>();
        // CrudEditHost sekme açıcıyı [Inject] ile ister (kaydet-ve-aç akışı); davranışı sınanmıyor.
        AddSubstitute<IMdiTabOpener>();

        _categoryService.GetParentOptionsAsync(Arg.Any<Guid?>())
            .Returns(Task.FromResult(new List<ProductCategoryListDto>()));
        _categoryService.GetChannelMappingsAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult(new List<ProductCategoryChannelMappingDto>()));
    }

    [Fact]
    public async Task Empty_id_is_treated_as_new_and_never_queries_channel_mappings()
    {
        // Popup bu host'u Guid.Empty ile açıyordu; sunucuya boş id gitmesi EntityNotFound demekti.
        Render<ProductCategoryEditHost>(parameters => parameters
            .Add(p => p.Id, Guid.Empty)
            .Add(p => p.IsPopupMode, true));

        await _categoryService.DidNotReceive().GetChannelMappingsAsync(Arg.Any<Guid>());

        // Üst kategori seçenekleri de boş id ile SORULMAMALI — null geçilmeli (yeni kayıt).
        await _categoryService.Received().GetParentOptionsAsync(Arg.Is<Guid?>(x => x == null));
    }

    [Fact]
    public async Task Null_id_is_treated_as_new_too()
    {
        Render<ProductCategoryEditHost>(parameters => parameters
            .Add(p => p.Id, (Guid?)null)
            .Add(p => p.IsPopupMode, true));

        await _categoryService.DidNotReceive().GetChannelMappingsAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Persisted_id_still_loads_its_channel_mappings()
    {
        // Karşı taraf: gerçek bir kayıt açıldığında eşleştirmeler YÜKLENMELİ — düzeltme "hep atla"ya kaymasın.
        var id = Guid.NewGuid();

        Render<ProductCategoryEditHost>(parameters => parameters
            .Add(p => p.Id, id)
            .Add(p => p.IsPopupMode, true));

        await _categoryService.Received().GetChannelMappingsAsync(id);
        await _categoryService.Received().GetParentOptionsAsync(Arg.Is<Guid?>(x => x == id));
    }
}
