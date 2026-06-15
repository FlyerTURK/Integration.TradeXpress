using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Integration.Framework.Blazor.Client.Services.Base;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    public partial class CrudEditModal<TGetDto, TListDto, TViewModel, TKey>
    {
        [Parameter, EditorRequired] public ICrudStateService<TGetDto, TListDto, TKey, TViewModel> StateService { get; set; } = default!;
        [Parameter] public string? EntityName { get; set; }

        /// <summary>Bu entity'nin ikonu (FontAwesome class) — başlıkta gösterilir. Boşsa generic ikon.</summary>
        [Parameter] public string? EntityIcon { get; set; }

        /// <summary>Başlıkta gösterilecek birincil değer seçici (genelde Code). Yeni: "Yeni {Entity} {Code?}",
        /// Düzenle: "{Entity} {Code}".</summary>
        [Parameter] public Func<TViewModel, string?>? PrimaryTextSelector { get; set; }

        /// <summary>Varsa üst (parent) entity adı — başlığa " - [ParentEntityName: ...]" eklenir.</summary>
        [Parameter] public string? ParentEntityName { get; set; }

        /// <summary>Üst entity gösterim metni seçici (genelde parent Code alanı).</summary>
        [Parameter] public Func<TViewModel, string?>? ParentTextSelector { get; set; }

        [Parameter] public RenderFragment<TViewModel>? EditPageContent { get; set; }
        [Parameter] public EventCallback OnSaveClick { get; set; }
        [Parameter] public EventCallback OnSaveAndNewClick { get; set; }
        [Parameter] public bool ValidateOnPropertyChange { get; set; } = true;

        [Inject] public ITradeXpressUiService UiService { get; set; } = default!;

        private EditContext? CurrentEditContext;
        private ValidationMessageStore? _serverErrorStore;

        // Başlık: Yeni → "Yeni {Entity} {Code?}", Düzenle → "{Entity} {Code/primary}". Code yoksa yalnız Entity adı.
        private string BuildTitle()
        {
            var name = EntityName ?? string.Empty;
            var model = StateService?.EditingModel;
            var primary = (model != null && PrimaryTextSelector != null) ? PrimaryTextSelector(model) : null;

            var title = (StateService != null && StateService.IsNewRecord)
                ? (string.IsNullOrWhiteSpace(primary) ? $"{L["New"]} {name}".Trim() : $"{L["New"]} {name} {primary}".Trim())
                : (string.IsNullOrWhiteSpace(primary) ? name : $"{name} {primary}");

            var parent = (model != null && ParentTextSelector != null) ? ParentTextSelector(model) : null;
            if (!string.IsNullOrWhiteSpace(ParentEntityName) && !string.IsNullOrWhiteSpace(parent))
                title += $" - [{ParentEntityName}: {parent}]";

            return title;
        }

        // Başlık ikonu: entity ikonu (yoksa generic düzenleme ikonu).
        private string HeaderIcon => string.IsNullOrEmpty(EntityIcon) ? "fas fa-pen-to-square" : EntityIcon!;

        protected override void OnParametersSet()
        {
            if (StateService != null && StateService.EditPageVisible && StateService.EditingModel != null)
            {
                if (CurrentEditContext?.Model != StateService.EditingModel)
                {
                    if (CurrentEditContext != null)
                    {
                        CurrentEditContext.OnFieldChanged -= EditContext_OnFieldChanged;
                        CurrentEditContext.OnValidationStateChanged -= EditContext_OnValidationStateChanged;
                    }
                    CurrentEditContext = new EditContext(StateService.EditingModel);
                    _serverErrorStore = null;
                    CurrentEditContext.OnFieldChanged += EditContext_OnFieldChanged;
                    CurrentEditContext.OnValidationStateChanged += EditContext_OnValidationStateChanged;
                }
            }
        }

        private void EditContext_OnFieldChanged(object? sender, FieldChangedEventArgs e)
        {
            if (!StateService.IsDirty)
                StateService.IsDirty = true;

            // Kullanıcı alanı düzenlemeye başladığında sunucu hataları temizlenir.
            if (_serverErrorStore != null)
            {
                _serverErrorStore.Clear();
                _serverErrorStore = null;
                CurrentEditContext?.NotifyValidationStateChanged();
            }

            if (ValidateOnPropertyChange)
                CurrentEditContext?.Validate();

            StateService.NotifyStateChanged();
        }

        // Submit attempt on untouched fields raises validation state (not field) changes — re-render
        // so the conditional validation summary appears.
        private void EditContext_OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
        {
            InvokeAsync(StateHasChanged);
        }

        private async Task OnPopupVisibleChanged(bool visible)
        {
            if (!visible)
            {
                if (StateService.IsDirty)
                {
                    StateService.EditPageVisible = true;
                    StateService.NotifyStateChanged();

                    var dialogResult = await UiService.ConfirmDeleteAsync(L["DiscardChangesConfirmation"]);
                    if (dialogResult == ConfirmDialogResult.Yes)
                    {
                        StateService.IsDirty = false;
                        StateService.HideEditPage();
                    }
                }
                else
                {
                    if (CurrentEditContext != null)
                    {
                        CurrentEditContext.OnFieldChanged -= EditContext_OnFieldChanged;
                        CurrentEditContext.OnValidationStateChanged -= EditContext_OnValidationStateChanged;
                        CurrentEditContext = null;
                    }
                    _serverErrorStore = null;
                    StateService.HideEditPage();
                }
            }
        }

        private Task HandleValidSubmit() => SubmitAsync(OnSaveClick);
        private Task HandleValidSubmitAndNew() => SubmitAsync(OnSaveAndNewClick);

        private async Task SubmitAsync(EventCallback callback)
        {
            if (!callback.HasDelegate) return;

            // Önceki sunucu hatalarını temizle.
            _serverErrorStore?.Clear();
            _serverErrorStore = null;
            CurrentEditContext?.NotifyValidationStateChanged();

            await callback.InvokeAsync();

            // Sunucu doğrulama hatalarını forma aktar.
            var serverErrors = StateService.PendingServerErrors;
            if (serverErrors?.Count > 0 && CurrentEditContext != null)
            {
                _serverErrorStore = new ValidationMessageStore(CurrentEditContext);
                foreach (var error in serverErrors)
                {
                    var field = string.IsNullOrEmpty(error.MemberName)
                        ? new FieldIdentifier(CurrentEditContext.Model, string.Empty)
                        : CurrentEditContext.Field(error.MemberName);
                    _serverErrorStore.Add(field, error.Message);
                }
                CurrentEditContext.NotifyValidationStateChanged();
                StateService.PendingServerErrors = null;
            }
        }
    }
}
