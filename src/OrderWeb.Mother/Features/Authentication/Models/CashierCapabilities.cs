namespace POS_in_NET.Models;

/// <summary>
/// Explicit, server-enforced permissions for the Cashier reports-and-till role.
/// These names are intentionally independent of page routes and UI controls.
/// </summary>
public static class CashierCapabilities
{
    public const string ViewDaily = "reports.view_daily";
    public const string ViewYesterday = "reports.view_yesterday";
    public const string ViewFull = "reports.view_full";
    public const string PreviewZ = "reports.preview_z";
    public const string PrintZ = "reports.print_z";
    public const string ReprintZ = "reports.reprint_z";
    public const string Export = "reports.export";
    public const string ViewVoids = "reports.view_voids";
    public const string ViewDiscounts = "reports.view_discounts";
    public const string OpenDrawer = "cashdrawer.open";
    public const string ViewDrawer = "cashdrawer.view";
    public const string ReconcileDrawer = "cashdrawer.reconcile";
    public const string ViewPrintHistory = "reports.view_print_history";

    public static IReadOnlySet<string> CashierDefaults { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ViewDaily, ViewYesterday, ViewFull, PreviewZ, PrintZ, ReprintZ, Export,
        ViewVoids, ViewDiscounts, OpenDrawer, ViewDrawer, ReconcileDrawer, ViewPrintHistory
    };

    public static bool IsGrantedTo(UserRole role, string capability) =>
        role == UserRole.Admin ||
        (role == UserRole.Cashier && CashierDefaults.Contains(capability));
}
