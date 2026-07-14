using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Entity-agnostik görsel edit alanları — TEK-görsel çekirdeği (kaynak/URL/dosya/önizleme) paylaşılan
/// <c>SingleImageEditFields</c>'te; burada yalnız sıra + varsayılan alanları ve <see cref="IEntityImageAppService"/>
/// upload bağlaması. Herhangi bir entity kaydının (Good, GoodVariant, Metal...) çok-görselli drill'i tüketir.</summary>
public partial class EntityImageEditFields
{
    [Parameter, EditorRequired] public EntityImageEditDto Model { get; set; } = default!;

    /// <summary>Dosya adı bu kayıtta zaten var mı — upload'dan ÖNCE kontrol (yetim blob önlenir).</summary>
    [Parameter] public Func<string, bool>? IsDuplicateFileName { get; set; }

    [Inject] private IEntityImageAppService ImageAppService { get; set; } = default!;

    /// <summary>Paylaşılan çekirdeğin upload delegesi — agnostik görsel servisiyle blob'a yazar.</summary>
    private async Task<SingleImageUploadResult> UploadCoreAsync(string fileName, byte[] content)
    {
        var result = await ImageAppService.UploadAsync(new EntityImageUploadDto
        {
            FileName = fileName,
            Content = content,
        });

        return new SingleImageUploadResult(result.BlobName, result.PreviewDataUrl ?? string.Empty);
    }
}
