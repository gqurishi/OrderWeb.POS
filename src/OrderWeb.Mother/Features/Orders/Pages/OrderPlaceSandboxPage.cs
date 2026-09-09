using OrderWeb.SharedUI.Views;

namespace POS_in_NET.Pages;

/// <summary>
/// Phase 1 sandbox: preview SharedUI Order Place controls at POS densities.
/// Open via Shell route <c>orderplacesandbox</c> (flyout hidden). No live order logic.
/// </summary>
public sealed class OrderPlaceSandboxPage : ContentPage
{
    public OrderPlaceSandboxPage()
    {
        Title = "Order Place Sandbox";
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#F8FAFC");

        var back = new Button
        {
            Text = "Back",
            BackgroundColor = Color.FromArgb("#E2E8F0"),
            TextColor = Color.FromArgb("#334155"),
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            WidthRequest = 100,
            CornerRadius = 10,
            HorizontalOptions = LayoutOptions.Start,
            Margin = new Thickness(12, 8, 12, 0)
        };
        back.Clicked += async (_, _) =>
        {
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync(false);
            }
            else
            {
                await Shell.Current.GoToAsync("//managerdashboard");
            }
        };

        var hint = new Label
        {
            Text = "Phase 1 component sandbox — resize the window to check 14″ vs 15.5″ column density. Not wired to real orders.",
            FontSize = 12,
            TextColor = Color.FromArgb("#64748B"),
            Margin = new Thickness(12, 4, 12, 8)
        };

        var sandbox = new OrderPlaceSandboxView();
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        root.Add(back);
        root.Add(hint, 0, 1);
        root.Add(sandbox, 0, 2);
        Content = root;
    }
}
