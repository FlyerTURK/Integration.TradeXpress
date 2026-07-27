using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Fiş satırının BELGE + NOT eklerini düzenleyen açılır pencere. Ekler agnostik altyapıda yaşar
/// (<c>EntityName="VoucherLine"</c> + satır Id'si) — fiş şemasına alan eklenmez.
/// <para>Kuyum operasyonundaki karşılığı: parçanın <b>seri numarası</b>, poşet/kamera kaydı, kargo ve sigorta
/// evrakı, teslim tutanağı. İhtilafta delil zinciri bu kayıtlardan kurulur.</para>
/// <para>Satır KAYDEDİLMİŞ olmalıdır (ek, satırın kimliğine bağlanır); çağıran bunu garanti eder.</para>
/// </summary>
public partial class VoucherLineAttachmentsDialog : CrudComponentBase
{
    /// <summary>Agnostik ek altyapısında fiş satırını temsil eden sahip adı.</summary>
    public const string VoucherLineEntityName = "VoucherLine";

    [Inject] private IEntityDocumentAppService DocumentAppService { get; set; } = default!;
    [Inject] private IEntityNoteAppService NoteAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>Kaydedildikten sonra çağrılır — çağıran toolbar rozetlerini (adet) tazeleyebilsin.</summary>
    [Parameter] public EventCallback OnSaved { get; set; }

    /// <summary>Pencerenin hangi ek türünü gösterdiği — her toolbar düğmesi kendi kipini açar.</summary>
    public enum AttachmentMode
    {
        Documents,
        Notes,
    }

    private bool Visible { get; set; }
    private string HeaderText { get; set; } = string.Empty;
    private Guid _lineId;
    private bool _saving;
    private AttachmentMode _mode = AttachmentMode.Documents;

    private List<EntityDocumentEditDto> _documents = new();
    private List<EntityNoteEditDto> _notes = new();

    /// <summary>Pencereyi İSTENEN KİPTE açar ve satırın mevcut eklerini yükler. <paramref name="title"/>
    /// başlıkta gösterilir (ör. emtia kodu + tutar) ki kullanıcı hangi satırda çalıştığını görsün.
    /// <para>Her iki set de yüklenir: kaydetme ikisini birden yazar (agnostik sözleşme delete-all+insert-new
    /// olduğundan, yalnız görüneni yazmak diğerini SİLERDİ).</para></summary>
    public async Task OpenAsync(Guid lineId, string title, AttachmentMode mode)
    {
        _lineId = lineId;
        _mode = mode;
        var kind = mode == AttachmentMode.Documents ? L["Documents"].Value : L["Notes"].Value;
        HeaderText = string.IsNullOrWhiteSpace(title) ? kind : $"{kind} — {title}";
        _documents = ToEditList(await DocumentAppService.GetForAsync(VoucherLineEntityName, lineId));
        _notes = ToEditList(await NoteAppService.GetForAsync(VoucherLineEntityName, lineId));
        Visible = true;
        StateHasChanged();
    }

    private async Task SaveAsync()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            // Her iki set de TÜMÜYLE değiştirilir (agnostik servis sözleşmesi: delete-all + insert-new).
            await DocumentAppService.ReplaceForAsync(VoucherLineEntityName, _lineId, _documents);
            await NoteAppService.ReplaceForAsync(VoucherLineEntityName, _lineId, _notes);
            UiService.ShowSuccessToast(L["SavedSuccessfully"]);
            Close();
            if (OnSaved.HasDelegate)
            {
                await OnSaved.InvokeAsync();
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Close()
    {
        Visible = false;
        StateHasChanged();
    }

    private static List<EntityDocumentEditDto> ToEditList(List<EntityDocumentDto> source)
    {
        var result = new List<EntityDocumentEditDto>(source.Count);
        foreach (var d in source)
        {
            result.Add(new EntityDocumentEditDto
            {
                Id = d.Id,
                FileName = d.FileName,
                BlobName = d.BlobName,
                ContentType = d.ContentType,
                Size = d.Size,
                Description = d.Description,
                DisplayOrder = d.DisplayOrder,
            });
        }

        return result;
    }

    private static List<EntityNoteEditDto> ToEditList(List<EntityNoteDto> source)
    {
        var result = new List<EntityNoteEditDto>(source.Count);
        foreach (var n in source)
        {
            result.Add(new EntityNoteEditDto
            {
                Id = n.Id,
                Text = n.Text,
                DisplayOrder = n.DisplayOrder,
            });
        }

        return result;
    }
}
