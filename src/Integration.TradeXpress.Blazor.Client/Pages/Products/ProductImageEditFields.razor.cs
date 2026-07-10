using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Ürün görseli edit alanları — TEK-görsel çekirdeği (kaynak tipi / URL / dosya / önizleme)
/// paylaşılan <c>SingleImageEditFields</c>'te; burada yalnız ürün-özel sıra + varsayılan alanları ve
/// <c>IProductImageAppService</c> upload bağlaması kalır. Çağıran DxFormLayout'u sağlar.</summary>
public partial class ProductImageEditFields
{
    [Parameter, EditorRequired] public ProductImageGraphDto Model { get; set; } = default!;

    /// <summary>Dosya adı bu üründe zaten var mı — upload'dan ÖNCE kontrol edilir ki duplicate'a takılacak
    /// dosyanın blob'u hiç yazılmasın (yetim blob önlenir; SaveGuard zaten kaydı da engeller).</summary>
    [Parameter] public Func<string, bool>? IsDuplicateFileName { get; set; }

    [Inject] private IProductImageAppService ImageAppService { get; set; } = default!;

    /// <summary>Paylaşılan çekirdeğin upload delegesi — ürünün görsel servisiyle blob'a yazar.</summary>
    private async Task<SingleImageUploadResult> UploadCoreAsync(string fileName, byte[] content)
    {
        var result = await ImageAppService.UploadAsync(new ProductImageUploadDto
        {
            FileName = fileName,
            Content = content,
        });

        return new SingleImageUploadResult(result.BlobName, result.PreviewDataUrl);
    }
}
