using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>Mother-style capability-driven dashboard tile grid.</summary>
public class DashboardView : ContentView
{
    private DashboardViewModel? _viewModel;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _offline;
    private readonly Grid _tileGrid;

    public DashboardView()
    {
        _title = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "InterBold",
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#111827")
        };
        _subtitle = new Label { FontSize = 16, HorizontalOptions = LayoutOptions.Center };
        _subtitle.Use(Label.TextColorProperty, "OwTextMuted");
        _offline = new Label
        {
            Text = "Mother offline — showing cached options",
            IsVisible = false,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        };
        _offline.Use(Label.TextColorProperty, "OwWarningText");

        _tileGrid = new Grid { RowSpacing = 38, ColumnSpacing = 80, HorizontalOptions = LayoutOptions.Center };

        var stack = new VerticalStackLayout
        {
            Spacing = 18,
            Padding = new Thickness(24, 30),
            HorizontalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 900,
            VerticalOptions = LayoutOptions.Center,
            Children = { _title, _subtitle, _offline, _tileGrid }
        };

        Content = new ScrollView { BackgroundColor = Colors.White, Content = stack };
    }

    public DashboardViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel == value) return;
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnVmChanged;
            _viewModel = value;
            BindingContext = value;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnVmChanged;
                Rebuild();
            }
        }
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_viewModel is null) return;
        _title.Text = _viewModel.Title;
        _subtitle.Text = _viewModel.Subtitle;
        _subtitle.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.Subtitle);
        _offline.IsVisible = _viewModel.IsOffline;

        _tileGrid.Children.Clear();
        _tileGrid.RowDefinitions.Clear();
        _tileGrid.ColumnDefinitions.Clear();

        var tiles = _viewModel.Tiles.ToList();
        if (tiles.Count == 0) return;

        var columns = Math.Min(3, tiles.Count);
        var rows = (int)Math.Ceiling(tiles.Count / 3d);
        for (var c = 0; c < columns; c++) _tileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows; r++) _tileGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < tiles.Count; i++)
        {
            var model = tiles[i];
            var row = i / 3;
            var col = i % 3;
            if (columns == 3 && tiles.Count - row * 3 == 1) col = 1;

            var tile = new DashboardTile
            {
                Title = model.Title,
                Subtitle = model.Subtitle ?? string.Empty,
                IconSource = model.Icon,
                Badge = model.Badge ?? string.Empty,
                IsTileEnabled = model.IsEnabled,
                IsLoading = model.IsLoading,
                Command = _viewModel.SelectTileCommand,
                CommandParameter = model
            };
            // Mother User dashboard: large icons + InterBold labels, light card chrome.
            if (tile.Content is Border card)
            {
                card.StrokeThickness = 1;
                card.Stroke = Color.FromArgb("#E5E7EB");
                card.BackgroundColor = Colors.White;
                card.Shadow = null;
                card.Padding = 14;
                if (card.StrokeShape is Microsoft.Maui.Controls.Shapes.RoundRectangle rr)
                {
                    rr.CornerRadius = 20;
                }

                if (card.Content is Grid { Children: var children })
                {
                    foreach (var child in children)
                    {
                        if (child is VerticalStackLayout layout)
                        {
                            layout.Spacing = 8;
                            if (layout.Children.OfType<Image>().FirstOrDefault() is { } image)
                            {
                                image.WidthRequest = 150;
                                image.HeightRequest = 150;
                            }

                            if (layout.Children.OfType<Label>().FirstOrDefault() is { } label)
                            {
                                label.FontSize = 21;
                                label.FontFamily = "InterBold";
                                label.FontAttributes = FontAttributes.Bold;
                                label.TextColor = Color.FromArgb("#111827");
                            }
                        }
                    }
                }
            }

            _tileGrid.Add(tile, col, row);
        }
    }
}
