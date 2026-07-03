using Integration.TradeXpress.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Integration.TradeXpress.Permissions;

public class TradeXpressPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(TradeXpressPermissions.GroupName, L("Permission:TradeXpress"));

        var currencyUnits = myGroup.AddPermission(
            TradeXpressPermissions.CurrencyUnits.Default, L("Permission:CurrencyUnits"));
        currencyUnits.AddChild(TradeXpressPermissions.CurrencyUnits.Create, L("Permission:Create"));
        currencyUnits.AddChild(TradeXpressPermissions.CurrencyUnits.Update, L("Permission:Update"));
        currencyUnits.AddChild(TradeXpressPermissions.CurrencyUnits.Delete, L("Permission:Delete"));

        var currencyUnitMargins = myGroup.AddPermission(
            TradeXpressPermissions.CurrencyUnitMargins.Default, L("Permission:CurrencyUnitMargins"));
        currencyUnitMargins.AddChild(TradeXpressPermissions.CurrencyUnitMargins.Create, L("Permission:Create"));
        currencyUnitMargins.AddChild(TradeXpressPermissions.CurrencyUnitMargins.Update, L("Permission:Update"));
        currencyUnitMargins.AddChild(TradeXpressPermissions.CurrencyUnitMargins.Delete, L("Permission:Delete"));

        var parities = myGroup.AddPermission(
            TradeXpressPermissions.Parities.Default, L("Permission:Parities"));
        parities.AddChild(TradeXpressPermissions.Parities.Create, L("Permission:Create"));
        parities.AddChild(TradeXpressPermissions.Parities.Update, L("Permission:Update"));
        parities.AddChild(TradeXpressPermissions.Parities.Delete, L("Permission:Delete"));

        var companies = myGroup.AddPermission(
            TradeXpressPermissions.Companies.Default, L("Permission:Companies"));
        companies.AddChild(TradeXpressPermissions.Companies.Create, L("Permission:Create"));
        companies.AddChild(TradeXpressPermissions.Companies.Update, L("Permission:Update"));
        companies.AddChild(TradeXpressPermissions.Companies.Delete, L("Permission:Delete"));

        var countries = myGroup.AddPermission(
            TradeXpressPermissions.Countries.Default, L("Permission:Countries"));
        countries.AddChild(TradeXpressPermissions.Countries.Create, L("Permission:Create"));
        countries.AddChild(TradeXpressPermissions.Countries.Update, L("Permission:Update"));
        countries.AddChild(TradeXpressPermissions.Countries.Delete, L("Permission:Delete"));

        var branches = myGroup.AddPermission(
            TradeXpressPermissions.Branches.Default, L("Permission:Branches"));
        branches.AddChild(TradeXpressPermissions.Branches.Create, L("Permission:Create"));
        branches.AddChild(TradeXpressPermissions.Branches.Update, L("Permission:Update"));
        branches.AddChild(TradeXpressPermissions.Branches.Delete, L("Permission:Delete"));

        var vaults = myGroup.AddPermission(
            TradeXpressPermissions.Vaults.Default, L("Permission:Vaults"));
        vaults.AddChild(TradeXpressPermissions.Vaults.Create, L("Permission:Create"));
        vaults.AddChild(TradeXpressPermissions.Vaults.Update, L("Permission:Update"));
        vaults.AddChild(TradeXpressPermissions.Vaults.Delete, L("Permission:Delete"));

        var assayOffices = myGroup.AddPermission(
            TradeXpressPermissions.AssayOffices.Default, L("Permission:AssayOffices"));
        assayOffices.AddChild(TradeXpressPermissions.AssayOffices.Create, L("Permission:Create"));
        assayOffices.AddChild(TradeXpressPermissions.AssayOffices.Update, L("Permission:Update"));
        assayOffices.AddChild(TradeXpressPermissions.AssayOffices.Delete, L("Permission:Delete"));

        var appointments = myGroup.AddPermission(
            TradeXpressPermissions.Appointments.Default, L("Permission:Appointments"));
        appointments.AddChild(TradeXpressPermissions.Appointments.Create, L("Permission:Create"));
        appointments.AddChild(TradeXpressPermissions.Appointments.Update, L("Permission:Update"));
        appointments.AddChild(TradeXpressPermissions.Appointments.Delete, L("Permission:Delete"));

        var cashes = myGroup.AddPermission(
            TradeXpressPermissions.Cashes.Default, L("Permission:Cashes"));
        cashes.AddChild(TradeXpressPermissions.Cashes.Create, L("Permission:Create"));
        cashes.AddChild(TradeXpressPermissions.Cashes.Update, L("Permission:Update"));
        cashes.AddChild(TradeXpressPermissions.Cashes.Delete, L("Permission:Delete"));

        var accounts = myGroup.AddPermission(
            TradeXpressPermissions.Accounts.Default, L("Permission:Accounts"));
        accounts.AddChild(TradeXpressPermissions.Accounts.Create, L("Permission:Create"));
        accounts.AddChild(TradeXpressPermissions.Accounts.Update, L("Permission:Update"));
        accounts.AddChild(TradeXpressPermissions.Accounts.Delete, L("Permission:Delete"));

        var subAccounts = myGroup.AddPermission(
            TradeXpressPermissions.SubAccounts.Default, L("Permission:SubAccounts"));
        subAccounts.AddChild(TradeXpressPermissions.SubAccounts.Create, L("Permission:Create"));
        subAccounts.AddChild(TradeXpressPermissions.SubAccounts.Update, L("Permission:Update"));
        subAccounts.AddChild(TradeXpressPermissions.SubAccounts.Delete, L("Permission:Delete"));

        var reports = myGroup.AddPermission(
            TradeXpressPermissions.Reports.Default, L("Permission:Reports"));
        reports.AddChild(TradeXpressPermissions.Reports.Position, L("Permission:Position"));
        reports.AddChild(TradeXpressPermissions.Reports.BalanceSheet, L("Permission:BalanceSheet"));
        reports.AddChild(TradeXpressPermissions.Reports.Transactions, L("Permission:TransactionReport"));

        var transactions = myGroup.AddPermission(
            TradeXpressPermissions.Transactions.Default, L("Permission:Transactions"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Metal, L("Permission:Transactions:Metal"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Scrap, L("Permission:Transactions:Scrap"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Cash, L("Permission:Transactions:Cash"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Convert, L("Permission:Transactions:Convert"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Service, L("Permission:Transactions:Service"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Future, L("Permission:Transactions:Future"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Stone, L("Permission:Transactions:Stone"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Jewelry, L("Permission:Transactions:Jewelry"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Bullion, L("Permission:Transactions:Bullion"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<TradeXpressResource>(name);
    }
}
