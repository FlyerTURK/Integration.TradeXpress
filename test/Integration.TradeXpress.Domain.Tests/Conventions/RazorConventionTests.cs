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
    // Allow-list = ŞU AN @code taşıyan TÜM .razor (grep kanıtı: 103 → 101 → 100 → 95; Stone/Jewelry panelleri code-behind
    // base'e, Cash+Metal+Scrap+Future+Convert+Service panelleri ProcessPanelHostBase + .razor.cs'e taşındı 2026-07-03). Golden yeşil.
    // YENİ dosya @code eklerse (listede yoksa) KIRMIZI → .razor.cs'e taşınmaya zorlar.
    // İstisna eklerken: gerekçe yorumu ekle (neden inline @code meşru).
    private static readonly HashSet<string> CodeBlockAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditForm.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditHost.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudEditView.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudFooterToolbar.razor",
        "src/Integration.Framework.Blazor.Client/Components/Crud/CrudToolbar.razor",
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
        "src/Integration.TradeXpress.Blazor.Client/Pages/CurrentTransactions/ProcessPanelBase.razor",
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
        // MetalLayout.razor 2026-07-10'da code-behind'a taşındı (maden görseli işi) — listeden çıktı.
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
    // Allow-list = ŞU AN tam-nitelikli @inject taşıyan TÜM .razor (grep kanıtı: 36 → 34 → 33 → 28; Stone/Jewelry panelleri
    // code-behind base'e, Cash+Metal+Scrap+Future+Convert+Service panelleri ProcessPanelHostBase + .razor.cs'e taşındı 2026-07-03). Golden yeşil.
    // YENİ dosya tam-nitelikli @inject eklerse KIRMIZI → tipi _Imports.razor @using'ine taşı, kısa ad kullan.
    private static readonly HashSet<string> QualifiedInjectAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.Framework.Blazor.Client/Components/Crud/TxGrid.razor",
        "src/Integration.TradeXpress.Blazor/Components/App.razor",
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
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/Components/MarginSetDialog.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Financials/CurrencyUnits/CurrencyUnitEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Futures/FutureEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Jewelries/JewelryEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Metals/MetalEditHost.razor",
        "src/Integration.TradeXpress.Blazor.Client/Pages/Reports/BalanceSheetReportPage.razor",
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

    [Fact]
    public void Razor_comments_must_not_sit_between_component_attributes()
    {
        // Kural (ui-blazor §DevExpress gotcha'ları): @* *@ bir bileşen etiketinin ATTRIBUTE'ları arasına konamaz.
        // Blazor yorumu parametre ADI sanır ve çalışma anında patlar: "does not have a property matching the
        // name '@* ... *@'" → sayfa "Beklenmeyen hata", circuit kopar. Derleme bunu YAKALAMAZ; bu yüzden
        // mekanik ağ şart (2026-07-27: kural yazılıydı ama yine ihlal edildi). Yorum etiketin DIŞINA alınır.
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var rel = ConventionSource.RelativePath(file);
            var insideTag = false;

            foreach (var (line, index) in File.ReadLines(file).Select((l, i) => (l.Trim(), i + 1)))
            {
                if (insideTag && line.StartsWith("@*", StringComparison.Ordinal))
                {
                    violations.Add($"{rel}:{index}: bileşen attribute'ları arasında @* *@ yorumu → etiketin DIŞINA al (ui-blazor).");
                }

                // Çok satırlı bileşen etiketi açıldı mı / kapandı mı (kaba ama bu kural için yeterli).
                if (OpeningComponentTagRegex.IsMatch(line) && !line.EndsWith(">", StringComparison.Ordinal))
                {
                    insideTag = true;
                }
                else if (insideTag && line.EndsWith(">", StringComparison.Ordinal))
                {
                    insideTag = false;
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor dosyalarında attribute'lar arasına yorum konmuş (runtime'da çöker):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>Büyük harfle başlayan bileşen etiketi açılışı (&lt;DxGrid, &lt;DrillList …).</summary>
    private static readonly Regex OpeningComponentTagRegex = new(@"^<[A-Z][A-Za-z0-9]*", RegexOptions.Compiled);

    /// <summary>DevExpress "nested settings" şablonları — içlerinde ASENKRON render yasak.</summary>
    private static readonly string[] NestedSettingsTemplates =
    {
        "CellDisplayTemplate", "CellEditTemplate", "HeaderTemplate", "FooterTemplate", "GroupRowTemplate",
    };

    /// <summary>Satır içi <c>async</c> lambda olay işleyicisi: <c>@(async …)</c> ya da <c>="@(async …)"</c>.</summary>
    private static readonly Regex AsyncLambdaHandlerRegex = new(@"@\(\s*async|=\s*""@\(async", RegexOptions.Compiled);

    [Fact]
    public void Razor_devexpress_templates_must_not_use_async_event_handlers()
    {
        // Kural (ui-blazor §DevExpress gotcha'ları [async-nested-settings]): DevExpress kolon/ayar şablonlarının
        // İÇİNDEKİ olay işleyicisi async olamaz. Blazor işleyici bitince StateHasChanged çağırır, DevExpress'in
        // SettingsRenderer'ı bunu ASENKRON render sayar ve "Async rendering is not allowed here" ile ÇÖKER
        // (2026-07-27: hücre içi Durum switch'i ve N11 cari combo'su bu yüzden düştü). Derleme yakalamaz.
        // Doğrusu: işleyici SENKRON; sunucuya gitmesi gereken iş InvokeAsync ile arka planda + hata gösterimi.
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.razor"))
        {
            var rel = ConventionSource.RelativePath(file);
            var depth = 0;

            foreach (var (line, index) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
            {
                foreach (var template in NestedSettingsTemplates)
                {
                    depth += CountOccurrences(line, $"<{template}");
                    depth -= CountOccurrences(line, $"</{template}>");
                }

                if (depth > 0 && AsyncLambdaHandlerRegex.IsMatch(line))
                {
                    violations.Add($"{rel}:{index}: DevExpress şablonu içinde async olay işleyicisi → senkron yaz, "
                                   + "asenkron işi InvokeAsync ile arka planda çalıştır (ui-blazor [async-nested-settings]).");
                }
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki .razor dosyalarında DevExpress şablonu içinde async işleyici var (runtime'da çöker):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary><c>ProductCommoditySeed.EditComponentOf</c> switch'inden <c>typeof(X.YEditHost)</c> host tiplerini
    /// çıkarır — liste kodda değişirse test sözleşmeyi otomatik takip eder.</summary>
    private static readonly Regex EditHostTypeofRegex = new(@"typeof\(\s*([A-Za-z0-9_.]*\.)?(\w+EditHost)\s*\)", RegexOptions.Compiled);

    /// <summary><c>[Parameter] public string? X { get; set; }</c> deseni (aynı satırda) — salt X kullanımını değil TANIMINI arar.</summary>
    private static Regex ParameterDeclarationRegex(string name) =>
        new(@"\[Parameter\][^\n;]*\b" + Regex.Escape(name) + @"\b\s*\{\s*get;\s*set;\s*\}", RegexOptions.Compiled);

    [Fact]
    public void Classification_panel_edit_hosts_must_declare_seed_parameters()
    {
        // Kural (2026-08-07 U1): sınıflandırma paneli ve reçete panelinin "Üründen" anahtarı emtia formunu
        // extraParams'la (SeedCode/SeedName) açıyor; bu [Parameter]'ları TANIMLAMAYAN host'ta DynamicComponent
        // çalışma anında InvalidOperationException fırlatıp CIRCUIT'İ DÜŞÜRÜR (Good dışı 6 ailede canlıda
        // yaşandı). Eşleme 2026-08-14'te tek kaynağa indi: ProductCommoditySeed.EditComponentOf — iki yüzey de
        // oradan okur, test de oradan tarar; liste büyürse sözleşmeyi otomatik takip eder.
        var seedSource = ConventionSource.EnumerateSource("ProductCommoditySeed.cs").FirstOrDefault();
        seedSource.ShouldNotBeNull("ProductCommoditySeed.cs bulunamadı — EditComponentOf sözleşmesi taranamıyor.");

        var hostTypeNames = EditHostTypeofRegex.Matches(File.ReadAllText(seedSource))
            .Select(m => m.Groups[2].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        hostTypeNames.ShouldNotBeEmpty("EditComponentOf switch'inde hiç {Family}EditHost bulunamadı — regex bayat olabilir.");

        var hostFiles = ConventionSource.EnumerateSource("*.razor.cs")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Replace(".razor.cs", ".razor")), StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var host in hostTypeNames)
        {
            if (!hostFiles.TryGetValue(host, out var file))
            {
                violations.Add($"{host}: EditComponentOf listesinde ama {host}.razor.cs bulunamadı.");
                continue;
            }

            // [Parameter] TANIMINI ara, salt "SeedCode" string'ini DEĞİL — aksi halde ApplyNewDefaults'taki
            // `m.Code = SeedCode` kullanımı, parametre tanımı silinse bile testi yanıltarak yeşil bırakır
            // (sabotaj testinde yakalandı, 2026-08-07).
            var text = File.ReadAllText(file);
            var hasSeedCode = ParameterDeclarationRegex("SeedCode").IsMatch(text);
            var hasSeedName = ParameterDeclarationRegex("SeedName").IsMatch(text);
            if (!hasSeedCode || !hasSeedName)
            {
                var eksik = (!hasSeedCode ? "SeedCode " : "") + (!hasSeedName ? "SeedName" : "");
                violations.Add($"{host}: eksik [Parameter] → {eksik.Trim()} (panelden extraParams gelince circuit çöker; U1).");
            }
        }

        violations.ShouldBeEmpty(
            "Sınıflandırma panelinin açtığı emtia host'ları Seed parametrelerini eksik tanımlıyor:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// REÇETE PANELİNDEKİ HER KATALOG LOOKUP'I RELOAD BAĞLAMALI. <c>LookupComboBox</c> "Ekle/Düzelt" popup'ı kayıt
    /// yapınca <c>OnLookupReloadRequested</c> ile host'tan liste tazelemesi ister; bağlı değilse kayıt olur ama
    /// combo'da GÖRÜNMEZ (2026-08-15 Hakan bulgusu — mamul ekleyip geri dönünce combo'da yoktu; sekiz katalog
    /// lookup'ının hiçbiri bağlamıyordu, aynı sayfanın diğer beş lookup'ı bağlıydı). Panel <c>Data</c>'yı hep
    /// dışarıdan aldığından bu kural panelde ŞARTTIR; her katalog lookup'ı <c>OnCatalogReloadRequested</c>'a bağlanır.
    /// Birim lookup'ları (<c>CurrentPriceDto</c>) fiyat cache'idir, katalog değil — kapsam dışı.
    /// </summary>
    [Fact]
    public void Recipe_panel_catalog_lookups_must_request_reload_after_inline_add()
    {
        var panel = ConventionSource.EnumerateSource("ProductRecipePanel.razor").FirstOrDefault();
        panel.ShouldNotBeNull("ProductRecipePanel.razor bulunamadı.");

        var text = File.ReadAllText(panel);
        // Her <LookupComboBox ...> açılış etiketini yakala (çok satırlı). Attribute değerleri lambda taşır
        // ("=> OnX(v)") → '>' etiket sonu SAYILMAZ: tırnak içi atlanır, yalnız tırnak dışındaki '>' kapatır.
        var lookups = Regex.Matches(text, "<LookupComboBox\\b(?<attrs>(?:\"[^\"]*\"|[^\">])*)>", RegexOptions.Singleline);
        lookups.Count.ShouldBeGreaterThan(0, "Panelde LookupComboBox bulunamadı — regex bayat olabilir.");

        var violations = new List<string>();
        foreach (Match m in lookups)
        {
            var attrs = m.Groups["attrs"].Value;
            var isCatalog = !attrs.Contains("TItem=\"CurrentPriceDto\"", StringComparison.Ordinal);
            if (isCatalog && !attrs.Contains("OnLookupReloadRequested=", StringComparison.Ordinal))
            {
                var tItem = Regex.Match(attrs, "TItem=\"(?<t>[^\"]+)\"").Groups["t"].Value;
                violations.Add($"ProductRecipePanel.razor: LookupComboBox TItem={tItem} → OnLookupReloadRequested bağlı değil (yeni kayıt combo'da görünmez).");
            }
        }

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    private static int CountOccurrences(string line, string token)
    {
        var count = 0;
        var position = line.IndexOf(token, StringComparison.Ordinal);
        while (position >= 0)
        {
            count++;
            position = line.IndexOf(token, position + token.Length, StringComparison.Ordinal);
        }

        return count;
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
