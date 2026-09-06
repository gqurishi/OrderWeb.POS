namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared Mother-style frame for manager pages. Hosts retain authorization and
/// service calls; this control contains presentation only.
/// </summary>
public sealed class ManagerPageFrame : ContentView
{
    private readonly ApplicationHeader _header;
    private readonly ContentView _body;
    public ManagerPageFrame()
    {
        _header = new ApplicationHeader();
        _body = new ContentView();
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        grid.Use(Grid.BackgroundColorProperty, "OwBackground");
        grid.Add(_header);
        grid.Add(new ScrollView { Content = _body, Padding = new Thickness(24) }, 0, 1);
        Content = grid;
    }
    public string Title { get => _header.Title; set => _header.Title = value; }
    public string UserName { get => _header.UserName; set => _header.UserName = value; }
    public string TerminalName { get => _header.TerminalName; set => _header.TerminalName = value; }
    public string ConnectionStatus { get => _header.ConnectionStatus; set => _header.ConnectionStatus = value; }
    public View? Body { get => _body.Content; set => _body.Content = value; }
    public event EventHandler? LogoutClicked { add => _header.LogoutClicked += value; remove => _header.LogoutClicked -= value; }
    public event EventHandler? BackRequested { add => _header.BackRequested += value; remove => _header.BackRequested -= value; }
}

/// <summary>Shared manager card with consistent surface, border, and spacing.</summary>
public sealed class ManagerPanel : ContentView
{
    private readonly Label _title;
    private readonly ContentView _content;
    public ManagerPanel()
    {
        _title = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold };
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _content = new ContentView();
        var stack = new VerticalStackLayout { Spacing = 14, Children = { _title, _content } };
        var border = new Border { Padding = 20, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = stack };
        border.Use(Border.BackgroundColorProperty, "OwSurface"); border.Use(Border.StrokeProperty, "OwBorder");
        Content = border;
    }
    public string Title { get => _title.Text; set => _title.Text = value; }
    public View? PanelContent { get => _content.Content; set => _content.Content = value; }
}
