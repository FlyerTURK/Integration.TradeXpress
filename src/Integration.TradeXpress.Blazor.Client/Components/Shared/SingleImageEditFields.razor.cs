using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Paylaşılan upload sonucu — servis-özel result DTO'ları (Product/Metal) buna indirgenir.</summary>
public sealed record SingleImageUploadResult(string BlobName, string PreviewDataUrl);

/// <summary>
/// TEK görsel düzenleme çekirdeği (ProductImageEditFields'ten çıkarıldı — DRY, 2026-07-10): kaynak tipi
/// (URL / Dosya), URL kutusu YA DA dosya yükleme (DxFileInput → <see cref="UploadAsync"/> delegesiyle blob'a
/// ANINDA; entity save'i yalnız referansı persist eder) + önizleme. Entity-özel ek alanlar (sıra/varsayılan)
/// <see cref="AfterSourceContent"/> slot'uyla kaynak combo'sunun hemen ardına girer. Çağıran DxFormLayout'u sağlar.
/// </summary>
public partial class SingleImageEditFields
{
    [Parameter, EditorRequired] public ISingleImageEditModel Model { get; set; } = default!;

    /// <summary>Dosya içeriğini blob'a yükleyen delege (fileName, content) — Product/Metal kendi AppService'ini bağlar.</summary>
    [Parameter, EditorRequired] public Func<string, byte[], Task<SingleImageUploadResult>> UploadAsync { get; set; } = default!;

    /// <summary>Yükleme boyut sınırı (byte) — sunucu guard'ıyla aynı sabit verilmeli (ör. ProductConsts.MaxImageSizeBytes).</summary>
    [Parameter, EditorRequired] public int MaxImageSizeBytes { get; set; }

    /// <summary>Boyut aşımı lokalizasyon anahtarı (ör. "TradeXpress:Product:ImageTooLarge"; {MaxMb} elle doldurulur).</summary>
    [Parameter, EditorRequired] public string TooLargeErrorKey { get; set; } = default!;

    /// <summary>Dosya adı zaten kullanılıyor mu — upload'dan ÖNCE kontrol edilir ki duplicate'a takılacak
    /// dosyanın blob'u hiç yazılmasın (yetim blob önlenir). Tek-görselli kullanımda boş bırakılır.</summary>
    [Parameter] public Func<string, bool>? IsDuplicateFileName { get; set; }

    /// <summary>Duplicate dosya adı uyarı anahtarı — yalnız <see cref="IsDuplicateFileName"/> verilmişse kullanılır.</summary>
    [Parameter] public string? DuplicateErrorKey { get; set; }

    /// <summary>Kaynak combo'sunun hemen ardına giren entity-özel alanlar (ör. Product: sıra + varsayılan).</summary>
    [Parameter] public RenderFragment? AfterSourceContent { get; set; }

    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // DrillList cascade EditContext'i — dosya yükleme ValueExpression'sız → dirty ELLE bildirilir.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    protected override void OnParametersSet()
    {
        // Fail-fast (review bulgusu — Least-Astonishment): duplicate delegesi verilip uyarı anahtarı verilmezse
        // duplicate dosya SESSİZCE reddedilirdi (toast yok) — sözleşme hatası geliştiriciye ANINDA patlasın.
        if (IsDuplicateFileName is not null && string.IsNullOrEmpty(DuplicateErrorKey))
        {
            throw new InvalidOperationException(
                $"{nameof(SingleImageEditFields)}: {nameof(IsDuplicateFileName)} verildiyse {nameof(DuplicateErrorKey)} da verilmeli — " +
                "yoksa duplicate dosya kullanıcıya hiçbir geri bildirim gösterilmeden reddedilir.");
        }
    }

    /// <summary>Önizleme kaynağı — URL tipli doğrudan URL, yüklenmişte data-URL (upload sonucu ya da GetAsync doldurur).</summary>
    private string? PreviewSrc
    {
        get
        {
            return Model.SourceType == ProductImageSourceType.Url ? Model.Url : Model.PreviewDataUrl;
        }
    }

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

    // Dosya seçildi (DxFileInput upload akışı): içeriği oku → delege ile blob'a yükle → referans + önizleme modele.
    private async Task OnFilesUploadingAsync(FilesUploadingEventArgs args)
    {
        var file = args.Files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        // Duplicate dosya adı upload'dan ÖNCE reddedilir — blob boşa yazılıp yetim kalmasın.
        // DuplicateErrorKey OnParametersSet fail-fast'iyle garanti (delege varsa anahtar da var).
        if (IsDuplicateFileName?.Invoke(file.Name) == true)
        {
            UiService.ShowWarningToast(L[DuplicateErrorKey!].Value);
            return;
        }

        try
        {
            using var buffer = new MemoryStream();
            await file.OpenReadStream(MaxImageSizeBytes).CopyToAsync(buffer);

            var result = await UploadAsync(file.Name, buffer.ToArray());

            Model.BlobName = result.BlobName;
            Model.FileName = file.Name;
            Model.PreviewDataUrl = result.PreviewDataUrl;
            EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.BlobName)));
            StateHasChanged();
        }
        catch (IOException)
        {
            // OpenReadStream boyut aşımı → dostane sınır mesajı (sunucu guard'ıyla aynı kural; {MaxMb} elle doldurulur).
            var maxMb = (MaxImageSizeBytes / (1024 * 1024)).ToString();
            UiService.ShowErrorToast(L[TooLargeErrorKey].Value.Replace("{MaxMb}", maxMb));
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
