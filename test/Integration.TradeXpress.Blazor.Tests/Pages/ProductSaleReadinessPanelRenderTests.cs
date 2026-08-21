using System;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Ürünün satışa hazırlık paneli (<see cref="ProductSaleReadinessPanel"/>) davranış sözleşmesi (2026-08-19).
///
/// <para>Kilitlenenler: (1) kaydedilmemiş üründe sunucuya HİÇ gidilmez; (2) kaydedilmiş ürün bir kez yüklenir ve
/// her ebeveyn render'ında yeniden çekilmez; (3) <b>kayıt sonrası</b> Id aynı kalsa da taze model bağlandığında
/// (ReloadToken) panel kendiliğinden tazelenir — aksi hâlde "fiyat girdim, kaydettim" sonrası sayaçlar bayat
/// kalıyordu (2026-08-19 gözden geçirme bulgusu); (4) kirli formda "Satışa Doğrula" çalışmaz, uyarı görünür;
/// (5) temiz formda doğrulama host'un yoluna gider ve ardından panel yeniden yüklenir.</para>
///
/// <para><b>bUnit sınırı:</b> DevExpress grid'i bUnit'te veri satırı çizmez (ProductCategoryPickerModeTests'teki
/// not); adım/issue satırlarındaki "Düzelt →" düğmesi bu yüzden burada SINANAMAZ. Sınanabilen kısım: özet şerit
/// (DxFormLayout), uyarı bandı ve araç çubuğu.</para>
/// </summary>
public class ProductSaleReadinessPanelRenderTests : BlazorComponentTestBase
{
    private readonly IProductAppService _productAppService;

    public ProductSaleReadinessPanelRenderTests()
    {
        _productAppService = AddSubstitute<IProductAppService>();
        _productAppService.GetSaleReadinessAsync(Arg.Any<Guid>())
            .Returns(call => Task.FromResult(ReadyDto(call.Arg<Guid>())));
    }

    [Fact]
    public void Unsaved_product_is_not_evaluated_on_the_server()
    {
        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, Guid.Empty));

        // Sunucuda değerlendirilecek kayıt yok → çağrı yok; panel yalnız "önce kaydet" der.
        _productAppService.DidNotReceive().GetSaleReadinessAsync(Arg.Any<Guid>());
        cut.Markup.ShouldContain("Product:SaleReadinessSaveFirst");
    }

    [Fact]
    public void Saved_product_is_loaded_once_and_rendered()
    {
        var productId = Guid.NewGuid();

        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, productId));

        _productAppService.Received(1).GetSaleReadinessAsync(productId);
        // Özet şeritteki stok kaynağı metni = yükleme gerçekten markup'a düştü (lokalizasyon sahtesi anahtarı döndürür).
        cut.Markup.ShouldContain("Enum:ProductStockPolicy:Calculated");

        // Aynı parametrelerle yeniden çizim → sunucuya İKİNCİ kez gidilmez (her ebeveyn render'ı bir istek olamaz).
        cut.Render(parameters => parameters.Add(p => p.ProductId, productId));
        _productAppService.Received(1).GetSaleReadinessAsync(productId);
    }

    /// <summary>Kayıt sonrası host TAZE model örneği bağlar ama Id değişmez; panel bunu tazeleme anahtarından
    /// anlar. Bu test kırılırsa kullanıcı her kayıttan sonra el ile "Yenile" basmak zorunda kalır.</summary>
    [Fact]
    public void Reloads_when_the_reload_token_changes_even_though_the_id_is_unchanged()
    {
        var productId = Guid.NewGuid();
        var firstModel = new object();
        var savedModel = new object();

        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, productId)
            .Add(p => p.ReloadToken, firstModel));
        _productAppService.Received(1).GetSaleReadinessAsync(productId);

        // Aynı anahtar → yeniden yükleme YOK.
        cut.Render(parameters => parameters
            .Add(p => p.ProductId, productId)
            .Add(p => p.ReloadToken, firstModel));
        _productAppService.Received(1).GetSaleReadinessAsync(productId);

        // Kayıt: yeni model örneği, aynı Id → yeniden yükleme VAR.
        cut.Render(parameters => parameters
            .Add(p => p.ProductId, productId)
            .Add(p => p.ReloadToken, savedModel));
        _productAppService.Received(2).GetSaleReadinessAsync(productId);
    }

    [Fact]
    public void Dirty_form_shows_the_save_first_warning()
    {
        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, Guid.NewGuid())
            .Add(p => p.IsDirty, true));

        cut.Markup.ShouldContain("Product:SaleReadinessSaveBeforeVerify");
    }

    [Fact]
    public void Clean_form_does_not_show_the_save_first_warning()
    {
        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, Guid.NewGuid())
            .Add(p => p.IsDirty, false));

        cut.Markup.ShouldNotContain("Product:SaleReadinessSaveBeforeVerify");
    }

    /// <summary>Kirli formda doğrulama ÇALIŞMAZ: kaydedilmemiş reçete/fiyat doğrulamaya girmez ve onay eski içeriğe
    /// verilmiş olurdu. Düğme pasiftir; pasif düğmeye gelen tıklama bile host'u çağırmaz (çift kilit).</summary>
    [Fact]
    public async Task Dirty_form_never_forwards_verification_to_the_host()
    {
        var verifyRequests = 0;
        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, Guid.NewGuid())
            .Add(p => p.IsDirty, true)
            .Add(p => p.OnVerifyRequested, EventCallback.Factory.Create(this, () => verifyRequests++)));

        var button = FindButton(cut, "Product:VerifyForSale");
        button.ShouldNotBeNull(Describe(cut));

        if (!button!.HasAttribute("disabled"))
        {
            // Düğme pasif çizilmediyse tıklama yine de CanVerifyNow guard'ından geçemez.
            await button.ClickAsync(new MouseEventArgs());
        }

        verifyRequests.ShouldBe(0);
    }

    [Fact]
    public async Task Clean_form_forwards_verification_to_the_host_and_reloads()
    {
        var productId = Guid.NewGuid();
        var verifyRequests = 0;
        var cut = Render<ProductSaleReadinessPanel>(parameters => parameters
            .Add(p => p.ProductId, productId)
            .Add(p => p.IsDirty, false)
            .Add(p => p.OnVerifyRequested, EventCallback.Factory.Create(this, () => verifyRequests++)));

        var button = FindButton(cut, "Product:VerifyForSale");
        button.ShouldNotBeNull(Describe(cut));
        await button!.ClickAsync(new MouseEventArgs());

        // Doğrulamanın kendisi host'ta (bir kez istenir), ardından panel sunucudan yeniden yüklenir.
        verifyRequests.ShouldBe(1);
        _productAppService.Received(2).GetSaleReadinessAsync(productId);
    }

    private static ProductSaleReadinessDto ReadyDto(Guid productId)
    {
        return new ProductSaleReadinessDto
        {
            ProductId = productId,
            ProductCode = "TEST",
            IsActive = true,
            StockPolicy = ProductStockPolicy.Calculated,
            ActiveVariantCount = 2,
            PricedVariantCount = 2,
            SellableVariantCount = 1,
            CanVerify = true,
        };
    }

    /// <summary>Düğmeyi METNİNDEN bulur (lokalizasyon sahtesi anahtarı aynen döndürür → metin = anahtar).</summary>
    private static AngleSharp.Dom.IElement? FindButton(IRenderedComponent<ProductSaleReadinessPanel> cut, string key)
    {
        return cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains(key));
    }

    private static string Describe(IRenderedComponent<ProductSaleReadinessPanel> cut)
    {
        return "Düğme bulunamadı. Bulunanlar: " + string.Join(" | ", cut.FindAll("button").Select(b => b.TextContent.Trim()));
    }
}
