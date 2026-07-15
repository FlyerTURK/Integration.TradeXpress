using Volo.Abp.Settings;

namespace Integration.TradeXpress.Settings;

public class TradeXpressUiSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                TradeXpressUiSettingNames.MdiTabs,
                defaultValue: "[]",
                isVisibleToClients: true
            ),
            new SettingDefinition(
                TradeXpressUiSettingNames.GridStates,
                defaultValue: "{}",
                isVisibleToClients: true
            ),
            new SettingDefinition(
                TradeXpressUiSettingNames.Theme,
                defaultValue: "",
                isVisibleToClients: true
            ),
            new SettingDefinition(
                TradeXpressUiSettingNames.WorkingBranch,
                defaultValue: "",
                isVisibleToClients: true
            ),
            new SettingDefinition(
                TradeXpressUiSettingNames.WorkingVault,
                defaultValue: "",
                isVisibleToClients: true
            )
        );
    }
}
