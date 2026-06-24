namespace Integration.TradeXpress.Settings;

public static class TradeXpressUiSettingNames
{
    private const string Prefix = "TradeXpress.UI.";

    public const string MdiTabs = Prefix + "MdiTabs";
    public const string GridStates = Prefix + "GridStates";
    public const string Theme = Prefix + "Theme";

    /// <summary>Çalışma bağlamı — seçili çalışma ŞUBESİ (Branch.Id). Per-user, cihazdan bağımsız.</summary>
    public const string WorkingBranch = Prefix + "WorkingBranch";
}
