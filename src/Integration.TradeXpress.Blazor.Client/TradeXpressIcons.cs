namespace Integration.TradeXpress.Blazor.Client;

/// <summary>
/// Entity/navigasyon ikonlarının TEK kaynağı. Her entity'nin ikonu burada bir kez tanımlanır;
/// list page (EntityIcon), drill (EntityIcon), MDI sekme açma (TabManager), menü ve toolbar
/// child-açma butonları hep buradan okur. Yeni entity eklerken ikonu yalnız buraya yaz.
/// FontAwesome (free, solid) class'ları.
/// </summary>
public static class TradeXpressIcons
{
    // ── Org hiyerarşisi ──
    public const string Company = "custom-icon-company";
    public const string Branch = "custom-icon-branch";
    public const string Vault = "custom-icon-select-vault";

    // ── Tanımlar ──
    public const string Country = "custom-icon-country";
    public const string CurrencyUnit = "custom-icon-currency-unit";
    public const string Cash = "custom-icon-cash";
    public const string Service = "custom-icon-service";
    // SalesChannel'a özel ikon yok — kanala özel SVG/CSS ileride ayrı polish (CSS §1 onayı). Şimdilik generic list.
    public const string SalesChannel = "custom-icon-list";
    public const string Future = "custom-icon-future";
    public const string Scrap = "custom-icon-scrap";
    public const string Metal = "custom-icon-metal";
    public const string Stone = "custom-icon-stone";
    public const string Jewelry = "custom-icon-jewelry";
    // Mamül (bitmiş/paketlenmiş ürün) — kendi kutu ikonu (mavi + turuncu bant); emtia ailesiyle uyumlu, commodities'ten ayrık.
    public const string Good = "custom-icon-good";
    // Özel Kod (gruplama sözlüğü) — kendi etiket ikonu (mor tag).
    public const string SpecialCode = "custom-icon-special-code";
    // Sipariş eklentisi (kurdele/kutu/ambalaj) katalogu — mevcut fiyat ikonu reuse (fiyatlı seçenek); yeni CSS yok.
    public const string AddOn = "custom-icon-price";
    // Varyant tanım katalogu (demet) — nitelik ekseni ikonu (sliders) reuse (agnostik nitelik paneliyle hizalı); yeni CSS yok.
    public const string VariantTemplate = "custom-icon-sliders";
    // Kargo şablonu katalogu — mevcut adres-kartı ikonu reuse (şablonun çekirdeği menşei/iade adresi + teslimat); yeni CSS yok.

    // ── Ürünler ──
    public const string Product = "custom-icon-commodities";
    // ProductVariant — kendi varyant ikonu (üst üste yeşil kartlar = kopyalar).
    public const string ProductVariant = "custom-icon-product-variant";

    // ── Muadil (Substitution) ── mevcut swap ikonu yeniden kullanılır (ikame = takas; yeni CSS yok).
    public const string Substitution = "custom-icon-swap";

    // ── Hesaplar ──
    public const string Account = "custom-icon-account";
    public const string SubAccount = "custom-icon-list";
    public const string Accounts = "custom-icon-accounts";
    public const string CurrencyMargin = "custom-icon-currency-margin";
    public const string Parity = "custom-icon-parity";

    // ── İşlemler ──
    public const string CurrentTransactions = "custom-icon-current-transactions";
    // Kasa→kasa transfer — mevcut swap (takas) ikonu reuse (kasalar arası değer taşıma); yeni CSS yok.
    public const string Transfer = "custom-icon-swap";
    // Teyit (karşılıklı ayna onayı) — mevcut check-circle (onay) ikonu reuse; yeni CSS yok.
    public const string Confirmation = "custom-icon-check-circle";

    // ── Yönetim ──
    public const string Tenant = "custom-icon-tenant";
    public const string Settings = "custom-icon-settings";
    public const string User = "custom-icon-user";
    public const string Role = "custom-icon-role";
    public const string Permission = "custom-icon-permission";

    // ── Menü grupları (parent düğümler) ──
    public const string Definitions = "custom-icon-definitions";
    public const string Commodities = "custom-icon-commodities";
    public const string Organizations = "custom-icon-organizations";
    public const string Financial = "custom-icon-financial";
    public const string Identity = "custom-icon-identity";

    // ── Genel ──
    public const string Home = "custom-icon-home";

    // �� Action & System ��
    public const string Add = "custom-icon-add";
    public const string Edit = "custom-icon-edit";
    public const string Delete = "custom-icon-delete";
    public const string Download = "custom-icon-download";
    public const string Spinner = "custom-icon-spinner custom-spin";
    public const string Comments = "custom-icon-comments";
    public const string Report = "custom-icon-report";
    public const string Save = "custom-icon-save";
    public const string Sliders = "custom-icon-sliders";
    public const string Back = "custom-icon-back";
    public const string Swap = "custom-icon-swap";
    public const string AddressCard = "custom-icon-address-card";
    public const string Check = "custom-icon-check";
    public const string CheckCircle = "custom-icon-check-circle";
    public const string Refresh = "custom-icon-refresh";
    public const string Warning = "custom-icon-warning";
    public const string Percent = "custom-icon-percent";
    public const string History = "custom-icon-history";
    public const string Close = "custom-icon-close";
    public const string Eye = "custom-icon-eye";
    public const string SignOut = "custom-icon-sign-out";
    public const string Server = "custom-icon-server";
    public const string Bug = "custom-icon-bug";
    public const string Copy = "custom-icon-copy";
    public const string ChevronDown = "custom-icon-chevron-down";
    public const string Lightbulb = "custom-icon-lightbulb";
}


