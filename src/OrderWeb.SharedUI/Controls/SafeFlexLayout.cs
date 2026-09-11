namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// WinUI-safe FlexLayout child removal (Mother Live Order fix).
/// <see cref="FlexLayout"/>.Children.Clear() can crash with 0xC0000005 during transitions.
/// </summary>
public static class SafeFlexLayout
{
    public static void RemoveAllChildren(FlexLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        for (var index = layout.Children.Count - 1; index >= 0; index--)
        {
            layout.Children.RemoveAt(index);
        }
    }
}
