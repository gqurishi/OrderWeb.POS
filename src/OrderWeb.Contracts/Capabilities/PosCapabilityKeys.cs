namespace OrderWeb.Contracts.Capabilities;

public static class PosCapabilityKeys
{
    public const string ViewDashboard = "pos.dashboard.view";
    public const string ViewCustomers = "pos.customers.view";
    public const string CreateOrders = "pos.orders.create";
    public const string SubmitOrders = "pos.orders.submit";
    public const string TakeOrders = "pos.orders.take";
    public const string TakePayments = "pos.payments.take";
    public const string OpenTables = "pos.tables.open";
    public const string TransferTables = "pos.tables.transfer";
    public const string ApplyDiscount = "pos.discounts.apply";
    public const string VoidItems = "pos.items.void";
    public const string VoidOrders = "pos.orders.void";
    public const string Refund = "pos.payments.refund";
    public const string ManageCustomers = "pos.customers.manage";
    public const string ViewReports = "pos.reports.view";
    public const string ManageUsers = "pos.users.manage";
    public const string EditMenu = "pos.menu.edit";
    public const string ConfigurePrinters = "pos.printers.configure";
    public const string PrintReceipts = "pos.receipts.print";
    public const string ReprintReceipts = "pos.receipts.reprint";
    public const string OpenCashDrawer = "pos.cashdrawer.open";
    public const string EditTables = "pos.tables.edit";
    public const string AccessSettings = "pos.settings.access";
    public const string ApproveManagerAction = "pos.manager.approve";
    public const string AccessAdmin = "pos.admin.access";
}

public sealed record PosCapability(string Key, bool IsAvailable);
