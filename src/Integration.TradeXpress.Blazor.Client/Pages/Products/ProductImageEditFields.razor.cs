using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Ürün görseli edit alanları — kaynak tipi (URL / Dosya), URL kutusu YA DA dosya yükleme (DxFileInput →
/// blob'a ANINDA yüklenir, ürün save'i referansı persist eder) + önizleme + sıra. Çağıran DxFormLayout'u sağlar.</summary>
public partial class ProductImageEditFields
{
    [Parameter, EditorRequired] public ProductImageGraphDto Model { get; set; } = default!;

    [Inject] private IProductImageAppService ImageAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // DrillList cascade EditContext'i — dosya yükleme ValueExpression'sız → dirty ELLE bildirilir.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    /// <summary>Önizleme kaynağı — URL tipli doğrudan URL, yüklenmişte data-URL (upload sonucu ya da GetAsync doldurur).</summary>
    private string? PreviewSrc =>
        Model.SourceType == ProductImageSourceType.Url ? Model.Url : Model.PreviewDataUrl;

    // Kaynak tipi değişti: KARŞI kaynağın alanları temizlenir — bayat Url/BlobName entity JSON'ına persist olmasın
    // (review bulgusu). Dirty ValueExpression'la otomatik.
    private void OnSourceTypeChanged(ProductImageSourceType sourceType)
    {
        Model.SourceType = sourceType;
        if (sourceType == ProductImageSourceType.Url)
        {
            Model.BlobName = null;
            Model.FileName = null;
            Model.PreviewDataUrl = null;
        }
        else
        {
            Model.Url = null;
        }
    }

    // Dosya seçildi (DxFileInput upload akışı): içeriği oku → blob'a yükle → referans + önizleme modele.
    private async Task OnFilesUploadingAsync(FilesUploadingEventArgs args)
    {
        var file = args.Files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            using var buffer = new MemoryStream();
            await file.OpenReadStream(ProductConsts.MaxImageSizeBytes).CopyToAsync(buffer);

            var result = await ImageAppService.UploadAsync(new ProductImageUploadDto
            {
                FileName = file.Name,
                Content = buffer.ToArray(),
            });

            Model.BlobName = result.BlobName;
            Model.FileName = file.Name;
            Model.PreviewDataUrl = result.PreviewDataUrl;
            EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.BlobName)));
            StateHasChanged();
        }
        catch (IOException)
        {
            // OpenReadStream boyut aşımı → dostane sınır mesajı (sunucu guard'ıyla aynı kural; {MaxMb} elle doldurulur).
            var maxMb = (ProductConsts.MaxImageSizeBytes / (1024 * 1024)).ToString();
            UiService.ShowErrorToast(L["TradeXpress:Product:ImageTooLarge"].Value.Replace("{MaxMb}", maxMb));
        }
        catch (OperationCanceledException)
        {
            // kullanıcı yüklemeyi iptal etti → sessiz
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }
}
