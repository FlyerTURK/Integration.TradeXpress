namespace Integration.TradeXpress.Permissions;

public static class TradeXpressPermissions
{
    public const string GroupName = "TradeXpress";

    public static class CurrencyUnits
    {
        public const string Default = GroupName + ".CurrencyUnits";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class CurrencyUnitMargins
    {
        public const string Default = GroupName + ".CurrencyUnitMargins";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Parities
    {
        public const string Default = GroupName + ".Parities";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Companies
    {
        public const string Default = GroupName + ".Companies";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Countries
    {
        public const string Default = GroupName + ".Countries";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Branches
    {
        public const string Default = GroupName + ".Branches";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Vaults
    {
        public const string Default = GroupName + ".Vaults";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Cashes
    {
        public const string Default = GroupName + ".Cashes";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Accounts
    {
        public const string Default = GroupName + ".Accounts";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class SubAccounts
    {
        public const string Default = GroupName + ".SubAccounts";
        public const string Create  = Default + ".Create";
        public const string Update  = Default + ".Update";
        public const string Delete  = Default + ".Delete";
    }

    public static class Reports
    {
        public const string Default  = GroupName + ".Reports";
        public const string Position = Default + ".Position";
    }
}
