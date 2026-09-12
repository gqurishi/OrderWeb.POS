using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-look Recent Customers board: summary cards, search, All/Collection/Delivery,
/// cache rows + Remove. Presentation only — hosts own load/sync/delete.
/// </summary>
public sealed class RecentCustomersBoardView : ContentView
{
    private readonly Label _subtitleLabel;
    private readonly Label _syncedCountLabel;
    private readonly Label _pendingCountLabel;
    private readonly Label _failedCountLabel;
    private readonly Label _lastSyncLabel;
    private readonly Entry _searchEntry;
    private readonly Button _filterAll;
    private readonly Button _filterCollection;
    private readonly Button _filterDelivery;
    private readonly VerticalStackLayout _rows;
    private readonly VerticalStackLayout _emptyView;
    private readonly Button _retryButton;
    private readonly Button _refreshButton;

    private RecentCustomerFilter _filter = RecentCustomerFilter.All;
    private bool _suppressSearch;
    private CancellationTokenSource? _searchCts;

    public RecentCustomersBoardView()
    {
        _subtitleLabel = new Label
        {
            Text = "7-day local cache · OrderWeb is the master record",
            FontFamily = "OpenSansRegular",
            FontSize = 12,
            TextColor = Color.FromArgb("#64748B")
        };

        _syncedCountLabel = CountLabel("0", "#14532D");
        _pendingCountLabel = CountLabel("0", "#78350F");
        _failedCountLabel = CountLabel("0", "#991B1B");
        _lastSyncLabel = new Label
        {
            Text = "Never",
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            TextColor = Color.FromArgb("#1E3A8A"),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        _retryButton = HeaderAction("Retry Sync", "#7C3AED");
        _retryButton.Clicked += (_, _) => RetrySyncRequested?.Invoke(this, EventArgs.Empty);
        _refreshButton = HeaderAction("Refresh", "#0284C7");
        _refreshButton.Clicked += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _searchEntry = new Entry
        {
            Placeholder = "Search name, phone, address, postcode...",
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            Margin = new Thickness(10, 4),
            BackgroundColor = Colors.Transparent
        };
        _searchEntry.TextChanged += OnSearchTextChanged;

        _filterAll = FilterButton("All", selected: true);
        _filterCollection = FilterButton("Collection", selected: false);
        _filterDelivery = FilterButton("Delivery", selected: false);
        _filterAll.Clicked += (_, _) => SelectFilter(RecentCustomerFilter.All);
        _filterCollection.Clicked += (_, _) => SelectFilter(RecentCustomerFilter.Collection);
        _filterDelivery.Clicked += (_, _) => SelectFilter(RecentCustomerFilter.Delivery);

        _rows = new VerticalStackLayout { Spacing = 6 };
        _emptyView = new VerticalStackLayout
        {
            Padding = 30,
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children =
            {
                new Label
                {
                    Text = "No recent customers",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 18,
                    TextColor = Color.FromArgb("#64748B"),
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Customers appear here when staff use collection or delivery screens. Rows older than 7 days are purged automatically.",
                    FontFamily = "OpenSansRegular",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#94A3B8"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        headerGrid.Add(new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label
                {
                    Text = "Recent Customers",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 20,
                    FontAttributes = FontAttributes.None,
                    TextColor = Color.FromArgb("#0F172A")
                },
                _subtitleLabel
            }
        });
        headerGrid.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { _retryButton, _refreshButton }
        }, 1);
        var header = SoftCard(headerGrid, padding: 10);

        var summary = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        summary.Add(StatCard("Synced", _syncedCountLabel, "#BBF7D0", "#F0FDF4", "#166534"), 0);
        summary.Add(StatCard("Pending", _pendingCountLabel, "#FDE68A", "#FFFBEB", "#92400E"), 1);
        summary.Add(StatCard("Failed", _failedCountLabel, "#FECACA", "#FEF2F2", "#B91C1C"), 2);
        summary.Add(StatCard("Last cloud sync", _lastSyncLabel, "#BFDBFE", "#EFF6FF", "#1D4ED8"), 3);

        var info = new Border
        {
            Stroke = Color.FromArgb("#BFDBFE"),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10, 6),
            Content = new Label
            {
                FontFamily = "OpenSansRegular",
                FontSize = 12,
                FontAttributes = FontAttributes.None,
                TextColor = Color.FromArgb("#334155"),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                Text = "7-day till cache. Full history stays in OrderWeb. Remove cache only deletes the local row."
            }
        };

        var searchRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        searchRow.Add(new Border
        {
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(4, 0),
            Content = _searchEntry
        });
        searchRow.Add(_filterAll, 1);
        searchRow.Add(_filterCollection, 2);
        searchRow.Add(_filterDelivery, 3);

        var listCard = SoftCard(new VerticalStackLayout
        {
            Spacing = 6,
            Children = { _rows, _emptyView }
        }, padding: 12);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(12, 8),
                Spacing = 8,
                Children = { header, summary, info, searchRow, listCard }
            }
        };

        BackgroundColor = Color.FromArgb("#F8FAFC");
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? RetrySyncRequested;
    public event EventHandler<string>? SearchChanged;
    public event EventHandler<RecentCustomerFilterChangedEventArgs>? FilterChanged;
    public event EventHandler<RecentCustomerRowEventArgs>? RemoveCacheRequested;

    public RecentCustomerFilter SelectedFilter => _filter;
    public string SearchText => _searchEntry.Text?.Trim() ?? string.Empty;

    public void SetSummary(RecentCustomerSyncSummaryPresentation summary)
    {
        _subtitleLabel.Text = summary.Subtitle;
        _syncedCountLabel.Text = summary.SyncedCount.ToString();
        _pendingCountLabel.Text = summary.PendingCount.ToString();
        _failedCountLabel.Text = summary.FailedCount.ToString();
        _lastSyncLabel.Text = string.IsNullOrWhiteSpace(summary.LastCloudSyncDisplay)
            ? "Never"
            : summary.LastCloudSyncDisplay;
    }

    public void SetFilter(RecentCustomerFilter filter)
    {
        _filter = filter;
        ApplyFilterStyles();
    }

    public void SetSearchText(string? text)
    {
        _suppressSearch = true;
        try
        {
            _searchEntry.Text = text ?? string.Empty;
        }
        finally
        {
            _suppressSearch = false;
        }
    }

    public void SetBusy(bool busy)
    {
        _retryButton.IsEnabled = !busy;
        _refreshButton.IsEnabled = !busy;
        _filterAll.IsEnabled = !busy;
        _filterCollection.IsEnabled = !busy;
        _filterDelivery.IsEnabled = !busy;
        _searchEntry.IsEnabled = !busy;
    }

    public void SetRows(IReadOnlyList<RecentCustomerRowPresentation> rows)
    {
        _rows.Children.Clear();
        foreach (var row in rows)
        {
            _rows.Children.Add(BuildRow(row));
        }

        _emptyView.IsVisible = rows.Count == 0;
        _rows.IsVisible = rows.Count > 0;
    }

    private void SelectFilter(RecentCustomerFilter filter)
    {
        if (_filter == filter)
        {
            return;
        }

        SetFilter(filter);
        FilterChanged?.Invoke(this, new RecentCustomerFilterChangedEventArgs(filter));
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressSearch)
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            await Task.Delay(250, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                SearchChanged?.Invoke(this, SearchText);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyFilterStyles()
    {
        StyleFilter(_filterAll, _filter == RecentCustomerFilter.All);
        StyleFilter(_filterCollection, _filter == RecentCustomerFilter.Collection);
        StyleFilter(_filterDelivery, _filter == RecentCustomerFilter.Delivery);
    }

    private static void StyleFilter(Button button, bool selected)
    {
        button.BackgroundColor = selected ? Color.FromArgb("#3B82F6") : Color.FromArgb("#E2E8F0");
        button.TextColor = selected ? Colors.White : Color.FromArgb("#334155");
    }

    private View BuildRow(RecentCustomerRowPresentation row)
    {
        var badges = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        if (row.ShowCollectionBadge)
        {
            badges.Children.Add(Pill("Collection", "#DCFCE7", "#BBF7D0", "#166534"));
        }

        if (row.ShowDeliveryBadge)
        {
            badges.Children.Add(Pill("Delivery", "#DBEAFE", "#BFDBFE", "#1D4ED8"));
        }

        var sync = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        if (row.ShowSyncedBadge)
        {
            sync.Children.Add(Pill("Synced", "#DCFCE7", null, "#166534"));
        }

        if (row.ShowPendingBadge)
        {
            sync.Children.Add(Pill("Pending", "#FEF3C7", null, "#92400E"));
        }

        if (row.ShowFailedBadge)
        {
            sync.Children.Add(Pill("Failed", "#FEE2E2", null, "#B91C1C"));
        }

        var left = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        left.Add(new Label
        {
            Text = row.Name,
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Color.FromArgb("#0F172A"),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Center
        });
        left.Add(badges, 1);
        left.Add(sync, 2);
        left.Add(new Label
        {
            Text = row.ContactDetail,
            FontFamily = "OpenSansRegular",
            FontSize = 14,
            TextColor = Color.FromArgb("#475569"),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Center
        }, 3);

        var remove = MotherLookButton(
            "Remove cache",
            background: "#FEE2E2",
            textColor: "#B91C1C",
            fontFamily: "OpenSansRegular",
            fontSize: 12,
            cornerRadius: 8,
            padding: new Thickness(10, 6));
        remove.VerticalOptions = LayoutOptions.Center;
        remove.HeightRequest = 28;
        remove.Clicked += (_, _) => RemoveCacheRequested?.Invoke(this, new RecentCustomerRowEventArgs(row));

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        grid.Add(left);
        grid.Add(remove, 1);

        return new Border
        {
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 8),
            HeightRequest = 52,
            MinimumHeightRequest = 52,
            MaximumHeightRequest = 52,
            Content = grid
        };
    }

    private static Border SoftCard(View content, double padding = 16) =>
        new()
        {
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = padding,
            Content = content
        };

    private static Border StatCard(string title, View value, string stroke, string background, string titleColor) =>
        new()
        {
            Stroke = Color.FromArgb(stroke),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb(background),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10, 6),
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new Label
                    {
                        Text = title,
                        FontFamily = "OpenSansRegular",
                        FontSize = 11,
                        TextColor = Color.FromArgb(titleColor)
                    },
                    value
                }
            }
        };

    private static Label CountLabel(string text, string color) =>
        new()
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = 18,
            TextColor = Color.FromArgb(color)
        };

    /// <summary>
    /// Mother-look buttons. Explicit fonts beat Client App.xaml Button style
    /// (OpenSansSemibold 15) so Mother + Client paint the same.
    /// </summary>
    private static Button MotherLookButton(
        string text,
        string background,
        string textColor,
        string fontFamily,
        double fontSize,
        int cornerRadius,
        Thickness padding) =>
        new()
        {
            Text = text,
            BackgroundColor = Color.FromArgb(background),
            TextColor = Color.FromArgb(textColor),
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontAttributes = FontAttributes.None,
            CornerRadius = cornerRadius,
            Padding = padding,
            BorderWidth = 0,
            Style = null,
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0
        };

    private static Button HeaderAction(string text, string background) =>
        MotherLookButton(
            text,
            background,
            textColor: "#FFFFFF",
            fontFamily: "OpenSansSemibold",
            fontSize: 13,
            cornerRadius: 8,
            padding: new Thickness(12, 6));

    private static Button FilterButton(string text, bool selected) =>
        MotherLookButton(
            text,
            background: selected ? "#3B82F6" : "#E2E8F0",
            textColor: selected ? "#FFFFFF" : "#334155",
            fontFamily: "OpenSansRegular",
            fontSize: 13,
            cornerRadius: 8,
            padding: new Thickness(12, 6));

    private static Border Pill(string text, string background, string? stroke, string textColor) =>
        new()
        {
            BackgroundColor = Color.FromArgb(background),
            Stroke = stroke is null ? Colors.Transparent : Color.FromArgb(stroke),
            StrokeThickness = stroke is null ? 0 : 1,
            Padding = new Thickness(8, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new Label
            {
                Text = text,
                FontSize = 11,
                FontFamily = "OpenSansSemibold",
                TextColor = Color.FromArgb(textColor)
            }
        };
}
