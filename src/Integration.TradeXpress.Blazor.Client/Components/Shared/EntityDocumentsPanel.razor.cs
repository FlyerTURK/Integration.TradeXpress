using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Entity-agnostik doküman paneli (reusable DrillList) — herhangi bir entity kaydının blob dosya ekleri.
/// Sahip form bir DxTabPage içine koyar. Graf save sahip AppService'te (IEntityDocumentAppService.ReplaceForAsync).
/// İndirme sunucudan içerik çekip <c>download.js</c> ES modülüyle (DotNetStreamReference) tarayıcıya dosya verir.</summary>
public partial class EntityDocumentsPanel
{
    [Parameter, EditorRequired] public List<EntityDocumentEditDto> Documents { get; set; } = default!;

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    [Inject] private IEntityDocumentAppService DocumentAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    private DrillList<EntityDocumentEditDto>? _drill;
    private IJSObjectReference? _downloadModule;
    private bool _downloading;

    // Yeni doküman eklenince Sıra No otomatik artar (mevcutların max'ı + 1; boşsa 1).
    private int NextOrder()
    {
        return Documents.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Kaydetme engeli: dosya yüklenmemiş (BlobName boş) satır kabul edilmez (sunucu ReplaceFor da eler — savunma).
    private string? DocumentSaveGuard(EntityDocumentEditDto candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.BlobName) ? L["TradeXpress:Document:FileRequired"].Value : null;
    }

    // İndirme: yalnız kaydedilmiş dokümanlar (Id dolu). Sunucudan içerik → DotNetStreamReference ile tarayıcıya dosya.
    private async Task DownloadAsync(EntityDocumentEditDto doc)
    {
        if (doc.Id == Guid.Empty || _downloading)
        {
            return;
        }

        _downloading = true;
        try
        {
            var file = await DocumentAppService.DownloadAsync(doc.Id);
            var module = await GetDownloadModuleAsync();
            using var stream = new MemoryStream(file.Content);
            using var streamRef = new DotNetStreamReference(stream);
            await module.InvokeVoidAsync("downloadFileFromStream", file.FileName, file.ContentType, streamRef);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _downloading = false;
        }
    }

    private async Task<IJSObjectReference> GetDownloadModuleAsync()
    {
        return _downloadModule ??= await Js.InvokeAsync<IJSObjectReference>("import", "./js/download.js");
    }
}
