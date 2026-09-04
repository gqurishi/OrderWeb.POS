using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

public sealed class RoleOption
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string BadgeText { get; init; } = string.Empty;
    public Color BadgeColor { get; init; } = Color.FromArgb("#3B82F6");
}

/// <summary>
/// Shared role-selection presentation. Hosts supply options and handle RoleSelected.
/// </summary>
public sealed class RoleSelectionView : ContentView
{
    private readonly Label _title = new()
    {
        Text = "Select User Role",
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        HorizontalOptions = LayoutOptions.Center
    };

    private readonly VerticalStackLayout _options = new() { Spacing = 12 };

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(RoleSelectionView), "Select User Role",
        propertyChanged: (b, _, v) => ((RoleSelectionView)b)._title.Text = v?.ToString() ?? "Select User Role");

    public static readonly BindableProperty OptionsProperty = BindableProperty.Create(
        nameof(Options), typeof(IList<RoleOption>), typeof(RoleSelectionView),
        propertyChanged: (b, _, _) => ((RoleSelectionView)b).RebuildOptions());

    public static readonly BindableProperty IsOverlayProperty = BindableProperty.Create(
        nameof(IsOverlay), typeof(bool), typeof(RoleSelectionView), true,
        propertyChanged: (b, _, _) => ((RoleSelectionView)b).BuildShell());

    public RoleSelectionView()
    {
        _title.Use(Label.TextColorProperty, "PosTextStrong");
        BuildShell();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IList<RoleOption>? Options
    {
        get => (IList<RoleOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public bool IsOverlay
    {
        get => (bool)GetValue(IsOverlayProperty);
        set => SetValue(IsOverlayProperty, value);
    }

    public event EventHandler<RoleOption>? RoleSelected;
    public event EventHandler? Dismissed;

    public void ShowDefaultPosRoles()
    {
        Options = new List<RoleOption>
        {
            new() { Key = "Staff", Title = "Staff", Subtitle = "Clock in/out only", BadgeText = "S", BadgeColor = Color.FromArgb("#64748B") },
            new() { Key = "User", Title = "User", Subtitle = "Orders and tables", BadgeText = "U", BadgeColor = Color.FromArgb("#3B82F6") },
            new() { Key = "Manager", Title = "Manager", Subtitle = "Floor and staff tools", BadgeText = "M", BadgeColor = Color.FromArgb("#10B981") },
            new() { Key = "Admin", Title = "Admin", Subtitle = "Full system access", BadgeText = "A", BadgeColor = Color.FromArgb("#8B5CF6") }
        };
    }

    private void BuildShell()
    {
        var card = new Border
        {
            WidthRequest = 320,
            Padding = 24,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children = { _title, _options }
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");

        if (!IsOverlay)
        {
            Content = card;
            RebuildOptions();
            return;
        }

        var dismiss = new BoxView { Color = Color.FromArgb("#80000000") };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        dismiss.GestureRecognizers.Add(tap);

        Content = new Grid
        {
            Children = { dismiss, card }
        };
        RebuildOptions();
    }

    private void RebuildOptions()
    {
        _options.Children.Clear();
        if (Options is null)
        {
            return;
        }

        foreach (var option in Options)
        {
            var badge = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = option.BadgeColor,
                Content = new Label
                {
                    Text = string.IsNullOrWhiteSpace(option.BadgeText) ? option.Title[..1] : option.BadgeText,
                    TextColor = Colors.White,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };

            var title = new Label { Text = option.Title, FontSize = 16, FontAttributes = FontAttributes.Bold };
            title.Use(Label.TextColorProperty, "PosTextStrong");
            var subtitle = new Label { Text = option.Subtitle, FontSize = 13 };
            subtitle.Use(Label.TextColorProperty, "PosTextMuted");

            var row = new Border
            {
                HeightRequest = 60,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(16, 0)
            };
            row.Use(Border.BackgroundColorProperty, "PosSurfaceMuted");
            row.Use(Border.StrokeProperty, "PosBorder");

            var textStack = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Children = { title, subtitle }
            };
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(40),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            grid.Add(badge);
            grid.Add(textStack, 1);
            row.Content = grid;

            var selected = option;
            var selectTap = new TapGestureRecognizer();
            selectTap.Tapped += (_, _) => RoleSelected?.Invoke(this, selected);
            row.GestureRecognizers.Add(selectTap);
            _options.Children.Add(row);
        }
    }
}
