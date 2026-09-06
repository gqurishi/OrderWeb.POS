using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>Mother-style session expired overlay used by both hosts.</summary>
public class SessionExpiredDialog : ContentView
{
    public SessionExpiredDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        var icon = new Border
        {
            WidthRequest = 74,
            HeightRequest = 74,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 37 },
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "!",
                TextColor = Colors.White,
                FontSize = 30,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        icon.Use(Border.BackgroundColorProperty, "OwWarning");

        var title = new Label
        {
            Text = "Session expired",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        title.Use(Label.TextColorProperty, "OwTextStrong");

        var message = new Label
        {
            Text = "Please sign in again to continue.",
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        message.Use(Label.TextColorProperty, "OwTextMuted");

        var login = new SharedButton { Text = "Sign in again" };
        login.Clicked += (_, _) => LoginAgainRequested?.Invoke(this, EventArgs.Empty);
        var logout = new SharedButton { Text = "Logout", Variant = ButtonVariant.Secondary };
        logout.Clicked += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        buttons.Add(logout);
        buttons.Add(login, 1);

        var panel = new Border
        {
            WidthRequest = 450,
            MaximumWidthRequest = 450,
            Padding = 24,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout { Spacing = 20, Children = { icon, title, message, buttons } }
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurface");
        panel.Use(Border.StrokeProperty, "OwWarningBorder");
        Content = new Grid { Padding = 24, Children = { panel } };
    }

    public event EventHandler? LoginAgainRequested;
    public event EventHandler? DismissRequested;
}
