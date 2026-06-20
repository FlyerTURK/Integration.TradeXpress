using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Volo.Abp.Application.Services;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Yeni Mimari: Sadece kendi verisini çeken, düzenleyen ve kaydeden
/// bağımsız (standalone) Edit formları için temel sınıf.
/// Popup (Modal) veya MDI Sekmesi (Tab) içinde çalışabilir.
/// </summary>
public abstract class CrudEditComponentBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> : CrudComponentBase
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : class, new()
    where TCreateDto : class, new()
    where TUpdateDto : class, new()
{
    [Parameter]
    public TKey? Id { get; set; }

    [Parameter]
    public bool IsPopupMode { get; set; }

    [Parameter]
    public EventCallback OnSaved { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    [CascadingParameter(Name = "CurrentMdiTab")]
    public IMdiTab? CurrentMdiTab { get; set; }

    [Inject]
    protected ITradeXpressUiService UiService { get; set; } = default!;

    [Inject]
    protected IEntityChangeNotifier EntityChanges { get; set; } = default!;

    [Inject]
    protected ILogger<CrudEditComponentBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto>> Logger { get; set; } = default!;

    [Inject]
    protected IPopupService PopupService { get; set; } = default!;

    protected abstract ICrudAppService<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> CrudAppService { get; }
    protected abstract string EntityChangeKey { get; }

    /// <summary>
    /// UI formunun doğrudan bağlandığı (bind edildiği) DTO.
    /// Yeni kayıt ise new(), düzenleme ise GetAsync ile dolar.
    /// </summary>
    protected TGetDto EditModel { get; set; } = new();

    protected bool IsNewMode => Id == null || Id.Equals(default(TKey));
    protected bool IsBusy { get; private set; }

    // ── Inline validation (alan-altı hata) altyapısı ──
    // EditContext, EditModel'e bağlanır; ValidateInput ihlalleri ValidationMessageStore'a yazar.
    // Sayfalar formu <CascadingValue Value="EditContext"> ile sarar → DevExpress editörleri mesajı
    // alanın altında gösterir. EditModel referansı değişince (yükleme/yeni) yeniden kurulur.
    protected EditContext? EditContext { get; private set; }
    private ValidationMessageStore? _messages;

    private void RebuildEditContext()
    {
        EditContext = new EditContext(EditModel!);
        _messages = new ValidationMessageStore(EditContext);
    }

    // ── Dirty takibi ──
    // EditModel yüklendiğinde/kaydedildiğinde JSON anlık görüntüsü alınır; o andan beri değişti mi
    // diye karşılaştırılır. Serileştirme başarısızsa fail-open (IsDirty=true) → kaydetme engellenmez.
    private string? _cleanSnapshot;

    private void CaptureSnapshot()
    {
        try { _cleanSnapshot = System.Text.Json.JsonSerializer.Serialize(EditModel); }
        catch { _cleanSnapshot = null; }
    }

    /// <summary>EditModel son yükleme/kayıttan beri değişti mi? (kaydet butonu vb. için)</summary>
    public bool IsDirty
    {
        get
        {
            if (_cleanSnapshot == null) return true;
            try { return System.Text.Json.JsonSerializer.Serialize(EditModel) != _cleanSnapshot; }
            catch { return true; }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        RebuildEditContext();   // ilk render güvenli (boş model)
        await LoadDataAsync();
        // Kapatma guard'ı (XAF DetailView davranışı): kaydedilmemiş değişiklik varsa onay sor.
        // Popup → PopupService.CloseGuard; MDI sekme → IMdiTab.CanCloseAsync. İkisi de ConfirmCloseAsync.
        if (IsPopupMode && PopupService != null)
        {
            PopupService.CloseGuard = ConfirmCloseAsync;
        }
        else if (CurrentMdiTab != null)
        {
            CurrentMdiTab.CanCloseAsync = ConfirmCloseAsync;
        }
        await base.OnInitializedAsync();
    }

    /// <summary>Popup kapatma onayı: dirty değilse true; dirty ise kullanıcıya sorar.</summary>
    protected virtual async Task<bool> ConfirmCloseAsync()
    {
        if (!IsDirty) return true;
        var result = await UiService.ConfirmDeleteAsync(
            L["DiscardChangesConfirmation"].Value,
            L["Cancel"].Value);
        return result == ConfirmDialogResult.Yes;
    }

    public virtual async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;
            if (!IsNewMode && Id != null)
            {
                EditModel = await CrudAppService.GetAsync(Id);
            }
            else
            {
                EditModel = new TGetDto();
                await OnModelCreatedAsync(EditModel);
            }
            CaptureSnapshot();
            RebuildEditContext();   // EditModel referansı değişti → context'i yenile
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading data for Edit form");
            ShowErrorLines(CrudErrorFormatter.Extract(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Yeni kayıt oluşturulduğunda DTO'nun default değerlerini set etmek için ezilebilir.
    /// </summary>
    protected virtual Task OnModelCreatedAsync(TGetDto model) => Task.CompletedTask;

    /// <summary>
    /// XAF Validation modülü emsali: Create/UpdateDto üzerindeki DataAnnotation'ları (Required,
    /// StringLength/MaxLength, Range) sunucuya gitmeden kontrol eder. Mesajlar projenin
    /// "Validation:*" anahtarlarıyla LOCALIZE edilir; alan adı için L[propName] denenir (yoksa ham ad).
    /// Geçersizse toast gösterir ve false döner. Özel kurallar için override edilebilir.
    /// </summary>
    protected virtual bool ValidateInput(object input)
    {
        // (alanAdı, mesaj) — alan adı DTO property'si (EditModel ile aynı) → inline eşleşir.
        var found = new List<(string Field, string Message)>();
        foreach (var prop in input.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(input);
            var field = FieldDisplayName(prop.Name);

            foreach (var attr in prop.GetCustomAttributes(inherit: true))
            {
                switch (attr)
                {
                    case RequiredAttribute when value == null || (value is string s && string.IsNullOrWhiteSpace(s)):
                        found.Add((prop.Name, L["Validation:Required", field].Value));
                        break;
                    case StringLengthAttribute sl when value is string s2 && s2.Length > sl.MaximumLength:
                        found.Add((prop.Name, L["Validation:MaxLength", field, sl.MaximumLength].Value));
                        break;
                    case MaxLengthAttribute ml when ml.Length >= 0 && value is string s3 && s3.Length > ml.Length:
                        found.Add((prop.Name, L["Validation:MaxLength", field, ml.Length].Value));
                        break;
                    case RangeAttribute r when value is IComparable cmp:
                        try
                        {
                            var min = (IComparable)Convert.ChangeType(r.Minimum, value!.GetType());
                            var max = (IComparable)Convert.ChangeType(r.Maximum, value.GetType());
                            if (cmp.CompareTo(min) < 0 || cmp.CompareTo(max) > 0)
                                found.Add((prop.Name, L["Validation:Range", field, r.Minimum, r.Maximum].Value));
                        }
                        catch { /* tür uyuşmazsa atla */ }
                        break;
                }
            }
        }

        // Inline mesajları her zaman tazele (geçerliyse temizlenmiş olur).
        _messages?.Clear();
        var distinct = found.Distinct().ToList();
        if (EditContext != null && _messages != null)
        {
            foreach (var (f, m) in distinct)
                _messages.Add(EditContext.Field(f), m);   // aynı alanda çok mesaj → alt alta gösterilir
            EditContext.NotifyValidationStateChanged();
        }

        if (distinct.Count == 0) return true;
        // Toast: her hata AYRI notification (XAF tarzı).
        foreach (var msg in distinct.Select(x => x.Message).Distinct())
            UiService.ShowErrorToast(msg);
        return false;
    }

    /// <summary>Çok satırlı hata metnini satır başına ayrı toast olarak gösterir (XAF tarzı).</summary>
    protected void ShowErrorLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) { UiService.ShowErrorToast(text); return; }
        foreach (var line in lines) UiService.ShowErrorToast(line);
    }

    /// <summary>Alan adı için yerelleştirilmiş başlık (L[propName]); anahtar yoksa ham ad.</summary>
    private string FieldDisplayName(string propName)
    {
        var loc = L[propName];
        return loc.ResourceNotFound ? propName : loc.Value;
    }

    public virtual async Task<bool> SaveAsync()
    {
        var wasNew = IsNewMode;   // notify anında Id set edilmiş olacağından önceden yakala
        try
        {
            IsBusy = true;

            if (IsNewMode)
            {
                var createDto = ObjectMapper.Map<TGetDto, TCreateDto>(EditModel);
                if (!ValidateInput(createDto)) return false;   // XAF tarzı: geçersizse sunucuya gitmeden engelle
                var created = await CrudAppService.CreateAsync(createDto);
                Id = created.Id;

                // Kaydedildikten sonra EditModel'in ID'sini de güncelle.
                if (EditModel is Volo.Abp.Application.Dtos.EntityDto<TKey> dto)
                {
                    dto.Id = created.Id;
                }
            }
            else
            {
                var updateDto = ObjectMapper.Map<TGetDto, TUpdateDto>(EditModel);
                if (!ValidateInput(updateDto)) return false;
                await CrudAppService.UpdateAsync(Id!, updateDto);
            }

            EntityChanges.Notify(EntityChangeKey,
                wasNew ? EntityChangeKind.Created : EntityChangeKind.Updated, Id);
            CaptureSnapshot(); // kayıt sonrası model artık "temiz"
            UiService.ShowSuccessToast(LocalizationResource != null ? L["SuccessfullySaved"].Value : "Successfully saved");

            if (OnSaved.HasDelegate)
            {
                await OnSaved.InvokeAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving data in Edit form");
            // API'den gelen mesajları göster
            ShowErrorLines(CrudErrorFormatter.Extract(ex));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public virtual async Task SaveAndNewAsync()
    {
        if (await SaveAsync())
        {
            Id = default;
            await LoadDataAsync();
        }
    }

    public virtual async Task SaveAndCloseAsync()
    {
        if (await SaveAsync())
        {
            await CloseAsync();
        }
    }

    public virtual async Task DeleteRecordAsync()
    {
        if (IsNewMode || Id == null) return;
        var confirm = await UiService.ConfirmDeleteAsync(L["DeleteConfirmationMessage"].Value ?? "Are you sure?");
        if (confirm != ConfirmDialogResult.Yes) return;
        try
        {
            IsBusy = true;
            var deletedId = Id;
            await CrudAppService.DeleteAsync(Id);
            EntityChanges.Notify(EntityChangeKey, EntityChangeKind.Deleted, deletedId);
            UiService.ShowSuccessToast(L["SuccessfullyDeleted"].Value ?? "Deleted");
            await CloseAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting data");
            ShowErrorLines(CrudErrorFormatter.Extract(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public virtual async Task CloseAsync()
    {
        if (OnClosed.HasDelegate)
        {
            await OnClosed.InvokeAsync();
        }
    }
}
