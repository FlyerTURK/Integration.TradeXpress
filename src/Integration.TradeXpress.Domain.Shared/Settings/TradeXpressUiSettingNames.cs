namespace Integration.TradeXpress.Settings;

public static class TradeXpressUiSettingNames
{
    private const string Prefix = "TradeXpress.UI.";

    public const string MdiTabs = Prefix + "MdiTabs";
    public const string GridStates = Prefix + "GridStates";
    public const string Theme = Prefix + "Theme";

    /// <summary>Kullanıcının boyut modu (Small/Medium/Large) — per-user, cihazdan bağımsız.
    /// Tarayıcıdaki tx.last_size cookie'si yalnız anonim (login) projeksiyondur; kaynak budur.</summary>
    public const string SizeMode = Prefix + "SizeMode";

    /// <summary>Kullanıcının UI dili (tr/en) — per-user, cihazdan bağımsız. ABP'nin DefaultLanguage ayarı
    /// request-localization'a dinamik uygulanmadığı için kendi anahtarımız + cookie projeksiyonu kullanılır.</summary>
    public const string Culture = Prefix + "Culture";

    /// <summary>Çalışma bağlamı — seçili çalışma ŞUBESİ (Branch.Id). Per-user, cihazdan bağımsız.
    /// Kasa seviyesine geçildikten sonra da yazılır: MDI sekme anahtarı budur.</summary>
    public const string WorkingBranch = Prefix + "WorkingBranch";

    /// <summary>Çalışma bağlamı — seçili çalışma KASASI (Vault.Id). Per-user, cihazdan bağımsız.</summary>
    public const string WorkingVault = Prefix + "WorkingVault";
}
