using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Entity MEDYA paneli — kayıt'ın merkezi kütüphane medyalarına LİNK setini (in-memory) yönetir. Yükle/URL-import →
/// self-contained blob (dedup) → link; poster/▶; varsayılan/aktif (varsayılan pasif olamaz); sırala; sil; video kapak.
/// Sahip AppService save'de EntityMediaAppService.ReplaceFor ile persist eder.</summary>
public partial class EntityMediaPanel
{
    [Parameter, EditorRequired] public List<EntityMediaLinkEditDto> Media { get; set; } = default!;

    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private bool _busy;
    private bool _importPopupVisible;
    private string? _importUrl;

    private bool _capturePopupVisible;
    private EntityMediaLinkEditDto? _captureLink;
    private readonly string _captureVideoId = "entity_media_capture_" + Guid.NewGuid().ToString("N");

    private readonly List<string> _allowedExtensions = new()
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp4", ".webm", ".ogg", ".ogv", ".mov", ".m4v" };

    private bool _pickerVisible;
    private List<MediaDto> _libraryItems = new();

    private async Task OnFilesUploadingAsync(FilesUploadingEventArgs args)
    {
        foreach (var file in args.Files)
        {
            try
            {
                using var buffer = new MemoryStream();
                await file.OpenReadStream(MediaConsts.MaxVideoSizeBytes).CopyToAsync(buffer);
                var dto = await MediaService.UploadAsync(new MediaUploadDto { FileName = file.Name, Content = buffer.ToArray() });
                AddLink(dto);
            }
            catch (Exception ex)
            {
                Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
            }
        }
    }

    private async Task ImportUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(_importUrl))
        {
            return;
        }

        _busy = true;
        try
        {
            var dto = await MediaService.ImportFromUrlAsync(new MediaImportUrlDto { Url = _importUrl.Trim() });
            AddLink(dto);
            _importUrl = null;
            _importPopupVisible = false;
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OpenPickerAsync()
    {
        var result = await MediaService.GetLibraryAsync(new MediaListRequestDto { MaxResultCount = 500 });
        _libraryItems = result.Items.ToList();
        _pickerVisible = true;
    }

    private void Pick(MediaDto media)
    {
        AddLink(media);
        _pickerVisible = false;
    }

    private void AddLink(MediaDto dto)
    {
        if (Media.Any(l => l.MediaId == dto.Id))
        {
            return;   // aynı medya zaten linkli (dedup)
        }

        Media.Add(new EntityMediaLinkEditDto
        {
            MediaId = dto.Id,
            Media = dto,
            IsActive = true,
            IsDefault = Media.Count == 0,
            DisplayOrder = Media.Count,
        });
        ReindexOrder();
        NotifyChanged();
    }

    private void OnDefaultChanged(EntityMediaLinkEditDto link, bool value)
    {
        if (value)
        {
            foreach (var other in Media)
            {
                other.IsDefault = false;
            }

            link.IsDefault = true;
            link.IsActive = true;   // varsayılan pasif olamaz
        }
        else
        {
            link.IsDefault = false;
        }

        NotifyChanged();
    }

    private void OnActiveChanged(EntityMediaLinkEditDto link, bool value)
    {
        link.IsActive = value;
        if (!value)
        {
            link.IsDefault = false;   // pasif medya varsayılan olamaz
        }

        NotifyChanged();
    }

    private void Move(EntityMediaLinkEditDto link, int direction)
    {
        var i = Media.IndexOf(link);
        var j = i + direction;
        if (i < 0 || j < 0 || j >= Media.Count)
        {
            return;
        }

        (Media[i], Media[j]) = (Media[j], Media[i]);
        ReindexOrder();
        NotifyChanged();
    }

    private void Remove(EntityMediaLinkEditDto link)
    {
        Media.Remove(link);
        ReindexOrder();
        if (Media.Count > 0 && !Media.Any(l => l.IsDefault))
        {
            Media[0].IsDefault = true;
            Media[0].IsActive = true;
        }

        NotifyChanged();
    }

    private void OpenCapture(EntityMediaLinkEditDto link)
    {
        _captureLink = link;
        _capturePopupVisible = true;
    }

    private async Task CaptureFrameAsync()
    {
        if (_captureLink?.Media is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var frame = await JS.InvokeAsync<CaptureResult?>("erpUx.captureVideoFrame", _captureVideoId);
            if (frame is null || string.IsNullOrEmpty(frame.Base64))
            {
                Ui.ShowWarningToast(L["MediaCaptureFailed"].Value);
                return;
            }

            var updated = await MediaService.SetPosterAsync(new SetMediaPosterDto
            {
                MediaId = _captureLink.MediaId,
                PosterContent = Convert.FromBase64String(frame.Base64),
                Width = frame.Width,
                Height = frame.Height,
                DurationSeconds = frame.Duration,
            });
            _captureLink.Media = updated;   // yeni poster (sunucuda kayıtlı)
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
            _capturePopupVisible = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ReindexOrder()
    {
        for (var i = 0; i < Media.Count; i++)
        {
            Media[i].DisplayOrder = i;
        }
    }

    private bool IsFirst(EntityMediaLinkEditDto link)
    {
        return Media.IndexOf(link) <= 0;
    }

    private bool IsLast(EntityMediaLinkEditDto link)
    {
        return Media.IndexOf(link) >= Media.Count - 1;
    }

    private void NotifyChanged()
    {
        EditChanged?.Invoke();
        StateHasChanged();
    }

    private sealed record CaptureResult
    {
        public string Base64 { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public double? Duration { get; init; }
    }
}
