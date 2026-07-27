using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Bir fiş satırının BELGE + NOT eklerini panelde in-memory taşıyan taşıyıcı (composition — kalıtım YOK,
/// çünkü süreç panelleri tek bir ortak tabandan türemiyor: bir kısmı <c>ProcessPanelHostBase</c>,
/// bir kısmı doğrudan <c>IVoucherLineEditPanel</c>).
///
/// <para>Ekler agnostik altyapıda yaşar (<c>EntityName="VoucherLine"</c> + satır Id'si) — fiş şemasına alan
/// eklenmez. Kuyum karşılığı: parçanın SERİ NUMARASI, poşet/kamera kaydı, kargo ve sigorta evrakı, teslim
/// tutanağı; ihtilafta delil zinciri bu kayıtlardan kurulur.</para>
///
/// <para><b>Yaşam döngüsü:</b> düzenlemeye açılınca <see cref="LoadAsync"/> (yeni satırda boş başlar),
/// satır KAYDEDİLDİKTEN sonra <see cref="PersistAsync"/> — ek satırın kimliğine bağlandığı için önce
/// yazılamaz. Yeni satıra geçilirken <see cref="Reset"/>.</para>
/// </summary>
public sealed class VoucherLineAttachmentSet
{
    /// <summary>Agnostik ek altyapısında fiş satırını temsil eden sahip adı.</summary>
    public const string EntityName = "VoucherLine";

    private readonly IEntityDocumentAppService _documents;
    private readonly IEntityNoteAppService _notes;

    public VoucherLineAttachmentSet(IEntityDocumentAppService documents, IEntityNoteAppService notes)
    {
        _documents = documents;
        _notes = notes;
    }

    /// <summary>Satırın belge ekleri — panel doğrudan bu listeye bağlanır.</summary>
    public List<EntityDocumentEditDto> Documents { get; private set; } = new();

    /// <summary>Satırın not ekleri (seri numarası buraya girer).</summary>
    public List<EntityNoteEditDto> Notes { get; private set; } = new();

    /// <summary>Satırın eklerini yükler; yeni satırda (kimlik boş) boş listelerle başlar.</summary>
    public async Task LoadAsync(Guid lineId)
    {
        if (lineId == Guid.Empty)
        {
            Reset();
            return;
        }

        var documents = await _documents.GetForAsync(EntityName, lineId);
        Documents = documents.ConvertAll(d => new EntityDocumentEditDto
        {
            Id = d.Id,
            FileName = d.FileName,
            BlobName = d.BlobName,
            ContentType = d.ContentType,
            Size = d.Size,
            Description = d.Description,
            DisplayOrder = d.DisplayOrder,
        });

        var notes = await _notes.GetForAsync(EntityName, lineId);
        Notes = notes.ConvertAll(n => new EntityNoteEditDto
        {
            Id = n.Id,
            Text = n.Text,
            DisplayOrder = n.DisplayOrder,
        });
    }

    /// <summary>Ekleri KAYDEDİLMİŞ satıra yazar (agnostik sözleşme: delete-all + insert-new).
    /// Yazacak bir şey yoksa çağrı yapılmaz. Hata FIRLATIR — çağıran kullanıcıya bildirir; satır zaten
    /// kalıcı olduğundan geri alma YAPILMAZ (ekler tekrar denenebilir).</summary>
    public async Task PersistAsync(Guid lineId)
    {
        if (lineId == Guid.Empty || (Documents.Count == 0 && Notes.Count == 0))
        {
            return;
        }

        await _documents.ReplaceForAsync(EntityName, lineId, Documents);
        await _notes.ReplaceForAsync(EntityName, lineId, Notes);
    }

    /// <summary>Kaç kez sıfırlandığı — panel bunu izleyip ek grubunu yeniden KATLAR (yeni satıra geçilince
    /// grup açık kalmasın). Sayaç, "yeni satır başladı" olayının taşıyıcısıdır.</summary>
    public int ResetCount { get; private set; }

    /// <summary>Yeni satıra geçilirken önceki satırın ekleri taşınmasın diye temizler.</summary>
    public void Reset()
    {
        Documents = new List<EntityDocumentEditDto>();
        Notes = new List<EntityNoteEditDto>();
        ResetCount++;
    }
}
