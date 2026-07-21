using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Ürün görseli edit alanları — TEK-görsel çekirdeği (kaynak tipi / URL / dosya / önizleme)
/// paylaşılan <c>SingleImageEditFields</c>'te; burada yalnız ürün-özel sıra + varsayılan + VARYANT bağlama
/// alanları ve <c>IProductImageAppService</c> upload bağlaması kalır. Çağıran DxFormLayout'u sağlar.</summary>
public partial class ProductImageEditFields
{
    [Parameter, EditorRequired] public ProductImageGraphDto Model { get; set; } = default!;

    /// <summary>Ürün kodu — upload'da blob path ön-ekini (Products/{Kod}/…) üretmek için servise iletilir.</summary>
    [Parameter, EditorRequired] public string ProductCode { get; set; } = string.Empty;

    /// <summary>Ürünün varyantları — görseli bir varyanta bağlama combo'sunun kaynağı (boş seçim = ürün-geneli).</summary>
    [Parameter] public IReadOnlyList<ProductVariantGraphDto> Variants { get; set; } = Array.Empty<ProductVariantGraphDto>();

    [Inject] private IProductImageAppService ImageAppService { get; set; } = default!;

    /// <summary>Combo'da seçili varyant — VaryantKodu üzerinden çözülür (yeni üründe DB Id'si henüz Guid.Empty olabilir;
    /// kod ürün içinde tekil + kalıcı anahtar). null = ürün-geneli.</summary>
    private ProductVariantGraphDto? SelectedVariant
    {
        get
        {
            return string.IsNullOrEmpty(Model.VariantCode)
                ? null
                : Variants.FirstOrDefault(v => v.Code == Model.VariantCode);
        }
    }

    /// <summary>Varyant seçimi değişti — Id + Kod (denormalize) birlikte doldurulur; temizlenirse ürün-geneli.</summary>
    private void OnVariantChanged(ProductVariantGraphDto? variant)
    {
        Model.VariantId = variant?.Id;
        Model.VariantCode = variant?.Code;
    }

    /// <summary>Paylaşılan çekirdeğin upload delegesi — ürünün görsel servisiyle blob'a yazar (ürün kodu + varyant
    /// kodu path ön-ekini üretir).</summary>
    private async Task<SingleImageUploadResult> UploadCoreAsync(string fileName, byte[] content)
    {
        var result = await ImageAppService.UploadAsync(new ProductImageUploadDto
        {
            FileName = fileName,
            Content = content,
            ProductCode = ProductCode,
            VariantCode = Model.VariantCode,
        });

        return new SingleImageUploadResult(result.BlobName, result.PreviewDataUrl);
    }
}
