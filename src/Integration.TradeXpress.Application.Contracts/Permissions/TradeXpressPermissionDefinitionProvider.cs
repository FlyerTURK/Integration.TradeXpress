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

        var addOns = myGroup.AddPermission(
            TradeXpressPermissions.AddOns.Default, L("Permission:AddOns"));
        addOns.AddChild(TradeXpressPermissions.AddOns.Create, L("Permission:Create"));
        addOns.AddChild(TradeXpressPermissions.AddOns.Update, L("Permission:Update"));
        addOns.AddChild(TradeXpressPermissions.AddOns.Delete, L("Permission:Delete"));

        var variantTemplates = myGroup.AddPermission(
            TradeXpressPermissions.VariantTemplates.Default, L("Permission:VariantTemplates"));
        variantTemplates.AddChild(TradeXpressPermissions.VariantTemplates.Create, L("Permission:Create"));
        variantTemplates.AddChild(TradeXpressPermissions.VariantTemplates.Update, L("Permission:Update"));
        variantTemplates.AddChild(TradeXpressPermissions.VariantTemplates.Delete, L("Permission:Delete"));

        var shipmentTemplates = myGroup.AddPermission(
            TradeXpressPermissions.ShipmentTemplates.Default, L("Permission:ShipmentTemplates"));
        shipmentTemplates.AddChild(TradeXpressPermissions.ShipmentTemplates.Create, L("Permission:Create"));
        shipmentTemplates.AddChild(TradeXpressPermissions.ShipmentTemplates.Update, L("Permission:Update"));
        shipmentTemplates.AddChild(TradeXpressPermissions.ShipmentTemplates.Delete, L("Permission:Delete"));

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

        var metals = myGroup.AddPermission(
            TradeXpressPermissions.Metals.Default, L("Permission:Metals"));
        metals.AddChild(TradeXpressPermissions.Metals.Create, L("Permission:Create"));
        metals.AddChild(TradeXpressPermissions.Metals.Update, L("Permission:Update"));
        metals.AddChild(TradeXpressPermissions.Metals.Delete, L("Permission:Delete"));

        var scraps = myGroup.AddPermission(
            TradeXpressPermissions.Scraps.Default, L("Permission:Scraps"));
        scraps.AddChild(TradeXpressPermissions.Scraps.Create, L("Permission:Create"));
        scraps.AddChild(TradeXpressPermissions.Scraps.Update, L("Permission:Update"));
        scraps.AddChild(TradeXpressPermissions.Scraps.Delete, L("Permission:Delete"));

        var futures = myGroup.AddPermission(
            TradeXpressPermissions.Futures.Default, L("Permission:Futures"));
        futures.AddChild(TradeXpressPermissions.Futures.Create, L("Permission:Create"));
        futures.AddChild(TradeXpressPermissions.Futures.Update, L("Permission:Update"));
        futures.AddChild(TradeXpressPermissions.Futures.Delete, L("Permission:Delete"));

        var services = myGroup.AddPermission(
            TradeXpressPermissions.Services.Default, L("Permission:Services"));
        services.AddChild(TradeXpressPermissions.Services.Create, L("Permission:Create"));
        services.AddChild(TradeXpressPermissions.Services.Update, L("Permission:Update"));
        services.AddChild(TradeXpressPermissions.Services.Delete, L("Permission:Delete"));

        var salesChannels = myGroup.AddPermission(
            TradeXpressPermissions.SalesChannels.Default, L("Permission:SalesChannels"));
        salesChannels.AddChild(TradeXpressPermissions.SalesChannels.Create, L("Permission:Create"));
        salesChannels.AddChild(TradeXpressPermissions.SalesChannels.Update, L("Permission:Update"));
        salesChannels.AddChild(TradeXpressPermissions.SalesChannels.Delete, L("Permission:Delete"));

        var stones = myGroup.AddPermission(
            TradeXpressPermissions.Stones.Default, L("Permission:Stones"));
        stones.AddChild(TradeXpressPermissions.Stones.Create, L("Permission:Create"));
        stones.AddChild(TradeXpressPermissions.Stones.Update, L("Permission:Update"));
        stones.AddChild(TradeXpressPermissions.Stones.Delete, L("Permission:Delete"));

        var jewelries = myGroup.AddPermission(
            TradeXpressPermissions.Jewelries.Default, L("Permission:Jewelries"));
        jewelries.AddChild(TradeXpressPermissions.Jewelries.Create, L("Permission:Create"));
        jewelries.AddChild(TradeXpressPermissions.Jewelries.Update, L("Permission:Update"));
        jewelries.AddChild(TradeXpressPermissions.Jewelries.Delete, L("Permission:Delete"));

        var goods = myGroup.AddPermission(
            TradeXpressPermissions.Goods.Default, L("Permission:Goods"));
        goods.AddChild(TradeXpressPermissions.Goods.Create, L("Permission:Create"));
        goods.AddChild(TradeXpressPermissions.Goods.Update, L("Permission:Update"));
        goods.AddChild(TradeXpressPermissions.Goods.Delete, L("Permission:Delete"));

        var specialCodes = myGroup.AddPermission(
            TradeXpressPermissions.SpecialCodes.Default, L("Permission:SpecialCodes"));
        specialCodes.AddChild(TradeXpressPermissions.SpecialCodes.Create, L("Permission:Create"));
        specialCodes.AddChild(TradeXpressPermissions.SpecialCodes.Update, L("Permission:Update"));
        specialCodes.AddChild(TradeXpressPermissions.SpecialCodes.Delete, L("Permission:Delete"));

        var products = myGroup.AddPermission(
            TradeXpressPermissions.Products.Default, L("Permission:Products"));
        products.AddChild(TradeXpressPermissions.Products.Create, L("Permission:Create"));
        products.AddChild(TradeXpressPermissions.Products.Update, L("Permission:Update"));
        products.AddChild(TradeXpressPermissions.Products.Delete, L("Permission:Delete"));

        var substitutions = myGroup.AddPermission(
            TradeXpressPermissions.Substitutions.Default, L("Permission:Substitutions"));
        substitutions.AddChild(TradeXpressPermissions.Substitutions.Create, L("Permission:Create"));
        substitutions.AddChild(TradeXpressPermissions.Substitutions.Update, L("Permission:Update"));
        substitutions.AddChild(TradeXpressPermissions.Substitutions.Delete, L("Permission:Delete"));

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

        var confirmations = myGroup.AddPermission(
            TradeXpressPermissions.Confirmations.Default, L("Permission:Confirmations"));
        confirmations.AddChild(TradeXpressPermissions.Confirmations.Propose, L("Permission:Confirmations:Propose"));
        confirmations.AddChild(TradeXpressPermissions.Confirmations.Declare, L("Permission:Confirmations:Declare"));
        confirmations.AddChild(TradeXpressPermissions.Confirmations.Confirm, L("Permission:Confirmations:Confirm"));
        confirmations.AddChild(TradeXpressPermissions.Confirmations.Reject, L("Permission:Confirmations:Reject"));
        confirmations.AddChild(TradeXpressPermissions.Confirmations.View, L("Permission:Confirmations:View"));

        var reports = myGroup.AddPermission(
            TradeXpressPermissions.Reports.Default, L("Permission:Reports"));
        reports.AddChild(TradeXpressPermissions.Reports.Position, L("Permission:Position"));
        reports.AddChild(TradeXpressPermissions.Reports.BalanceSheet, L("Permission:BalanceSheet"));
        reports.AddChild(TradeXpressPermissions.Reports.Transactions, L("Permission:TransactionReport"));
        reports.AddChild(TradeXpressPermissions.Reports.Cash, L("Permission:CashReport"));
        reports.AddChild(TradeXpressPermissions.Reports.Metal, L("Permission:MetalReport"));
        reports.AddChild(TradeXpressPermissions.Reports.Scrap, L("Permission:ScrapReport"));
        reports.AddChild(TradeXpressPermissions.Reports.Good, L("Permission:GoodReport"));

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
        transactions.AddChild(TradeXpressPermissions.Transactions.Good, L("Permission:Transactions:Good"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Bullion, L("Permission:Transactions:Bullion"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Assay, L("Permission:Transactions:Assay"));
        transactions.AddChild(TradeXpressPermissions.Transactions.DebitNote, L("Permission:Transactions:DebitNote"));
        transactions.AddChild(TradeXpressPermissions.Transactions.Transfer, L("Permission:Transactions:Transfer"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<TradeXpressResource>(name);
    }
}
