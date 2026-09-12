using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-look Order History board: date card, search pill, type tabs, status banner,
/// completed/voided rows, pagination footer. Presentation only — hosts own load/API/print.
/// </summary>
public sealed class OrderHistoryBoardView : ContentView
{
    private readonly Label _selectedDateLabel;
    private readonly Label _searchPlaceholder;
    private readonly Border _searchStatusBorder;
    private readonly Label _searchStatusLabel;
    private readonly Label _searchSourceLabel;
    private readonly Button _clearSearchButton;
    private readonly VerticalStackLayout _completedList;
    private readonly Label _completedEmptyLabel;
    private readonly VerticalStackLayout _voidedSection;
    private readonly VerticalStackLayout _voidedList;
    private readonly Label _pageNumberLabel;
    private readonly Button _previousPageButton;
    private readonly Button _nextPageButton;
    private readonly Button _backButton;
    private readonly ChefLoaderView _loader;
    private readonly Dictionary<OrderHistoryFilter, (Border Border, Label Label, Label? Dot)> _tabs = new();

    private OrderHistoryFilter _filter = OrderHistoryFilter.All;
    private bool _suppressFilter;

    public OrderHistoryBoardView()
    {
        _selectedDateLabel = new Label
        {
            Text = DateTime.Today.ToString("MMMM d, yyyy"),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#14532D"),
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        };

        _searchPlaceholder = new Label
        {
            Text = "Search orders...",
            FontFamily = "OpenSansRegular",
            FontSize = 14,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#94A3B8"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            InputTransparent = true
        };

        _searchStatusLabel = new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#1E3A8A")
        };
        _searchSourceLabel = new Label
        {
            FontFamily = "OpenSansRegular",
            FontSize = 11,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#2563EB")
        };
        _clearSearchButton = MotherLookButton(
            "Clear",
            background: Colors.White,
            foreground: Color.FromArgb("#1D4ED8"),
            width: 88,
            height: 38);
        _clearSearchButton.BorderColor = Color.FromArgb("#93C5FD");
        _clearSearchButton.BorderWidth = 1;
        _clearSearchButton.Clicked += (_, _) => ClearSearchRequested?.Invoke(this, EventArgs.Empty);

        _searchStatusBorder = new Border
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            Stroke = Color.FromArgb("#BFDBFE"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
                Children =
                {
                    new VerticalStackLayout { Spacing = 2, Children = { _searchStatusLabel, _searchSourceLabel } }
                }
            }
        };
        ((Grid)_searchStatusBorder.Content!).Add(_clearSearchButton, 1);

        _completedList = new VerticalStackLayout { Spacing = 6 };
        _completedEmptyLabel = new Label
        {
            Text = "No orders found for this date",
            FontFamily = "OpenSansRegular",
            FontSize = 16,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#9CA3AF"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 40, 0, 0),
            IsVisible = false
        };

        _voidedList = new VerticalStackLayout { Spacing = 6 };
        _voidedSection = new VerticalStackLayout
        {
            Spacing = 12,
            IsVisible = false,
            Children =
            {
                BuildVoidedSeparator(),
                _voidedList
            }
        };

        _loader = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Loading orders",
            DelayMilliseconds = 0,
            IsLoading = false,
            HorizontalOptions = LayoutOptions.Center
        };

        _pageNumberLabel = new Label
        {
            Text = "Page 1",
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#334155"),
            VerticalOptions = LayoutOptions.Center,
            MinimumWidthRequest = 52,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _previousPageButton = MotherLookButton("Previous", Color.FromArgb("#E2E8F0"), Color.FromArgb("#334155"), 88, 32, 12);
        _previousPageButton.Clicked += (_, _) => PreviousPageRequested?.Invoke(this, EventArgs.Empty);
        _nextPageButton = MotherLookButton("Next", Color.FromArgb("#2563EB"), Colors.White, 72, 32, 12);
        _nextPageButton.Clicked += (_, _) => NextPageRequested?.Invoke(this, EventArgs.Empty);
        _backButton = MotherLookButton("Back", Color.FromArgb("#E2E8F0"), Color.FromArgb("#334155"), 72, 32, 12);
        _backButton.Clicked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);

        var dateCard = BuildDateCard();
        var searchPill = BuildSearchPill();
        var headerRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Children = { dateCard }
        };
        headerRow.Add(searchPill, 1);

        var scrollBody = new VerticalStackLayout
        {
            Padding = new Thickness(20),
            Spacing = 16,
            Children =
            {
                headerRow,
                BuildFilterStrip(),
                _searchStatusBorder,
                _loader,
                _completedList,
                _completedEmptyLabel,
                _voidedSection
            }
        };

        var scroll = new ScrollView
        {
            Content = scrollBody,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Always,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        Grid.SetRow(scroll, 0);

        var footer = new Grid
        {
            BackgroundColor = Color.FromArgb("#F8F9FA"),
            Padding = new Thickness(20, 8, 20, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Children = { _backButton }
        };
        footer.Add(_previousPageButton, 2);
        footer.Add(_pageNumberLabel, 3);
        footer.Add(_nextPageButton, 4);
        Grid.SetRow(footer, 1);

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#F8F9FA"),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children = { scroll, footer }
        };

        SetFilter(OrderHistoryFilter.All);
    }

    public event EventHandler? DateFilterTapped;
    public event EventHandler? SearchTapped;
    public event EventHandler? ClearSearchRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? PreviousPageRequested;
    public event EventHandler? NextPageRequested;
    public event EventHandler<OrderHistoryFilterChangedEventArgs>? FilterChanged;
    public event EventHandler<OrderHistoryRowTappedEventArgs>? ViewOrderRequested;

    public OrderHistoryFilter SelectedFilter => _filter;

    public void SetDateDisplay(DateTime date) =>
        _selectedDateLabel.Text = date.ToString("MMMM d, yyyy");

    public void SetSearchDisplay(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchPlaceholder.Text = "Search orders...";
            _searchPlaceholder.TextColor = Color.FromArgb("#94A3B8");
        }
        else
        {
            _searchPlaceholder.Text = query.Trim();
            _searchPlaceholder.TextColor = Color.FromArgb("#1F2937");
        }
    }

    public void SetFilter(OrderHistoryFilter filter)
    {
        _suppressFilter = true;
        try
        {
            _filter = filter;
            ApplyTabStyles();
        }
        finally
        {
            _suppressFilter = false;
        }
    }

    public void SetLoading(bool loading) => _loader.IsLoading = loading;

    public void SetPaging(int pageNumber, bool canPrevious, bool canNext)
    {
        _pageNumberLabel.Text = $"Page {Math.Max(1, pageNumber)}";
        _previousPageButton.IsEnabled = canPrevious;
        _previousPageButton.Opacity = canPrevious ? 1 : 0.45;
        _nextPageButton.IsEnabled = canNext;
        _nextPageButton.Opacity = canNext ? 1 : 0.45;
    }

    public void SetStatusBanner(
        bool visible,
        string title,
        string detail,
        bool showClear,
        bool warning = false,
        bool error = false)
    {
        _searchStatusBorder.IsVisible = visible;
        if (!visible)
        {
            return;
        }

        _searchStatusLabel.Text = title;
        _searchSourceLabel.Text = detail;
        _clearSearchButton.IsVisible = showClear;

        if (error)
        {
            _searchStatusBorder.BackgroundColor = Color.FromArgb("#FEF2F2");
            _searchStatusBorder.Stroke = Color.FromArgb("#FECACA");
            _searchStatusLabel.TextColor = Color.FromArgb("#991B1B");
            _searchSourceLabel.TextColor = Color.FromArgb("#B91C1C");
        }
        else if (warning)
        {
            _searchStatusBorder.BackgroundColor = Color.FromArgb("#FFFBEB");
            _searchStatusBorder.Stroke = Color.FromArgb("#FCD34D");
            _searchStatusLabel.TextColor = Color.FromArgb("#92400E");
            _searchSourceLabel.TextColor = Color.FromArgb("#B45309");
        }
        else
        {
            _searchStatusBorder.BackgroundColor = Color.FromArgb("#EFF6FF");
            _searchStatusBorder.Stroke = Color.FromArgb("#BFDBFE");
            _searchStatusLabel.TextColor = Color.FromArgb("#1E3A8A");
            _searchSourceLabel.TextColor = Color.FromArgb("#2563EB");
        }
    }

    public void SetRows(
        IReadOnlyList<OrderHistoryRowPresentation> completed,
        IReadOnlyList<OrderHistoryRowPresentation> voided,
        string? emptyText = null)
    {
        _completedList.Children.Clear();
        foreach (var row in completed)
        {
            _completedList.Children.Add(BuildRowCard(row, voidedStyle: false));
        }

        _voidedList.Children.Clear();
        foreach (var row in voided)
        {
            _voidedList.Children.Add(BuildRowCard(row, voidedStyle: true));
        }

        _voidedSection.IsVisible = voided.Count > 0;
        _completedEmptyLabel.Text = string.IsNullOrWhiteSpace(emptyText)
            ? "No orders found for this date"
            : emptyText!;
        _completedEmptyLabel.IsVisible = completed.Count == 0 && voided.Count == 0;
    }

    private Border BuildDateCard()
    {
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#F4FBF7"),
            Stroke = Color.FromArgb("#D1E7DD"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 0),
            HeightRequest = 42,
            MinimumWidthRequest = 196,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "DATE",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 10,
                        CharacterSpacing = 0.6,
                        TextColor = Color.FromArgb("#16A34A"),
                        VerticalTextAlignment = TextAlignment.Center
                    },
                    _selectedDateLabel
                }
            }
        };
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => DateFilterTapped?.Invoke(this, EventArgs.Empty))
        });
        return card;
    }

    private Border BuildSearchPill()
    {
        _searchPlaceholder.Margin = new Thickness(4, 0, 0, 0);
        var pill = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 42,
            Padding = new Thickness(14, 0),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "⌕",
                        FontSize = 16,
                        TextColor = Color.FromArgb("#94A3B8"),
                        VerticalTextAlignment = TextAlignment.Center,
                        InputTransparent = true
                    },
                    _searchPlaceholder
                }
            }
        };
        pill.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => SearchTapped?.Invoke(this, EventArgs.Empty))
        });
        return pill;
    }

    private View BuildFilterStrip()
    {
        // Mother screenshot: free-floating centered pills (no outer white card).
        var row = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(4, 4)
        };

        row.Children.Add(CreateTab(OrderHistoryFilter.All, "All", showWebDot: false));
        row.Children.Add(CreateTab(OrderHistoryFilter.Collection, "Collection", showWebDot: false));
        row.Children.Add(CreateTab(OrderHistoryFilter.Delivery, "Delivery", showWebDot: false));
        row.Children.Add(CreateTab(OrderHistoryFilter.Table, "Table", showWebDot: false));
        row.Children.Add(CreateTab(OrderHistoryFilter.Web, "Web", showWebDot: true));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
    }

    private Border CreateTab(OrderHistoryFilter filter, string text, bool showWebDot)
    {
        var label = new Label
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            FontAttributes = FontAttributes.None,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#6B7280")
        };

        Label? dot = null;
        View content = label;
        if (showWebDot)
        {
            dot = new Label
            {
                Text = "●",
                FontSize = 10,
                TextColor = Color.FromArgb("#2563EB"),
                VerticalOptions = LayoutOptions.Center
            };
            content = new HorizontalStackLayout
            {
                Spacing = 7,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { dot, label }
            };
        }

        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            StrokeThickness = 0,
            Padding = new Thickness(24, 10),
            HeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = content
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (_suppressFilter || _filter == filter)
                {
                    return;
                }

                SetFilter(filter);
                FilterChanged?.Invoke(this, new OrderHistoryFilterChangedEventArgs(filter));
            })
        });

        _tabs[filter] = (border, label, dot);
        return border;
    }

    private void ApplyTabStyles()
    {
        foreach (var (filter, tab) in _tabs)
        {
            var selected = filter == _filter;
            if (filter == OrderHistoryFilter.Web && selected)
            {
                tab.Border.BackgroundColor = Color.FromArgb("#2563EB");
                tab.Label.TextColor = Colors.White;
                if (tab.Dot is not null)
                {
                    tab.Dot.TextColor = Colors.White;
                }
            }
            else if (selected)
            {
                tab.Border.BackgroundColor = Color.FromArgb("#10B981");
                tab.Label.TextColor = Colors.White;
            }
            else
            {
                tab.Border.BackgroundColor = Color.FromArgb("#F5F5F5");
                tab.Label.TextColor = Color.FromArgb("#6B7280");
                if (tab.Dot is not null)
                {
                    tab.Dot.TextColor = Color.FromArgb("#2563EB");
                }
            }
        }
    }

    private View BuildRowCard(OrderHistoryRowPresentation row, bool voidedStyle)
    {
        var orderNumber = new Label
        {
            Text = row.OrderNumber,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = voidedStyle ? Color.FromArgb("#991B1B") : Color.FromArgb("#172033"),
            LineBreakMode = LineBreakMode.TailTruncation
        };
        var when = new Label
        {
            Text = row.OrderDateTime,
            FontSize = 12,
            TextColor = voidedStyle ? Color.FromArgb("#B91C1C") : Color.FromArgb("#64748B")
        };
        var customer = new Label
        {
            Text = row.CustomerDisplay,
            FontSize = 11,
            TextColor = voidedStyle ? Color.FromArgb("#B91C1C") : Color.FromArgb("#64748B"),
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var typeCell = voidedStyle
            ? (View)new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = row.OrderTypeDisplay,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#7F1D1D")
                    },
                    new Label
                    {
                        Text = "VOID",
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#DC2626")
                    }
                }
            }
            : new Border
            {
                BackgroundColor = Color.FromArgb("#EEF2FF"),
                Stroke = Color.FromArgb("#C7D2FE"),
                StrokeThickness = 1,
                Padding = new Thickness(12, 7),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Content = new Label
                {
                    Text = row.OrderTypeDisplay,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#4338CA")
                }
            };

        View paymentCell;
        if (voidedStyle)
        {
            paymentCell = new Label
            {
                Text = row.PaymentDisplay,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#7F1D1D"),
                VerticalOptions = LayoutOptions.Center
            };
        }
        else
        {
            paymentCell = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = row.PaymentDisplay,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#334155")
                    },
                    new Label
                    {
                        Text = row.StatusDisplay,
                        FontSize = 11,
                        TextColor = Color.FromArgb("#64748B")
                    }
                }
            };
        }

        var total = new Label
        {
            Text = row.TotalDisplay,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = voidedStyle ? Color.FromArgb("#DC2626") : Color.FromArgb("#059669"),
            Margin = new Thickness(6, 0),
            VerticalOptions = LayoutOptions.Center
        };

        var viewButton = new Border
        {
            BackgroundColor = Color.FromArgb("#2563EB"),
            StrokeThickness = 0,
            WidthRequest = 80,
            HeightRequest = 42,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Label
            {
                Text = "View",
                TextColor = Colors.White,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        viewButton.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ViewOrderRequested?.Invoke(this, new OrderHistoryRowTappedEventArgs(row)))
        });

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.05, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.25, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { orderNumber, when, customer }
                }
            }
        };
        grid.Add(typeCell, 1);
        grid.Add(paymentCell, 2);
        grid.Add(total, 3);
        grid.Add(viewButton, 4);

        return new Border
        {
            BackgroundColor = voidedStyle ? Color.FromArgb("#FFF7F7") : Colors.White,
            Stroke = voidedStyle ? Color.FromArgb("#FCA5A5") : Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = new Thickness(16, 12),
            MinimumHeightRequest = 72,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = grid
        };
    }

    private static Grid BuildVoidedSeparator()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            Margin = new Thickness(0, 20, 0, 12)
        };
        grid.Add(new BoxView
        {
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            HeightRequest = 1,
            VerticalOptions = LayoutOptions.Center
        });
        grid.Add(new Label
        {
            Text = "Voided Orders",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#6B7280"),
            Padding = new Thickness(12, 0)
        }, 1);
        grid.Add(new BoxView
        {
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            HeightRequest = 1,
            VerticalOptions = LayoutOptions.Center
        }, 2);
        return grid;
    }

    private static Button MotherLookButton(
        string text,
        Color background,
        Color foreground,
        double width,
        double height,
        double fontSize = 14) =>
        new()
        {
            Text = text,
            BackgroundColor = background,
            TextColor = foreground,
            FontFamily = "OpenSansSemibold",
            FontSize = fontSize,
            FontAttributes = FontAttributes.None,
            WidthRequest = width,
            HeightRequest = height,
            CornerRadius = 8,
            BorderWidth = 0,
            BorderColor = Colors.Transparent,
            Padding = new Thickness(8, 2),
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0,
            Shadow = new Shadow { Opacity = 0, Radius = 0 }
        };
}
