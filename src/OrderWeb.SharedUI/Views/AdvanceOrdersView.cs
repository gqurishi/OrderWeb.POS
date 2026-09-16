using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Manager Advance Orders list chrome for Mother + Client.
/// Presentation only — hosts own load / open order / print kitchen.
/// </summary>
public sealed class AdvanceOrdersView : ContentView
{
    private readonly Label _statusLabel;
    private readonly Label _emptyLabel;
    private readonly VerticalStackLayout _list;
    private readonly ChefLoaderView _loader;
    private readonly Dictionary<AdvanceOrderRange, (Border Border, Label Label)> _chips = new();

    private AdvanceOrderRange _range = AdvanceOrderRange.Today;
    private bool _suppressRange;
    private bool _busy;
    private bool _sampleMode;

    public AdvanceOrdersView()
    {
        _statusLabel = new Label
        {
            Text = string.Empty,
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B"),
            IsVisible = false,
            LineBreakMode = LineBreakMode.WordWrap
        };

        _emptyLabel = new Label
        {
            Text = "No advance orders",
            FontFamily = "OpenSansRegular",
            FontSize = 16,
            TextColor = Color.FromArgb("#9CA3AF"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 48, 0, 0),
            IsVisible = false
        };

        _list = new VerticalStackLayout { Spacing = 8 };

        _loader = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Loading advance orders",
            DelayMilliseconds = 0,
            IsLoading = false,
            HorizontalOptions = LayoutOptions.Center
        };

        var title = new Label
        {
            Text = "Advance Orders",
            FontFamily = "OpenSansSemibold",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A")
        };

        var subtitle = new Label
        {
            Text = "Scheduled Collection and Delivery orders.",
            FontFamily = "OpenSansRegular",
            FontSize = 14,
            TextColor = Color.FromArgb("#64748B")
        };

        var scrollBody = new VerticalStackLayout
        {
            Padding = new Thickness(20),
            Spacing = 16,
            Children =
            {
                title,
                subtitle,
                BuildRangeStrip(),
                _statusLabel,
                _loader,
                _list,
                _emptyLabel
            }
        };

        BackgroundColor = Color.FromArgb("#F8FAFC");
        Content = new ScrollView
        {
            Content = scrollBody,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Always
        };

        PaintRangeChips();
    }

    public event EventHandler<AdvanceOrderRangeChangedEventArgs>? RangeChanged;
    public event EventHandler<AdvanceOrderRowTappedEventArgs>? RowTapped;
    public event EventHandler<AdvanceOrderPrintKitchenEventArgs>? PrintKitchenRequested;

    public AdvanceOrderRange SelectedRange => _range;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _loader.IsLoading = busy;
        if (busy)
        {
            _emptyLabel.IsVisible = false;
        }
    }

    public void SetStatus(string? message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.IsVisible = false;
            _statusLabel.Text = string.Empty;
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.TextColor = isError ? Color.FromArgb("#DC2626") : Color.FromArgb("#64748B");
        _statusLabel.IsVisible = true;
    }

    public void SetRange(AdvanceOrderRange range, bool raiseEvent = false)
    {
        if (_range == range && !raiseEvent)
        {
            PaintRangeChips();
            return;
        }

        _range = range;
        PaintRangeChips();
        if (raiseEvent)
        {
            RangeChanged?.Invoke(this, new AdvanceOrderRangeChangedEventArgs(range));
        }
    }

    public void SetRows(IReadOnlyList<AdvanceOrderRowPresentation>? rows)
    {
        _sampleMode = false;
        ApplyRows(rows);
    }

    /// <summary>Fills sample rows for SharedUI preview / host stub (no HTTP).</summary>
    public void ShowSampleData()
    {
        _sampleMode = true;
        SetBusy(false);
        SetStatus(null);
        ApplyRows(AdvanceOrderSampleData.RowsFor(_range));
    }

    private void ApplyRows(IReadOnlyList<AdvanceOrderRowPresentation>? rows)
    {
        _list.Children.Clear();
        var list = rows ?? Array.Empty<AdvanceOrderRowPresentation>();
        if (list.Count == 0)
        {
            _emptyLabel.IsVisible = !_busy;
            return;
        }

        _emptyLabel.IsVisible = false;
        foreach (var row in list)
        {
            _list.Children.Add(BuildRow(row));
        }
    }

    private View BuildRangeStrip()
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(4, 4)
        };

        row.Children.Add(CreateChip(AdvanceOrderRange.Today, "Today"));
        row.Children.Add(CreateChip(AdvanceOrderRange.Tomorrow, "Tomorrow"));
        row.Children.Add(CreateChip(AdvanceOrderRange.Next7Days, "Next 7 days"));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
    }

    private Border CreateChip(AdvanceOrderRange range, string text)
    {
        var label = new Label
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#6B7280")
        };

        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            StrokeThickness = 0,
            Padding = new Thickness(24, 10),
            HeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = label
        };

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (_suppressRange || _busy || _range == range)
                {
                    return;
                }

                _range = range;
                PaintRangeChips();
                if (_sampleMode)
                {
                    SetRows(AdvanceOrderSampleData.RowsFor(range));
                }

                RangeChanged?.Invoke(this, new AdvanceOrderRangeChangedEventArgs(range));
            })
        });

        _chips[range] = (border, label);
        return border;
    }

    private void PaintRangeChips()
    {
        _suppressRange = true;
        try
        {
            foreach (var pair in _chips)
            {
                var selected = pair.Key == _range;
                pair.Value.Border.BackgroundColor = selected
                    ? Color.FromArgb("#16A34A")
                    : Color.FromArgb("#F5F5F5");
                pair.Value.Label.TextColor = selected
                    ? Colors.White
                    : Color.FromArgb("#6B7280");
            }
        }
        finally
        {
            _suppressRange = false;
        }
    }

    private View BuildRow(AdvanceOrderRowPresentation row)
    {
        var time = new Label
        {
            Text = string.IsNullOrWhiteSpace(row.ScheduledDisplay) ? "—" : row.ScheduledDisplay,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalTextAlignment = TextAlignment.Center
        };

        var type = new Label
        {
            Text = row.OrderType,
            FontFamily = "OpenSansRegular",
            FontSize = 14,
            TextColor = Color.FromArgb("#334155"),
            VerticalTextAlignment = TextAlignment.Center
        };

        var customer = new Label
        {
            Text = string.IsNullOrWhiteSpace(row.CustomerName) ? "Customer" : row.CustomerName,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            TextColor = Color.FromArgb("#0F172A"),
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var phone = new Label
        {
            Text = string.IsNullOrWhiteSpace(row.CustomerPhone) ? "—" : row.CustomerPhone,
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B")
        };

        var number = new Label
        {
            Text = string.IsNullOrWhiteSpace(row.OrderNumber) ? "#" : row.OrderNumber,
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = Color.FromArgb("#475569"),
            VerticalTextAlignment = TextAlignment.Center
        };

        var statusColor = row.KitchenPrinted || string.Equals(row.Status, "Printed", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb("#059669")
            : Color.FromArgb("#B45309");
        var statusBg = row.KitchenPrinted || string.Equals(row.Status, "Printed", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb("#ECFDF5")
            : Color.FromArgb("#FEF3C7");

        var status = new Border
        {
            BackgroundColor = statusBg,
            StrokeThickness = 0,
            Padding = new Thickness(10, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = string.IsNullOrWhiteSpace(row.Status)
                    ? (row.KitchenPrinted ? "Printed" : "Pending")
                    : row.Status,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = statusColor
            }
        };

        var printButton = new Button
        {
            Text = "Print kitchen",
            Style = null,
            BackgroundColor = Color.FromArgb("#2563EB"),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            CornerRadius = 8,
            HeightRequest = 36,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.End
        };
        printButton.Clicked += (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            PrintKitchenRequested?.Invoke(this, new AdvanceOrderPrintKitchenEventArgs(row));
        };

        var left = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children = { time, type, number }
                },
                customer,
                phone
            }
        };

        var right = new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children = { status, printButton }
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            Padding = new Thickness(16, 12),
            Children = { left }
        };
        grid.Add(right, 1);

        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = grid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            RowTapped?.Invoke(this, new AdvanceOrderRowTappedEventArgs(row));
        };
        border.GestureRecognizers.Add(tap);

        return border;
    }
}
