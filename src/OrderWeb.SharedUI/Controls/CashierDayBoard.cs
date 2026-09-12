namespace OrderWeb.SharedUI.Controls;

/// <summary>Host-formatted cashier day tiles. Hosts own the numbers; SharedUI only draws them.</summary>
public sealed class CashierDayBoard
{
    public string DateLine { get; init; } = "Live day summary";
    public string UpdatedText { get; init; } = "Updated --:--:--";
    public string Gross { get; init; } = "£0.00";
    public string Net { get; init; } = "£0.00";
    public string Vat { get; init; } = "£0.00";
    public string Cash { get; init; } = "£0.00";
    public string Card { get; init; } = "£0.00";
    public string Tips { get; init; } = "£0.00";
    public string PosSales { get; init; } = "£0.00 (0)";
    public string OnlineSales { get; init; } = "£0.00 (0)";
    public string PettyCashOut { get; init; } = "£0.00";
    public string ExpectedCash { get; init; } = "£0.00";
    public string WebOrders { get; init; } = "0";
    public string VsYesterday { get; init; } = "--";
}
