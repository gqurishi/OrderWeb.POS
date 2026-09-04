namespace POS_in_NET.Pages;

/// <summary>
/// Compatibility destination for the former Web Orders menu. It opens the
/// unified Order History screen with the Web tab selected.
/// </summary>
public sealed class WebOrderHistoryPage : OrderHistoryPage
{
    public WebOrderHistoryPage() : base(true)
    {
    }
}
