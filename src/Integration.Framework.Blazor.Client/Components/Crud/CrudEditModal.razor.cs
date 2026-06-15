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
        [Parameter] public RenderFragment<TViewModel>? EditPageContent { get; set; }
        [Parameter] public EventCallback OnSaveClick { get; set; }
        [Parameter] public EventCallback OnSaveAndNewClick { get; set; }
        [Parameter] public bool ValidateOnPropertyChange { get; set; } = true;

        [Inject] public ITradeXpressUiService UiService { get; set; } = default!;

        private EditContext? CurrentEditContext;
        private ValidationMessageStore? _serverErrorStore;

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
