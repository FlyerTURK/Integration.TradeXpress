using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Attachments;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Pages.Media;

/// <summary>Şirket-kapsamlı medya kütüphanesi (DAM) yönetim sayfası — code-behind. Sol klasör ağacı (Tümü/Klasörsüz/hiyerarşi)
/// + sağ medya gridi. Yükle/URL-import (seçili klasöre, self-contained blob), poster galeri, video→istemci-kare-yakalama→kapak,
/// klasöre taşı, klasör CRUD (silme medyayı SİLMEZ, üste taşır), sil. Servis: IMediaAppService.</summary>
public partial class MediaLibraryPage
{
    private List<MediaDto> _items = new();
    private string? _filter;
    private bool _busy;

    private bool _importPopupVisible;
    private string? _importUrl;

    private bool _capturePopupVisible;
    private MediaDto? _captureMedia;
    private readonly string _captureVideoId = "media_capture_" + Guid.NewGuid().ToString("N");

    // ── Klasörler ──
    private List<MediaFolderDto> _folders = new();
    private List<FolderNode> _folderNodes = new();
    private string _selectedNodeKey = AllNodeKey;
    private FolderNodeKind _selectedKind = FolderNodeKind.All;
    private Guid? _selectedFolderId;   // yalnız Kind==Folder iken dolu

    private bool _folderPopupVisible;
    private string? _folderName;
    private Guid? _editingFolderId;    // null = yeni klasör; dolu = yeniden adlandır

    private bool _movePopupVisible;
    private Guid _moveMediaId;
    private Guid? _moveTargetFolderId;

    private const string AllNodeKey = "__all__";
    private const string UnfiledNodeKey = "__none__";

    // İzinli uzantılar (görsel + video) — sunucu whitelist'iyle hizalı.
    private readonly List<string> _allowedExtensions = new()
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp4", ".webm", ".ogg", ".ogv", ".mov", ".m4v" };

    private bool CanEditSelectedFolder
    {
        get { return _selectedKind == FolderNodeKind.Folder; }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadFoldersAsync();
        await ReloadAsync();
    }

    private async Task LoadFoldersAsync()
    {
        _folders = (await MediaService.GetFoldersAsync()).ToList();
        RebuildFolderNodes();
    }

    private void RebuildFolderNodes()
    {
        var nodes = new List<FolderNode>
        {
            new() { Key = AllNodeKey, ParentKey = null, Name = L["MediaFolderAll"].Value, Kind = FolderNodeKind.All },
            new() { Key = UnfiledNodeKey, ParentKey = null, Name = L["MediaFolderUnfiled"].Value, Kind = FolderNodeKind.Unfiled },
        };
        foreach (var f in _folders)
        {
            nodes.Add(new FolderNode
            {
                Key = f.Id.ToString(),
                ParentKey = f.ParentId?.ToString(),   // null → kök (Tümü/Klasörsüz ile aynı seviye)
                Name = f.Name,
                FolderId = f.Id,
                Kind = FolderNodeKind.Folder,
            });
        }

        _folderNodes = nodes;
    }

    private async Task OnFolderNodeClick(TreeViewNodeClickEventArgs e)
    {
        if (e.NodeInfo.DataItem is not FolderNode node)
        {
            return;
        }

        _selectedNodeKey = node.Key;
        _selectedKind = node.Kind;
        _selectedFolderId = node.FolderId;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var result = await MediaService.GetLibraryAsync(new MediaListRequestDto
        {
            Filter = _filter,
            FilterByFolder = _selectedKind != FolderNodeKind.All,
            FolderId = _selectedKind == FolderNodeKind.Folder ? _selectedFolderId : null,
            MaxResultCount = 500,
        });
        _items = result.Items.ToList();
        StateHasChanged();
    }

    private async Task OnFilterChangedAsync(string? value)
    {
        _filter = value;
        await ReloadAsync();
    }

    private async Task OnFilesUploadingAsync(FilesUploadingEventArgs args)
    {
        var target = CanEditSelectedFolder ? _selectedFolderId : null;
        var uploaded = 0;
        foreach (var file in args.Files)
        {
            try
            {
                using var buffer = new MemoryStream();
                await file.OpenReadStream(MediaConsts.MaxVideoSizeBytes).CopyToAsync(buffer);
                await MediaService.UploadAsync(new MediaUploadDto { FileName = file.Name, Content = buffer.ToArray(), FolderId = target });
                uploaded++;
            }
            catch (Exception ex)
            {
                Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
            }
        }

        if (uploaded > 0)
        {
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
            await ReloadAsync();
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
            var target = CanEditSelectedFolder ? _selectedFolderId : null;
            await MediaService.ImportFromUrlAsync(new MediaImportUrlDto { Url = _importUrl.Trim(), FolderId = target });
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
            _importUrl = null;
            _importPopupVisible = false;
            await ReloadAsync();
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

    // ── Klasör CRUD ──

    private void OpenCreateFolder()
    {
        _editingFolderId = null;
        _folderName = null;
        _folderPopupVisible = true;
    }

    private void OpenRenameFolder()
    {
        if (!CanEditSelectedFolder || _selectedFolderId is null)
        {
            return;
        }

        _editingFolderId = _selectedFolderId;
        _folderName = _folders.FirstOrDefault(f => f.Id == _selectedFolderId)?.Name;
        _folderPopupVisible = true;
    }

    private async Task SaveFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(_folderName))
        {
            return;
        }

        _busy = true;
        try
        {
            if (_editingFolderId is null)
            {
                // Yeni klasör: seçili gerçek klasörün altına (aksi halde köke).
                var parent = CanEditSelectedFolder ? _selectedFolderId : null;
                await MediaService.CreateFolderAsync(new CreateMediaFolderDto { Name = _folderName.Trim(), ParentId = parent });
            }
            else
            {
                // Yeniden adlandır (üst klasör korunur).
                var current = _folders.FirstOrDefault(f => f.Id == _editingFolderId.Value);
                await MediaService.UpdateFolderAsync(_editingFolderId.Value, new UpdateMediaFolderDto { Name = _folderName.Trim(), ParentId = current?.ParentId });
            }

            _folderPopupVisible = false;
            await LoadFoldersAsync();
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
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

    private async Task DeleteFolderAsync()
    {
        if (!CanEditSelectedFolder || _selectedFolderId is null)
        {
            return;
        }

        if (await Ui.ConfirmDeleteAsync(L["MediaFolderDeleteConfirm"].Value) != ConfirmDialogResult.Yes)
        {
            return;
        }

        try
        {
            await MediaService.DeleteFolderAsync(_selectedFolderId.Value);
            _selectedNodeKey = AllNodeKey;   // seçim köke düşsün
            _selectedKind = FolderNodeKind.All;
            _selectedFolderId = null;
            await LoadFoldersAsync();
            await ReloadAsync();
            Ui.ShowSuccessToast(L["SuccessfullyDeleted"].Value);
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // ── Medyayı klasöre taşı ──

    private void OpenMove(MediaDto media)
    {
        _moveMediaId = media.Id;
        _moveTargetFolderId = media.FolderId;
        _movePopupVisible = true;
    }

    private async Task MoveAsync()
    {
        _busy = true;
        try
        {
            await MediaService.MoveToFolderAsync(new MoveMediaToFolderDto
            {
                MediaIds = new List<Guid> { _moveMediaId },
                FolderId = _moveTargetFolderId,
            });
            _movePopupVisible = false;
            await ReloadAsync();
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
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

    // ── Video kapak (istemci-yakalama) ──

    private void OpenCapture(MediaDto media)
    {
        _captureMedia = media;
        _capturePopupVisible = true;
    }

    private async Task CaptureFrameAsync()
    {
        if (_captureMedia is null)
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

            await MediaService.SetPosterAsync(new SetMediaPosterDto
            {
                MediaId = _captureMedia.Id,
                PosterContent = Convert.FromBase64String(frame.Base64),
                Width = frame.Width,
                Height = frame.Height,
                DurationSeconds = frame.Duration,
            });
            Ui.ShowSuccessToast(L["SuccessfullySaved"].Value);
            _capturePopupVisible = false;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            // GEÇİCİ TEŞHİS — poster concurrency fix'i doğrulanınca friendly mesaja döndürülecek.
            Ui.ShowErrorToast($"[{ex.GetType().Name}] {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteAsync(MediaDto media)
    {
        if (await Ui.ConfirmDeleteAsync(L["DeleteConfirmationMessage"].Value) != ConfirmDialogResult.Yes)
        {
            return;
        }

        try
        {
            await MediaService.DeleteAsync(media.Id);
            Ui.ShowSuccessToast(L["SuccessfullyDeleted"].Value);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024d):N1} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:N0} KB";
        }

        return $"{bytes} B";
    }

    // erpUx.captureVideoFrame dönüşü (base64 JPEG + boyut/süre).
    private sealed record CaptureResult
    {
        public string Base64 { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public double? Duration { get; init; }
    }

    // Klasör ağacı düğümü (flat Key/ParentKey → DxTreeView hiyerarşi). Synthetic Tümü/Klasörsüz + gerçek klasörler.
    private sealed record FolderNode
    {
        public string Key { get; init; } = string.Empty;
        public string? ParentKey { get; init; }
        public string Name { get; init; } = string.Empty;
        public Guid? FolderId { get; init; }
        public FolderNodeKind Kind { get; init; }
    }

    private enum FolderNodeKind
    {
        All,
        Unfiled,
        Folder,
    }
}
