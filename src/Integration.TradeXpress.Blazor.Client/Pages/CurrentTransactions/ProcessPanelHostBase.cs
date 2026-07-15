using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// GetDto-direct fiş satırı panellerinin (Cash/Metal/Scrap/Future/Convert/Service) ortak gövdesi:
/// ortak parametre seti (Context + OnBack/OnSaved), HandleSave iskeleti (bağlamı modele kopyala →
/// SaveLineAsync → toast → düzeltmede geri dön, yeni eklemede alan sıfırla) ve LoadForEditAsync deseni.
/// Markup türeyen .razor'dadır; panel-özel davranış abstract/virtual kancalarla sağlanır.
/// (CommodityProcessPanelBase'ten farkı: markup içermez — 6 panelin markup'ları birbirinden farklıdır.)
/// </summary>
public abstract class ProcessPanelHostBase : ComponentBase, IVoucherLineEditPanel
{
    [Inject] protected IStringLocalizer<TradeXpressResource> L { get; set; } = default!;
    [Inject] protected IVoucherAppService VoucherService { get; set; } = default!;
    [Inject] protected IUiInteractionService Ui { get; set; } = default!;

    /// <summary>Kaydın normal fiş yoluna mı Teyit yoluna mı gideceğinin TEK karar noktası (SSOT).</summary>
    [Inject] protected VoucherLinePersister Persister { get; set; } = default!;

    /// <summary>Fiş bağlamı — AccountSelectionPanel'den tek nesne olarak gelir.</summary>
    [Parameter] public VoucherLineContext Context { get; set; } = new();

    [Parameter] public EventCallback OnBack { get; set; }

    /// <summary>Satır kaydedilince (VoucherId döner → sonraki satırlar aynı fişe).</summary>
    [Parameter] public EventCallback<VoucherLineDto> OnSaved { get; set; }

    /// <summary>Teyit yoluna gidildiğinde (teklif kuruldu ya da beyan edildi) tetiklenir — fiş OLUŞMADIĞI için
    /// <see cref="OnSaved"/> tetiklenmez. Gelen kutusu bunu dinleyip popup'ı kapatır/listeyi tazeler.</summary>
    [Parameter] public EventCallback<VoucherLinePersistOutcome> OnConfirmationSubmitted { get; set; }

    /// <summary>Aktif fiş: bağlamdan gelir; kayıt sonrası sunucudan dönen değere güncellenir.</summary>
    protected Guid? VoucherId { get; set; }

    private VoucherLineDto? _model;

    /// <summary>Kaydetme sürüyor mu — re-entrancy bayrağı (çift tıklama/Enter çift-gönderim koruması).</summary>
    private bool _saving;

    /// <summary>Markup'ta Kaydet butonunu disable etmek için: <c>SaveEnabled="@(!IsSaving)"</c>.</summary>
    protected bool IsSaving => _saving;

    /// <summary>Edit modeli (GetDto-direct): tüm edit alanları doğrudan bunun üzerinde.</summary>
    protected VoucherLineDto Model
    {
        get => _model ??= CreateModel();
        set => _model = value;
    }

    // ── Türeyen panelin sağladıkları ──

    /// <summary>Satırın işlem tipi (Cash/Metal/...). Save'de modele yazılır.</summary>
    protected abstract ProcessType ProcessType { get; }

    /// <summary>Yeni (boş) edit modeli — panelin varsayılanlarıyla.</summary>
    protected abstract VoucherLineDto CreateModel();

    /// <summary>Save öncesi geçerlilik: false → sessizce çık (mevcut panel davranışı).</summary>
    protected virtual bool CanSave() => true;

    /// <summary>Panel-özel türetilen alanları modele yazar (Factor/Total/MarketPrice vb.).
    /// Ortak fiş bağlamı + Type base tarafından ZATEN kopyalanmıştır.</summary>
    protected abstract void PrepareModelForSave();

    /// <summary>Yeni ekleme sonrası bir sonraki satır için sıfırlanacak alanlar
    /// (tutarlar/açıklama; sınıflandırma ve seçimler kalır).</summary>
    protected abstract void ResetVolatileFields();

    /// <summary>Kayıt başarıyla döndükten sonra, OnSaved bildirilmeden ÖNCE çalışan kanca
    /// (ör. Cash/Metal: EditedField sıfırlama).</summary>
    protected virtual void OnAfterSavePersisted()
    {
    }

    /// <summary>Yeni ekleme akışında, alanlar sıfırlandıktan SONRA çalışan async kanca
    /// (ör. Convert: sonraki satır auto-fill'i için bakiyeleri tazeler).</summary>
    protected virtual Task OnAfterResetAsync() => Task.CompletedTask;

    /// <summary>Düzelt akışında model yüklendikten sonra panel-özel lookup/kilit senkronu.</summary>
    protected virtual Task OnLoadedForEditAsync(VoucherLineDto dto) => Task.CompletedTask;

    /// <summary>Kalıcılaştırma seam'i — kararı <see cref="VoucherLinePersister"/> verir (TEK yerde, tüm paneller
    /// için): dış cari → normal fiş satırı kaydı (bugünkü davranış, birebir aynı) · iç kasa → Teyit teklifi ·
    /// beyan kipi → alıcının kendi satırı. Teyit yollarında <b>fiş oluşmaz</b> → <c>Line</c> null gelir ve
    /// çağıran fiş/grid durumuna DOKUNMAZ.</summary>
    protected virtual async Task<VoucherLinePersistResult> PersistAsync()
    {
        return await Persister.PersistAsync(new VoucherLinePersistRequest(
            Model,
            Context.CounterpartyVaultId,
            Context.VaultId,
            Context.DeclareConfirmationId));
    }

    protected override void OnParametersSet()
    {
        // Eski parametre davranışıyla parite: parent her render'da VoucherId'yi bağlamdan tazeler.
        VoucherId = Context.VoucherId;
    }

    /// <summary>Ortak kaydetme iskeleti: bağlamı modele kopyala → SaveLineAsync → toast →
    /// düzeltmede panel kapanır, yeni eklemede uçucu alanlar sıfırlanır.</summary>
    protected async Task HandleSave()
    {
        if (_saving)
            return; // kaydetme zaten sürüyor — çift tıklama/çift Enter yut (re-entrancy koruması)

        if (!CanSave())
            return; // panel-özel ön koşul sağlanmadı (ör. emtia/birim seçili değil)

        _saving = true;
        StateHasChanged(); // Kaydet butonu ilk await'te disabled çizilsin
        try
        {
            await HandleSaveCoreAsync();
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>HandleSave'in asıl gövdesi — <see cref="HandleSave"/> re-entrancy guard'ı ile sarar.</summary>
    private async Task HandleSaveCoreAsync()
    {
        // Fiş bağlamı + işlem tipi.
        Model.VoucherId          = VoucherId;
        Model.CompanyId          = Context.CompanyId;
        Model.BranchId           = Context.BranchId;
        Model.VaultId            = Context.VaultId;
        Model.AccountId          = Context.AccountId;
        Model.SubAccountId       = Context.SubAccountId;
        Model.VoucherDate        = Context.VoucherDate;
        Model.VoucherDescription = Context.VoucherDescription;
        Model.Type               = ProcessType;

        PrepareModelForSave();

        var wasEdit = Model.Id != Guid.Empty;   // güncelleme mi, yeni ekleme mi?

        VoucherLinePersistResult persisted;
        try
        {
            persisted = await PersistAsync();
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(L["Voucher_LineSaveFailed", ex.Message].Value);
            return;
        }

        if (persisted.Line is not { } result)
        {
            // Fiş oluşmadı (Teyit teklifi/beyanı kuruldu — postlama iki tarafın onayına ertelendi, ya da ön koşul
            // sağlanmadı): fiş/grid durumu ELLENMEZ. Toast'ı VoucherLinePersister verir (kayıt değil TEKLİF/BEYAN
            // olduğu net söylensin) — kural nerede, bildirimi orada.
            ResetVolatileFields();
            await OnAfterResetAsync();
            if (persisted.Outcome != VoucherLinePersistOutcome.Blocked)
            {
                await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
            }
            return;
        }

        VoucherId       = result.VoucherId;
        Model.VoucherId = result.VoucherId;
        Model.Id        = Guid.Empty;
        OnAfterSavePersisted();
        await OnSaved.InvokeAsync(result);
        Ui.ShowSuccessToast(wasEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);

        if (wasEdit)
        {
            // Güncelleme bittiyse panel kapanır (yeni mod gibi açık kalmaz).
            await OnBack.InvokeAsync();
            return;
        }

        // Yeni ekleme: bir sonraki satır için uçucu alanları sıfırla.
        ResetVolatileFields();
        await OnAfterResetAsync();
    }

    /// <summary>Düzeltme: GetDto'yu doğrudan model olarak alır (recompute YOK — saklı değerler gösterilir).</summary>
    public async Task LoadForEditAsync(VoucherLineDto dto)
    {
        Model     = dto;
        VoucherId = dto.VoucherId;
        await OnLoadedForEditAsync(dto);
        StateHasChanged();
    }
}
