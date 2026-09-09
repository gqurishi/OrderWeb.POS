using OrderWeb.SharedUI.Controls;

namespace POS_in_NET.Pages;

/// <summary>
/// Phase 1 preview host for <see cref="ChefLoaderView"/> (sizes + modes).
/// Open via sidebar "Chef Loader" (Admin) or Shell route //chefloader.
/// </summary>
public class ChefLoaderSandboxPage : ContentPage
{
    private readonly ChefLoaderView _preview;
    private readonly ChefLoaderView _inlineSample;
    private readonly Border _stage;
    private readonly Label _status;
    private ChefLoaderSize _size = ChefLoaderSize.Md;
    private ChefLoaderMode _mode = ChefLoaderMode.Overlay;

    public ChefLoaderSandboxPage()
    {
        Title = "Chef Loader";
        BackgroundColor = Color.FromArgb("#F6F8FC");

        _preview = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Overlay,
            Size = ChefLoaderSize.Md,
            Message = "Cooking up your data…",
            DelayMilliseconds = 300,
            IsLoading = true
        };

        _inlineSample = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Syncing menu",
            DelayMilliseconds = 0,
            IsLoading = true,
            HorizontalOptions = LayoutOptions.Center
        };

        _stage = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            HeightRequest = 360,
            Padding = 0,
            Content = new Grid
            {
                Children =
                {
                    new Label
                    {
                        Text = "Stage (order / sync surface)",
                        TextColor = Color.FromArgb("#94A3B8"),
                        FontSize = 14,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    },
                    _preview
                }
            }
        };

        _status = new Label
        {
            Text = "Preview: Overlay · Md · loading",
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var density14 = BuildDensityCard("14\" density", 1280, ChefLoaderSize.Md);
        var density155 = BuildDensityCard("15.5\" density", 1440, ChefLoaderSize.Lg);
        Grid.SetColumn(density155, 1);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Chef Loader sandbox",
                        FontSize = 26,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1E2A4A")
                    },
                    new Label
                    {
                        Text = "SharedUI preloader for Mother + Client. Toggle size/mode and simulate busy.",
                        FontSize = 14,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    BuildToolbar(),
                    _stage,
                    _status,
                    new Label
                    {
                        Text = "Inline sample",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1E2A4A"),
                        Margin = new Thickness(0, 8, 0, 0)
                    },
                    new Border
                    {
                        Padding = 16,
                        StrokeThickness = 1,
                        Stroke = Color.FromArgb("#E2E8F0"),
                        BackgroundColor = Colors.White,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                        Content = _inlineSample
                    },
                    new Label
                    {
                        Text = "Density check",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1E2A4A"),
                        Margin = new Thickness(0, 8, 0, 0)
                    },
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Star)
                        },
                        ColumnSpacing = 12,
                        Children = { density14, density155 }
                    }
                }
            }
        };

        UpdateStatus();
    }

    private View BuildToolbar()
    {
        var row = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row
        };

        void AddChip(string text, Action action)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                Padding = new Thickness(14, 8),
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = 10,
                BackgroundColor = Color.FromArgb("#EEF2FF"),
                TextColor = Color.FromArgb("#1E2A4A")
            };
            button.Clicked += (_, _) => action();
            row.Children.Add(button);
        }

        AddChip("Size Sm", () => SetSize(ChefLoaderSize.Sm));
        AddChip("Size Md", () => SetSize(ChefLoaderSize.Md));
        AddChip("Size Lg", () => SetSize(ChefLoaderSize.Lg));
        AddChip("Mode Inline", () => SetMode(ChefLoaderMode.Inline));
        AddChip("Mode Overlay", () => SetMode(ChefLoaderMode.Overlay));
        AddChip("Mode Fullscreen", () => SetMode(ChefLoaderMode.Fullscreen));
        AddChip("Start loading", () =>
        {
            _preview.IsLoading = true;
            _inlineSample.IsLoading = true;
            UpdateStatus();
        });
        AddChip("Stop loading", () =>
        {
            _preview.IsLoading = false;
            _inlineSample.IsLoading = false;
            UpdateStatus();
        });
        AddChip("Simulate 2s busy", async () =>
        {
            _preview.DelayMilliseconds = 300;
            _preview.IsLoading = true;
            UpdateStatus();
            await Task.Delay(2000);
            _preview.IsLoading = false;
            UpdateStatus();
        });

        return row;
    }

    private static View BuildDensityCard(string title, double widthHint, ChefLoaderSize size)
    {
        var loader = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Overlay,
            Size = size,
            Message = "Cooking up your data…",
            DelayMilliseconds = 0,
            IsLoading = true
        };

        return new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = 12,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = $"{title} (~{widthHint}px)",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#334155")
                    },
                    new Grid
                    {
                        HeightRequest = 200,
                        Children =
                        {
                            new BoxView { Color = Color.FromArgb("#F8FAFC") },
                            loader
                        }
                    }
                }
            }
        };
    }

    private void SetSize(ChefLoaderSize size)
    {
        _size = size;
        _preview.Size = size;
        UpdateStatus();
    }

    private void SetMode(ChefLoaderMode mode)
    {
        _mode = mode;
        _preview.Mode = mode;
        _stage.HeightRequest = mode switch
        {
            ChefLoaderMode.Fullscreen => 420,
            ChefLoaderMode.Inline => 220,
            _ => 360
        };
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _status.Text = $"Preview: {_mode} · {_size} · {(_preview.IsLoading ? "loading" : "idle")}";
    }
}
