using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Entity-agnostik doküman edit alanları — dosyayı <see cref="IEntityDocumentAppService.UploadAsync"/> ile
/// blob'a ANINDA yükler (referans + MIME + boyut modele yazılır), açıklama/sıra düzenlenir. Herhangi bir entity
/// kaydının doküman drill'i tüketir (<see cref="EntityDocumentsPanel"/> içinde).</summary>
public partial class EntityDocumentEditFields
{
    [Parameter, EditorRequired] public EntityDocumentEditDto Model { get; set; } = default!;

    /// <summary>Dosya adı bu kayıtta zaten var mı — upload'dan ÖNCE kontrol (yetim blob önlenir).</summary>
    [Parameter] public Func<string, bool>? IsDuplicateFileName { get; set; }

    [Inject] private IEntityDocumentAppService DocumentAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // DrillList cascade EditContext'i — dosya yükleme ValueExpression'sız → dirty ELLE bildirilir.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    private string SizeText => EntityDocumentSize.Format(Model.Size);

    // Dosya seçildi (DxFileInput upload akışı): içeriği oku → blob'a yükle → referans + MIME + boyut modele.
    private async Task OnFilesUploadingAsync(FilesUploadingEventArgs args)
    {
        var file = args.Files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        // Duplicate dosya adı upload'dan ÖNCE reddedilir — blob boşa yazılıp yetim kalmasın.
        if (IsDuplicateFileName?.Invoke(file.Name) == true)
        {
            UiService.ShowWarningToast(L["TradeXpress:Document:Duplicate"].Value);
            return;
        }

        try
        {
            using var buffer = new MemoryStream();
            await file.OpenReadStream(EntityDocumentConsts.MaxDocumentSizeBytes).CopyToAsync(buffer);

            var result = await DocumentAppService.UploadAsync(new EntityDocumentUploadDto
            {
                FileName = file.Name,
                Content = buffer.ToArray(),
            });

            Model.FileName = file.Name;
            Model.BlobName = result.BlobName;
            Model.ContentType = result.ContentType;
            Model.Size = result.Size;
            EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.BlobName)));
            StateHasChanged();
        }
        catch (IOException)
        {
            // OpenReadStream boyut aşımı → dostane sınır mesajı (sunucu guard'ıyla aynı kural).
            var maxMb = (EntityDocumentConsts.MaxDocumentSizeBytes / (1024 * 1024)).ToString();
            UiService.ShowErrorToast(L["TradeXpress:Document:TooLarge"].Value.Replace("{MaxMb}", maxMb));
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
