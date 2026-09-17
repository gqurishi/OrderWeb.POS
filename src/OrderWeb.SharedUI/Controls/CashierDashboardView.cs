namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared Cashier dashboard. Same day tiles as the admin live summary, plus till actions.
/// Hosts supply the text and handle Refresh, drawer, and Z-report.
/// </summary>
public sealed class CashierDashboardView : ContentView
{
    private readonly Label _dateLine;
    private readonly Label _updated;
    private readonly Label _gross;
    private readonly Label _net;
    private readonly Label _vat;
    private readonly Label _cash;
    private readonly Label _card;
    private readonly Label _giftCard;
    private readonly Label _pos;
    private readonly Label _online;
    private readonly Label _petty;
    private readonly Label _expected;
    private readonly Label _webOrders;
    private readonly Label _vsYesterday;
    private readonly Button _drawer;
    private readonly Button _preview;
    private readonly Button _print;
    private readonly Button _uploadReport;

    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenDrawerRequested;
    public event EventHandler? PreviewZRequested;
    public event EventHandler? PrintZRequested;
    public event EventHandler? UploadReportRequested;

    public CashierDashboardView()
    {
        _dateLine = Caption("Live day summary", 13, "#64748B");
        _updated = Caption("Updated --:--:--", 12, "#94A3B8");
        _gross = Value("£0.00", 18, "#14532D");
        _net = Value("£0.00", 18, "#1E3A8A");
        _vat = Value("£0.00", 18, "#9A3412");
        _cash = Value("£0.00", 16, "#0F172A");
        _card = Value("£0.00", 16, "#0F172A");
        _giftCard = Value("£0.00", 16, "#5B21B6");
        _pos = Value("£0.00 (0)", 16, "#0F172A");
        _online = Value("£0.00 (0)", 16, "#0F172A");
        _petty = Value("£0.00", 16, "#7F1D1D");
        _expected = Value("£0.00", 16, "#065F46");
        _webOrders = Value("0", 16, "#1E3A8A");
        _vsYesterday = Value("--", 14, "#0F172A");
        _vsYesterday.LineBreakMode = LineBreakMode.WordWrap;

        var refresh = ActionButton("Refresh", "#EFF6FF", "#1D4ED8", 44);
        refresh.Clicked += (_, e) => RefreshRequested?.Invoke(this, e);

        var tiles = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        tiles.Add(Tile("Gross", _gross, "#F0FDF4", "#BBF7D0", "#166534"));
        tiles.Add(Tile("Net", _net, "#EFF6FF", "#BFDBFE", "#1D4ED8"), 1, 0);
        tiles.Add(Tile("VAT", _vat, "#FFF7ED", "#FED7AA", "#C2410C"), 2, 0);
        tiles.Add(Tile("Cash", _cash, "#ECFDF5", "#A7F3D0", "#047857"), 0, 1);
        tiles.Add(Tile("Card", _card, "#EFF6FF", "#BFDBFE", "#1D4ED8"), 1, 1);
        tiles.Add(Tile("Gift Card", _giftCard, "#F5F3FF", "#DDD6FE", "#6D28D9"), 2, 1);
        tiles.Add(Tile("POS Sales", _pos, "#F8FAFC", "#E2E8F0", "#475569"), 0, 2);
        tiles.Add(Tile("Online Sales", _online, "#F8FAFC", "#E2E8F0", "#475569"), 1, 2);
        tiles.Add(Tile("Petty Cash Out", _petty, "#FEF2F2", "#FECACA", "#991B1B"), 2, 2);
        tiles.Add(Tile("Expected Cash in Till", _expected, "#ECFDF5", "#A7F3D0", "#047857"), 0, 3);
        tiles.Add(Tile("Total Web Orders", _webOrders, "#EFF6FF", "#BFDBFE", "#1D4ED8"), 1, 3);
        tiles.Add(Tile("vs Yesterday", _vsYesterday, "#F8FAFC", "#E2E8F0", "#475569"), 2, 3);

        var panel = new Border
        {
            Stroke = Color.FromArgb("#BFDBFE"),
            StrokeThickness = 1,
            BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = 18,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    refresh,
                    new VerticalStackLayout { Spacing = 4, Children = { _dateLine, _updated } },
                    tiles
                }
            }
        };

        _drawer = ActionButton("Open Cash Drawer", "#0F766E", "#FFFFFF", 64);
        _preview = ActionButton("Z Report Preview", "#1D4ED8", "#FFFFFF", 64);
        _print = ActionButton("Print Z Report", "#7C3AED", "#FFFFFF", 64);
        _uploadReport = ActionButton("Upload Report", "#0F766E", "#FFFFFF", 64);
        _drawer.Clicked += (_, e) => OpenDrawerRequested?.Invoke(this, e);
        _preview.Clicked += (_, e) => PreviewZRequested?.Invoke(this, e);
        _print.Clicked += (_, e) => PrintZRequested?.Invoke(this, e);
        _uploadReport.Clicked += (_, e) => UploadReportRequested?.Invoke(this, e);

        var actions = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        actions.Add(_drawer);
        actions.Add(_preview, 1);
        actions.Add(_print, 2);
        actions.Add(_uploadReport, 0, 1);
        Grid.SetColumnSpan(_uploadReport, 3);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 20, 24, 28),
                Spacing = 18,
                Children =
                {
                    panel,
                    new Label
                    {
                        Text = "Quick actions",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A"),
                        Margin = new Thickness(0, 8, 0, 0)
                    },
                    actions
                }
            }
        };
    }

    public bool ShowOpenDrawer
    {
        get => _drawer.IsVisible;
        set => _drawer.IsVisible = value;
    }

    /// <summary>Mother Cashier: Upload Report → OrderWeb Business Reports only (not VAT filing).</summary>
    public bool ShowUploadReport
    {
        get => _uploadReport.IsVisible;
        set => _uploadReport.IsVisible = value;
    }

    public void SetActionsEnabled(bool enabled)
    {
        _drawer.IsEnabled = enabled && _drawer.IsVisible;
        _preview.IsEnabled = enabled;
        _print.IsEnabled = enabled;
        _uploadReport.IsEnabled = enabled && _uploadReport.IsVisible;
        if (enabled)
        {
            _uploadReport.Text = "Upload Report";
        }
    }

    /// <summary>Phase 3 busy / double-tap guard while Cashier upload is in flight.</summary>
    public void SetUploadReportBusy(bool busy)
    {
        _uploadReport.IsEnabled = !busy && _uploadReport.IsVisible;
        _uploadReport.Text = busy ? "Uploading…" : "Upload Report";
        if (busy)
        {
            _drawer.IsEnabled = false;
            _preview.IsEnabled = false;
            _print.IsEnabled = false;
        }
        else
        {
            SetActionsEnabled(true);
        }
    }

    public void Apply(CashierDayBoard board)
    {
        _dateLine.Text = board.DateLine;
        _updated.Text = board.UpdatedText;
        _gross.Text = board.Gross;
        _net.Text = board.Net;
        _vat.Text = board.Vat;
        _cash.Text = board.Cash;
        _card.Text = board.Card;
        _giftCard.Text = board.GiftCard;
        _pos.Text = board.PosSales;
        _online.Text = board.OnlineSales;
        _petty.Text = board.PettyCashOut;
        _expected.Text = board.ExpectedCash;
        _webOrders.Text = board.WebOrders;
        _vsYesterday.Text = board.VsYesterday;
    }

    private static Label Caption(string text, double size, string color) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = Color.FromArgb(color),
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static Label Value(string text, double size, string color) => new()
    {
        Text = text,
        FontSize = size,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb(color)
    };

    private static Border Tile(string title, Label value, string background, string stroke, string titleColor) => new()
    {
        BackgroundColor = Color.FromArgb(background),
        Stroke = Color.FromArgb(stroke),
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Padding = 12,
        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = title, FontSize = 11, TextColor = Color.FromArgb(titleColor) },
                value
            }
        }
    };

    private static Button ActionButton(string text, string background, string foreground, double height) => new()
    {
        Text = text,
        Style = null,
        BackgroundColor = Color.FromArgb(background),
        TextColor = Color.FromArgb(foreground),
        FontAttributes = FontAttributes.Bold,
        FontSize = 15,
        CornerRadius = height >= 60 ? 12 : 10,
        HeightRequest = height,
        MinimumHeightRequest = height,
        MinimumWidthRequest = 0,
        Padding = 0,
        HorizontalOptions = LayoutOptions.Fill
    };
}
