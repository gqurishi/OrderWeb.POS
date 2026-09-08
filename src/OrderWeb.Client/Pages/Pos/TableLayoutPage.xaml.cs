using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace OrderWeb.Client.Pages.Pos;

public partial class TableLayoutPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private IReadOnlyList<CachedFloor> _floors = Array.Empty<CachedFloor>();
    private CachedFloor? _selectedFloor;
    private CachedTable? _selectedTable;
    private Label? _dateLabel;
    private Label? _timeLabel;
    private readonly IDispatcherTimer _clockTimer;
    private bool _usingFallbackLayout;

    public TableLayoutPage()
    {
        InitializeComponent();
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        _ = LoadAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task LoadAsync(int? floorId)
    {
        await _cache.InitializeAsync();
        _floors = await _cache.GetFloorsWithTablesAsync();
        _usingFallbackLayout = !_floors.Any(floor => floor.Tables.Count > 0);
        _selectedFloor = floorId.HasValue
            ? _floors.FirstOrDefault(floor => floor.Id == floorId.Value) ?? _floors.FirstOrDefault()
            : _selectedFloor == null
                ? _floors.FirstOrDefault(floor => floor.Tables.Count > 0) ?? _floors.FirstOrDefault()
                : _floors.FirstOrDefault(floor => floor.Id == _selectedFloor.Id && floor.Tables.Count > 0)
                    ?? _floors.FirstOrDefault(floor => floor.Tables.Count > 0)
                    ?? _floors.FirstOrDefault();

        BuildPage();
    }

    private void BuildPage()
    {
        Root.Children.Clear();

        var page = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(150),
                new RowDefinition(76),
                new RowDefinition(GridLength.Star)
            },
            BackgroundColor = Colors.White
        };

        page.Children.Add(RestaurantHeader());

        var floorRow = new Grid
        {
            Padding = new Thickness(20, 15),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(260)
            },
            BackgroundColor = Colors.White
        };

        var floorButtons = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        foreach (var floor in _floors)
        {
            floorButtons.Children.Add(FloorButton(floor));
        }

        floorRow.Children.Add(floorButtons);
        floorRow.Children.Add(new Label
        {
            Text = $"Synced {DateTime.Now:HH:mm:ss}",
            FontFamily = "InterMedium",
            FontSize = 12,
            TextColor = Color.FromArgb("#059669"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        });
        SetColumn(floorRow.Children[1], 1);

        page.Children.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Content = floorRow
        });
        SetRow(page.Children[1], 1);

        var tableCanvas = new AbsoluteLayout { BackgroundColor = Colors.White };
        var selectedTables = (_selectedFloor?.Tables ?? Array.Empty<CachedTable>()).ToList();
        if (selectedTables.Count == 0)
        {
            tableCanvas.Children.Add(new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Image { Source = "table_1.png", WidthRequest = 60, HeightRequest = 60, Opacity = 0.35, HorizontalOptions = LayoutOptions.Center },
                    new Label
                    {
                        Text = _usingFallbackLayout
                            ? "No tables from Mother POS. Use Update All after login."
                            : "No Tables on This Floor",
                        FontSize = 18,
                        FontFamily = "OpenSansSemibold",
                        TextColor = Color.FromArgb("#9CA3AF"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            });
            SetAbsoluteLayout(tableCanvas.Children[0], new Rect(0.5, 0.5, -1, -1), AbsoluteLayoutFlags.PositionProportional);
        }
        else
        {
            for (var index = 0; index < selectedTables.Count; index++)
            {
                var table = selectedTables[index];
                var card = TableCard(table);
                var (x, y) = TablePosition(table, index);
                tableCanvas.Children.Add(card);
                SetAbsoluteLayout(card, new Rect(x, y, 148, 148), AbsoluteLayoutFlags.None);
            }
        }

        page.Children.Add(tableCanvas);
        SetRow(tableCanvas, 2);
        Root.Children.Add(page);
    }

    private View RestaurantHeader()
    {
        var menuButton = new ImageButton
        {
            Source = "mian.png",
            WidthRequest = 35,
            HeightRequest = 35,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };
        menuButton.Clicked += async (_, _) => await Navigation.PopAsync(false);

        var logoutButton = new ImageButton
        {
            Source = "outred.png",
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            Margin = new Thickness(0, 10, 0, 0),
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End
        };
        logoutButton.Clicked += async (_, _) => await DisplayAlert("Restaurant POS", "Logout returns to login.", "OK");

        _dateLabel = new Label
        {
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.End
        };
        _timeLabel = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0369A1"),
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.End
        };
        UpdateClock();

        var header = new Border
        {
            BackgroundColor = Color.FromArgb("#F6F9FC"),
            StrokeThickness = 0,
            Content = new Grid
            {
                Padding = new Thickness(20, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(120),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(270)
                },
                Children =
                {
                    menuButton,
                    new Label
                    {
                        Text = "Restaurant Layout",
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1F2937"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center
                    },
                    new VerticalStackLayout
                    {
                        Spacing = 3,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.End,
                        Children =
                        {
                            _dateLabel,
                            _timeLabel,
                            logoutButton
                        }
                    }
                }
            }
        };

        if (header.Content is Grid grid)
        {
            SetColumn(grid.Children[1], 1);
            SetColumn(grid.Children[2], 2);
        }

        return header;
    }

    private Button FloorButton(CachedFloor floor)
    {
        var selected = floor.Id == _selectedFloor?.Id;
        var button = new Button
        {
            Text = $"{floor.Name} ({floor.Tables.Count})",
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            BackgroundColor = Color.FromArgb(selected ? "#3B82F6" : "#F6F9FC"),
            TextColor = selected ? Colors.White : Color.FromArgb("#1F2937"),
            CornerRadius = 22,
            HeightRequest = 46,
            Padding = new Thickness(22, 0)
        };
        button.Clicked += async (_, _) => await LoadAsync(floor.Id);
        return button;
    }

    private View TableCard(CachedTable table)
    {
        var (bg, border, text) = TableColors(table);
        var card = new Border
        {
            WidthRequest = 148,
            HeightRequest = 148,
            Padding = 14,
            Stroke = border,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = bg,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.14f, Radius = 8, Offset = new Point(0, 3) },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = TableAssetFor(table), WidthRequest = 58, HeightRequest = 58, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = table.TableNumber, FontFamily = "InterBold", FontSize = 20, TextColor = text, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "●", FontFamily = "OpenSansSemibold", FontSize = 18, TextColor = border, HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ShowGuestDialogAsync(table);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private async Task ShowGuestDialogAsync(CachedTable table)
    {
        _selectedTable = table;
        if (_usingFallbackLayout)
        {
            await DisplayAlert("Restaurant Layout", "No Mother POS table cache is available on this Client POS. Run bootstrap or Update All from Mother POS before opening a table.", "OK");
            return;
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId))
        {
            await Navigation.PushAsync(new OrderPage(table, Math.Max(table.Covers, 1)), false);
            return;
        }

        var overlay = new Grid { BackgroundColor = Color.FromRgba(0, 0, 0, 0.48), InputTransparent = false };
        var numbers = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            Padding = 20
        };

        for (var i = 1; i <= 12; i++)
        {
            var covers = i;
            var button = new Button
            {
                Text = i.ToString(),
                FontSize = 18,
                FontFamily = "OpenSansSemibold",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#1F2937"),
                CornerRadius = 10,
                HeightRequest = 56,
                Padding = 0
            };
            button.Clicked += async (_, _) =>
            {
                Root.Children.Remove(overlay);
                await OpenTableOrderAsync(covers);
            };
            numbers.Children.Add(button);
            SetRow(button, (i - 1) / 4);
            SetColumn(button, (i - 1) % 4);
        }

        var close = new Button
        {
            Text = "X",
            FontSize = 16,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb("#DC2626"),
            BackgroundColor = Color.FromArgb("#FEE2E2"),
            CornerRadius = 18,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = 0
        };
        close.Clicked += (_, _) => Root.Children.Remove(overlay);

        var otherGuestEntry = new Entry
        {
            Placeholder = "Other number...",
            FontSize = 15,
            FontFamily = "OpenSansRegular",
            BackgroundColor = Colors.White,
            Keyboard = Keyboard.Numeric,
            HeightRequest = 48,
            TextColor = Color.FromArgb("#1F2937"),
            PlaceholderColor = Color.FromArgb("#9CA3AF")
        };

        var goButton = new Button
        {
            Text = "Go",
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            CornerRadius = 8,
            WidthRequest = 64,
            HeightRequest = 48
        };
        goButton.Clicked += async (_, _) =>
        {
            var covers = int.TryParse(otherGuestEntry.Text, out var enteredGuests) && enteredGuests > 0 ? enteredGuests : 4;
            Root.Children.Remove(overlay);
            await OpenTableOrderAsync(covers);
        };

        var footer = new Grid
        {
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            Padding = new Thickness(20, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(64)
            },
            ColumnSpacing = 12,
            Children =
            {
                otherGuestEntry,
                goButton
            }
        };

        var modal = new Border
        {
            WidthRequest = 380,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.25f, Radius = 24, Offset = new Point(0, 8) },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Grid
                    {
                        Padding = new Thickness(24, 20),
                        BackgroundColor = Color.FromArgb("#F9FAFB"),
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                        Children =
                        {
                            new VerticalStackLayout
                            {
                                Spacing = 4,
                                Children =
                                {
                                    new Label { Text = $"Table {table.TableNumber}", FontSize = 20, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb("#1F2937") },
                                    new Label { Text = "How many guests?", FontSize = 14, FontFamily = "OpenSansRegular", TextColor = Color.FromArgb("#6B7280") }
                                }
                            },
                            close
                        }
                    },
                    new Grid { BackgroundColor = Colors.White, Children = { numbers } },
                    footer
                }
            }
        };
        SetColumn(close, 1);
        SetRow(((Grid)modal.Content).Children[1], 1);
        SetColumn(goButton, 1);
        SetRow(footer, 2);
        overlay.Children.Add(modal);
        Root.Children.Add(overlay);
    }

    private async Task OpenTableOrderAsync(int covers)
    {
        if (_selectedTable == null)
        {
            return;
        }

        if (_usingFallbackLayout)
        {
            await DisplayAlert("Restaurant Layout", "No Mother POS table cache is available on this Client POS. Run bootstrap or Update All from Mother POS before opening a table.", "OK");
            return;
        }

        await Navigation.PushAsync(new OrderPage(_selectedTable, covers), false);
    }

    private static (Color Bg, Color Border, Color Text) TableColors(CachedTable table)
    {
        if (table.SessionStatus?.Equals("Payment", StringComparison.OrdinalIgnoreCase) == true
            || table.SessionStatus?.Equals("Cleaning", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb("#DC2626"));
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId) || table.Status.Equals("Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb("#DC2626"));
        }

        if (table.Status.Equals("Reserved", StringComparison.OrdinalIgnoreCase))
        {
            return (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FCA5A5"), Color.FromArgb("#DC2626"));
        }

        return (Color.FromArgb("#ECFDF5"), Color.FromArgb("#86EFAC"), Color.FromArgb("#059669"));
    }

    private static string TableAssetFor(CachedTable table)
    {
        if (table.SessionStatus?.Equals("Cleaning", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "table_4.png";
        }

        if (table.Status.Equals("Reserved", StringComparison.OrdinalIgnoreCase))
        {
            return "table_3.png";
        }

        if (!string.IsNullOrWhiteSpace(table.CurrentOrderId) || table.Status.Equals("Occupied", StringComparison.OrdinalIgnoreCase))
        {
            return "table_2.png";
        }

        return "table_1.png";
    }

    private static (double X, double Y) TablePosition(CachedTable table, int index)
    {
        if (table.PositionX > 0 || table.PositionY > 0)
        {
            return (Math.Max(22, table.PositionX), Math.Max(36, table.PositionY));
        }

        const double cardWidth = 148;
        const double horizontalGap = 64;
        const double verticalGap = 96;
        var column = index % 5;
        var row = index / 5;
        return (52 + column * (cardWidth + horizontalGap), 64 + row * (cardWidth + verticalGap));
    }

    private static void SetAbsoluteLayout(IView view, Rect bounds, AbsoluteLayoutFlags flags)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Microsoft.Maui.Controls.AbsoluteLayout.LayoutBoundsProperty, bounds);
            bindable.SetValue(Microsoft.Maui.Controls.AbsoluteLayout.LayoutFlagsProperty, flags);
        }
    }

    private void UpdateClock()
    {
        if (_dateLabel != null)
        {
            _dateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        }

        if (_timeLabel != null)
        {
            _timeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    private static void SetRow(IView view, int row)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Microsoft.Maui.Controls.Grid.RowProperty, row);
        }
    }

    private static void SetColumn(IView view, int column)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, column);
        }
    }
}
