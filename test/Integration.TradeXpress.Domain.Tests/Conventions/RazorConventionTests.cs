using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Razor yazım konvansiyonlarının MEKANİK güvenlik ağı (governance Katman 2). Bu test KIRMIZIYSA bir .razor
/// dokümante UI kuralını çiğnemiştir — kural yalnız memory'de/insan dikkatinde kalmasın, <c>dotnet test</c>'te
/// yakalansın. Kaynak kurallar: ui-blazor (code-behind · ikon-seti · _Imports toplama). EntityConventionTests
/// ile aynı iskelet: <c>src/**/*.razor</c> tek geçiş taranır (obj/bin hariç), ihlal listesi tek mesajda verilir.
/// <para>Allow-list'ler = MEVCUT durum (grep kanıtıyla dolduruldu → golden YEŞİL). Listede OLMAYAN yeni dosya
/// ihlal ederse KIRMIZI. Allow-list'e ekleme = bilinçli istisna; yanına gerekçe yaz.</para>
/// </summary>
public class RazorConventionTests
{
    // ── Regex'ler (satır-bazlı; RegexOptions.Multiline) ─────────────────────────────────────────────
    // @code bloğu: satır başında (opsiyonel boşluk) '@code'. Golden: aşağıdaki allow-list'teki dosyalar.
    private static readonly Regex CodeBlockRegex = new(@"^[ \t]*@code\b", RegexOptions.Multiline | RegexOptions.Compiled);

    // Tam-nitelikli @inject: enjekte edilen satırda 3+ segmentli (A.B.C...) nokta-zincirli tip adı.
    private static readonly Regex QualifiedInjectRegex = new(@"@inject\b.*\b\w+(\.\w+){2,}", RegexOptions.Compiled);

    // Ad-hoc sembol ikonları (ui-blazor: FrameworkIcons/custom-icon kullan, ham sembol YOK) + emoji aralığı.
    private static readonly char[] BannedIconChars = { '✎', '✏', '➕', '⚠', '≈', '✔', '✖', '❌', '⭐' };

    // Yorumları soymak için (sembol taraması SADECE gerçek markup'a bakmalı): razor + C# blok + C# satır yorumu.
    private static readonly Regex RazorCommentRegex = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@"(?<!:)//[^\n]*", RegexOptions.Compiled); // '://' (URL) korunur

    // ── TEST a — Yeni .razor'da @code yasak (code-behind → .razor.cs partial) ───────────────────────
    // Allow-list = ŞU AN @code taşıyan TÜM .razor (grep kanıtı: 103 → 101; Stone/Jewelry panelleri code-behind base'e taşındı 2026-07-03). Golden yeşil.
    // YENİ dosya @code eklerse (listede yoksa) KIRMIZI → .razor.cs'e taşınmaya zorlar.
    // İstisna eklerken: gerekçe yorumu ekle (neden inline @code meşru).
    private static readonly HashSet<string> CodeBlockAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditForm.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditHost.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditView.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudFooterToolbar.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudToolbar.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/DrillList.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/DrillTabs.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/EntityEditForm.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/GridLinkColumn.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/LocalizedDataAnnotationsValidator.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/LookupComboBox.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/LookupEdit.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/SplitCrudView.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/ToolbarRenderer.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/TxGrid.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/ValueObjectEdit.razor",
        "src/Integration.Framework.Blazor.Client/Components/Inputs/PasswordTextEdit.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/ComboBoxEnumEdit.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/ConfirmDeleteDialog.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/EditShell.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/GlobalPopupHost.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/InfoCaption.razor",
        "src/Integration.Framework.Blazor.Client/Components/Shared/NumericSpinEdit.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Crud/BranchEditFields.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Crud/CompanyBranchDrill.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Crud/CompanyEditFields.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Crud/VaultDrill.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/CrudEditShell.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Mdi/MdiTabHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/StatusCell.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/StatusColumn.razor",
        "src/Integration.TradeXpress.Blazor.Client/Dev/DeveloperErrorPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/MainLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/NavMenu.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/RedirectToHome.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/RedirectToLogin.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/SettingsPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/UserAvatarMenu.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/UserInfoPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/WorkingBranchSelector.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Account/Login.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Account/TenantPickerPopup.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Accounts/AccountEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Accounts/AccountLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Accounts/SubAccountEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/EffectivePermissionsView.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/PermissionEditorPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/PermissionManagementPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/RoleEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/RoleLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/ScopedDirectPermissionsEditor.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/ScopedRolesEditor.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/UserEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Admin/UserLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/AssayOffices/AssayOfficeEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/AssayOffices/AssayOfficeLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Authentication.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Cashes/CashEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Cashes/CashLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/BranchEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/BranchLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/CompanyEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/CompanyLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Countries/CountryEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Countries/CountryEditPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Countries/CountryLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/CashProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ConvertProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/FutureProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/MetalProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ProcessPanelBase.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ScrapProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ServiceProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/Components/MarginSetDialog.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/CurrencyUnitEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/CurrencyUnitEditPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/CurrencyUnitLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/Parities/ParityEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/Parities/ParityLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Futures/FutureEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Futures/FutureLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Index.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Jewelries/JewelryEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Jewelries/JewelryLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Metals/MetalEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Metals/MetalLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/ResetTabs.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Scheduling/SchedulerPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Scraps/ScrapEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Scraps/ScrapLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Services/ServiceEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Services/ServiceLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Stones/StoneEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Stones/StoneLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/TenantManagement/TenantEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/TenantManagement/TenantLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Testing/N11TestPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Vaults/VaultEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Vaults/VaultEditPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Vaults/VaultLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Routes.razor",
    };

    // ── TEST b — Ad-hoc sembol ikonu (yorumlar soyulduktan sonra markup'ta) ──────────────────────────
    // Allow-list = ≈ (yaklaşık) göstergesi; koordinatör onayıyla girdi (DENETIM-2026-07-02 §E5 notu).
    // Yeni sembol markup'a girerse (listede yoksa) KIRMIZI → custom-icon-* ya da metin etiketi kullan.
    private static readonly HashSet<string> IconAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/CurrentTransactionForm.razor",
    };

    // ── TEST c — Tam-nitelikli @inject (namespace ön-eki = koku; _Imports.razor'a taşınmalı) ─────────
    // Allow-list = ŞU AN tam-nitelikli @inject taşıyan TÜM .razor (grep kanıtı: 36 → 34; Stone/Jewelry panelleri code-behind base'e taşındı 2026-07-03). Golden yeşil.
    // YENİ dosya tam-nitelikli @inject eklerse KIRMIZI → tipi _Imports.razor @using'ine taşı, kısa ad kullan.
    private static readonly HashSet<string> QualifiedInjectAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.Framework.Blazor.Client/Components/Crud/TxGrid.razor",
        "src/Integration.TradeXpress.Blazor/Components/App.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Mdi/MdiTabHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/StatusCell.razor",
        "src/Integration.TradeXpress.Blazor.Client/Components/Shared/StatusColumn.razor",
        "src/Integration.TradeXpress.Blazor.Client/Dev/DeveloperErrorPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/MainLayout.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/SessionTypeBadge.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/SettingsPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/UserAvatarMenu.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/UserInfoPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Layout/WorkingBranchSelector.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Accounts/AccountEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Accounts/SubAccountListPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Cashes/CashEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/BranchListPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Companies/CompanyListPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/AccountSelectionPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/CashProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ConvertProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/FutureProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/MetalProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ScrapProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ServiceProcessPanel.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/Components/MarginSetDialog.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/CurrencyUnitEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Futures/FutureEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Jewelries/JewelryEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Metals/MetalEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Reports/BalanceSheetReportPage.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/ResetTabs.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Scraps/ScrapEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Services/ServiceEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Stones/StoneEditHost.razor",
    };

    [Fact]
    public void Razor_must_not_introduce_inline_code_block_use_code_behind_partial()
    {
        // Kural (ui-blazor): yeni bileşende inline @code YOK → code-behind (.razor.cs partial). Mevcutlar allow-list'te.
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var rel = ConventionSource.RelativePath(file);
            if (CodeBlockAllowList.Contains(rel))
            {
                continue;
            }

            if (CodeBlockRegex.IsMatch(File.ReadAllText(file)))
            {
                violations.Add($"{rel}: yeni bileşende @code yasak → .razor.cs partial (ui-blazor.md).");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor dosyaları inline @code taşıyor (allow-list dışı → code-behind'a taşı):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Razor_markup_must_not_use_ad_hoc_symbol_icons()
    {
        // Kural (ui-blazor §ikon): ham sembol/emoji ikon YOK → FrameworkIcons/custom-icon-* ya da metin etiketi.
        // Yorumlar (@* *@, /* */, //) SOYULUR — sadece gerçek markup taranır (dokümantasyondaki ✎ meşru).
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var rel = ConventionSource.RelativePath(file);
            if (IconAllowList.Contains(rel))
            {
                continue;
            }

            var markup = StripComments(File.ReadAllText(file));
            var found = markup.Where(IsBannedIcon).Distinct().ToArray();
            if (found.Length > 0)
            {
                violations.Add($"{rel}: markup'ta ad-hoc sembol ikonu [{string.Join(" ", found)}] → custom-icon/metin kullan (ui-blazor §ikon).");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor markup'ları ham sembol/emoji ikon içeriyor:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Razor_inject_must_not_use_fully_qualified_type_names()
    {
        // Kural (kod-stili §namespace): @inject tipi 3+ segmentli tam-nitelikli olamaz → _Imports.razor'a taşı, kısa ad kullan.
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var rel = ConventionSource.RelativePath(file);
            if (QualifiedInjectAllowList.Contains(rel))
            {
                continue;
            }

            if (QualifiedInjectRegex.IsMatch(File.ReadAllText(file)))
            {
                violations.Add($"{rel}: tam-nitelikli @inject → tipi _Imports.razor @using'ine taşı, kısa ad kullan (kod-stili §namespace).");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor dosyaları tam-nitelikli @inject içeriyor (allow-list dışı):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    // Ham sembol listesi VEYA emoji aralığı (U+1F300–1F9FF). Bu aralık BMP dışı → C# string'de surrogate çifti
    // ile temsil edilir; yüksek surrogate D83C–D83E o aralığı kapsar (tek-char taramasıyla yakalanır).
    private static bool IsBannedIcon(char c) =>
        Array.IndexOf(BannedIconChars, c) >= 0 || (c >= '\uD83C' && c <= '\uD83E');

    private static string StripComments(string text)
    {
        text = RazorCommentRegex.Replace(text, string.Empty);
        text = BlockCommentRegex.Replace(text, string.Empty);
        text = LineCommentRegex.Replace(text, string.Empty);
        return text;
    }
}
