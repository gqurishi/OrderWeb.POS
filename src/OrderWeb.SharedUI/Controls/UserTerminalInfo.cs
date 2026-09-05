namespace OrderWeb.SharedUI.Controls;

public class UserTerminalInfo : ContentView
{
    private readonly Label _user;
    private readonly Label _terminal;
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(UserTerminalInfo), "No user", propertyChanged: (b, _, v) => ((UserTerminalInfo)b)._user.Text = v?.ToString());
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(UserTerminalInfo), "Terminal", propertyChanged: (b, _, v) => ((UserTerminalInfo)b)._terminal.Text = v?.ToString());
    public UserTerminalInfo()
    {
        var avatar = new Border { WidthRequest = 38, HeightRequest = 38, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 19 }, Content = new Label { Text = "●", TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } }; avatar.Use(Border.BackgroundColorProperty, "PosPrimary");
        _user = new Label { Text = "No user", FontSize = 14, FontAttributes = FontAttributes.Bold }; _user.Use(Label.TextColorProperty, "PosTextStrong");
        _terminal = new Label { Text = "Terminal", FontSize = 12 }; _terminal.Use(Label.TextColorProperty, "PosTextMuted");
        Content = new HorizontalStackLayout { Spacing = 10, Children = { avatar, new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { _user, _terminal } } } };
    }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string TerminalName { get => (string)GetValue(TerminalNameProperty); set => SetValue(TerminalNameProperty, value); }
}
