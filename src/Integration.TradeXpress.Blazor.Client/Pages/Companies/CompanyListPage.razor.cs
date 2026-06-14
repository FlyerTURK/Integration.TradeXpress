using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

public partial class CompanyListPage
{
    public CompanyListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject] protected ICompanyAppService CompanyAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        CompanyGetDto, CompanyListDto, Guid,
        CompanyListRequestDto, CompanyCreateDto, CompanyUpdateDto> CrudAppService
        => CompanyAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Companies.Default;

    // ── Toolbar drill (persistent): Şirket → Şubeler → Kasalar ───────────────────

    private CompanyListDto? SelectedCompany =>
        StateService.SelectedDataItems is { Count: 1 } sel ? sel[0] as CompanyListDto : null;

    private string SelectedCompanyName => SelectedCompany?.Name ?? string.Empty;

    private bool _branchPopupVisible;
    private bool _vaultPopupVisible;
    private BranchTreeItemViewModel? _selectedBranch;

    private void OpenBranchesAsync() => _branchPopupVisible = true;

    private void OnBranchSelected(BranchTreeItemViewModel? b) => _selectedBranch = b;

    private void OpenVaultsDrill() => _vaultPopupVisible = true;

    // ── Edit formu değişince IsDirty — combo/drill EditContext'i atlar. ────────
    private void MarkDrillDirty() => StateService.IsDirty = true;

    // ── CRUD ──────────────────────────────────────────────────────────────────────

    // Yeni şirket: varsayılan in-memory ağaçla başlar (bir HQ "Merkez Şube" + bir "Ana Kasa").
    public override Task BeforeCreateAsync()
    {
        var vm = new CompanyViewModel();
        vm.Branches.Add(CompanyTreeMapping.NewHeadquartersBranch());
        StateService.EditingModel = vm;
        StateService.ShowEditPage(isNewRecord: true);
        return Task.CompletedTask;
    }

    // Düzenleme: tam ağacı (şube + kasa) GetTree ile yükle.
    public override async Task BeforeUpdateAsync(CompanyListDto entity)
    {
        StateService.SetDataRowSelected(entity);
        await ExecuteAsync(async () =>
        {
            var tree = await CompanyAppService.GetTreeAsync(entity.Id);
            StateService.EditingModel = CompanyTreeMapping.ToViewModel(tree);
            StateService.ShowEditPage(isNewRecord: false);
        });
    }

    // Kaydet: önce tüm ağacı (şube+kasa dahil) toplu doğrula, sonra tek transaction'da yaz.
    public override async Task SaveAsync()
    {
        var model = StateService.EditingModel!;
        if (!TryValidateTree(model, out var error))
        {
            await Notify.Warn(string.Format(L["TreeValidationFailed"], error));
            return;
        }

        await ExecuteAsync(async () =>
        {
            var dto = CompanyTreeMapping.ToSaveDto(model);
            await CompanyAppService.SaveTreeAsync(dto);
            StateService.HideEditPage();
            StateService.RequestReload();
            await Notify.Success(L["SuccessfullySaved"]);
        });
    }

    // Şirket + tüm şube + tüm kasaları DataAnnotations ile özyinelemeli doğrula.
    private static bool TryValidateTree(CompanyViewModel m, out string error)
    {
        var errors = new List<string>();
        ValidateOne(m, m.Name, errors);
        foreach (var b in m.Branches)
        {
            ValidateOne(b, b.Name, errors);
            foreach (var v in b.Vaults)
                ValidateOne(v, v.Name, errors);
        }
        error = string.Join("; ", errors.Distinct().Take(5));
        return errors.Count == 0;
    }

    private static void ValidateOne(object obj, string label, List<string> errors)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(obj, new ValidationContext(obj), results, validateAllProperties: true))
            errors.AddRange(results.Select(r => $"{(string.IsNullOrWhiteSpace(label) ? "?" : label)}: {r.ErrorMessage}"));
    }
}
