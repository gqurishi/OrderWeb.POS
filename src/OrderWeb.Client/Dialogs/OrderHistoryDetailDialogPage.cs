using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Dialogs;

/// <summary>Read-only history order details + reprint. No pay/edit/reopen.</summary>
public sealed class OrderHistoryDetailDialogPage : ContentPage
{
    private readonly ClientOrderHistoryDetailDto _order;
    private readonly MotherPrintClient _printClient = new();
    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;
    private bool _busy;
    private Button? _reprintButton;

    public OrderHistoryDetailDialogPage(ClientOrderHistoryDetailDto order)
    {
        _order = order;
        _offlinePolicy = new ClientOfflinePolicy(_cache);
        BackgroundColor = Color.FromArgb("#990F172A");
        BuildUi();
    }

    public async Task ShowAsync(INavigation navigation)
    {
        _hostNavigation = navigation;
        await navigation.PushModalAsync(this, false);
        await _completion.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private void BuildUi()
    {
        var close = ActionButton("Close", "#E2E8F0", "#334155");
        close.Clicked += async (_, _) => await CloseAsync();

        _reprintButton = ActionButton("Reprint receipt", "#2563EB", "#FFFFFF");
        _reprintButton.IsEnabled = _order.CanReprint && !string.IsNullOrWhiteSpace(ResolvePrintOrderId());
        _reprintButton.Opacity = _reprintButton.IsEnabled ? 1 : 0.45;
        _reprintButton.Clicked += async (_, _) => await OnReprintAsync();

        var actions = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        actions.Add(close);
        actions.Add(_reprintButton, 1);

        var lines = new VerticalStackLayout { Spacing = 8 };
        foreach (var line in _order.Lines ?? Array.Empty<ClientOrderHistoryDetailLineDto>())
        {
            lines.Children.Add(LineRow(line));
        }

        if (lines.Children.Count == 0)
        {
            lines.Children.Add(new Label
            {
                Text = "No line items on this order.",
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                TextColor = Color.FromArgb("#64748B")
            });
        }

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(22),
            Children =
            {
                HeaderBlock(),
                SectionTitle("Customer"),
                InfoRow("Name", Display(_order.CustomerName)),
                InfoRow("Phone", Display(_order.CustomerPhone)),
                InfoRow("Email", Display(_order.CustomerEmail)),
                InfoRow("Address", Display(_order.CustomerAddress)),
                SectionTitle("Items"),
                lines,
                SectionTitle("Payment"),
                InfoRow("Method", Display(_order.PaymentDisplay ?? _order.PaymentMethod)),
                InfoRow("Status", Display(_order.PaymentStatusDisplay)),
                InfoRow("Amount paid", _order.AmountPaid.HasValue ? Money(_order.AmountPaid.Value) : "Not supplied"),
                InfoRow("Provider", Display(_order.PaymentProvider)),
                InfoRow("Reference", Display(_order.PaymentReference)),
                SectionTitle("Totals"),
                InfoRow("Subtotal", Money(_order.SubtotalAmount)),
                InfoRow("VAT", Money(_order.TaxAmount)),
                InfoRow("Discount", _order.DiscountAmount > 0 ? $"-{Money(_order.DiscountAmount)}" : Money(0)),
                InfoRow("Delivery fee", Money(_order.DeliveryFee)),
                InfoRow("Service charge", Money(_order.ServiceChargeAmount)),
                InfoRow("Tips", Money(_order.TipsAmount)),
                InfoRow("Total", Money(_order.TotalAmount), emphasize: true),
                SectionTitle("Notes"),
                InfoRow("Scheduled", Display(_order.ScheduledDisplay)),
                InfoRow("Instructions", Display(_order.SpecialInstructions, "None")),
                InfoRow("Promo", Display(_order.PromoCode, "None")),
                InfoRow("Gift card", Display(_order.GiftCardDisplay, "None")),
                InfoRow("Loyalty", Display(_order.LoyaltyDisplay, "None")),
                new Label
                {
                    Text = "View and reprint only — pay/edit are not available from Order History.",
                    FontFamily = "OpenSansRegular",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#64748B"),
                    Margin = new Thickness(0, 8, 0, 0)
                },
                actions
            }
        };

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = 0,
            MaximumWidthRequest = 720,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(24),
            Content = new ScrollView { Content = body }
        };

        var backdrop = new BoxView { Color = Colors.Transparent };
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseAsync())
        });

        Content = new Grid { Children = { backdrop, card } };
    }

    private View HeaderBlock()
    {
        var voided = string.Equals(_order.HistoryGroup, "voided", StringComparison.OrdinalIgnoreCase);
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(_order.OrderNumber) ? "#—" : _order.OrderNumber,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 24,
                    TextColor = voided ? Color.FromArgb("#991B1B") : Color.FromArgb("#0F172A")
                },
                new Label
                {
                    Text = $"{Display(_order.OrderTypeDisplay)} • {Display(_order.StatusDisplay)}",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 14,
                    TextColor = voided ? Color.FromArgb("#DC2626") : Color.FromArgb("#2563EB")
                },
                new Label
                {
                    Text = Display(_order.OrderDateTime),
                    FontFamily = "OpenSansRegular",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#64748B")
                }
            }
        };
    }

    private async Task OnReprintAsync()
    {
        if (_busy || _reprintButton is null)
        {
            return;
        }

        var printOrderId = ResolvePrintOrderId();
        if (string.IsNullOrWhiteSpace(printOrderId))
        {
            await DisplayAlertAsync("Reprint", "This order has no Mother print id.", "OK");
            return;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var gate = _offlinePolicy.Evaluate(ClientOperation.OrderHistory, online);
        if (!gate.Allowed)
        {
            await DisplayAlertAsync("Reprint", gate.Message, "OK");
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            await DisplayAlertAsync("Reprint", "Sign in again before reprinting.", "OK");
            return;
        }

        _busy = true;
        _reprintButton.IsEnabled = false;
        try
        {
            var result = await _printClient.RequestPrintAsync("reprint", printOrderId, session);
            var ok = string.Equals(result.Status, "queued", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(result.Status, "printed", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(result.Status, "partial", StringComparison.OrdinalIgnoreCase);
            await DisplayAlertAsync(
                ok ? "Reprint queued" : "Reprint failed",
                result.Message ?? (ok
                    ? "Mother POS accepted the reprint on its receipt printer."
                    : "Mother POS could not reprint this receipt."),
                "OK");
        }
        finally
        {
            _busy = false;
            _reprintButton.IsEnabled = _order.CanReprint && !string.IsNullOrWhiteSpace(ResolvePrintOrderId());
        }
    }

    private string? ResolvePrintOrderId() =>
        !string.IsNullOrWhiteSpace(_order.OrderId)
            ? _order.OrderId.Trim()
            : _order.Id > 0
                ? _order.Id.ToString(CultureInfo.InvariantCulture)
                : null;

    private async Task CloseAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            var navigation = _hostNavigation ?? Navigation;
            if (navigation.ModalStack.Contains(this))
            {
                await navigation.PopModalAsync(false);
            }
        }
        finally
        {
            _completion.TrySetResult(true);
        }
    }

    private static View LineRow(ClientOrderHistoryDetailLineDto line)
    {
        var nameLabel = new Label
        {
            Text = $"{Math.Max(1, line.Quantity)}× {Display(line.Name)}",
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Color.FromArgb("#1E293B")
        };
        var totalLabel = new Label
        {
            Text = Money(line.TotalPrice),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Color.FromArgb("#0F172A"),
            HorizontalTextAlignment = TextAlignment.End
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(nameLabel);
        header.Add(totalLabel, 1);

        var stack = new VerticalStackLayout { Spacing = 2, Children = { header } };
        if (!string.IsNullOrWhiteSpace(line.Details))
        {
            stack.Children.Add(new Label
            {
                Text = line.Details,
                FontFamily = "OpenSansRegular",
                FontSize = 12,
                TextColor = Color.FromArgb("#64748B")
            });
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10),
            Content = stack
        };
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 13,
        TextColor = Color.FromArgb("#0F172A"),
        Margin = new Thickness(0, 6, 0, 0)
    };

    private static View InfoRow(string label, string value, bool emphasize = false)
    {
        var valueLabel = new Label
        {
            Text = value,
            FontFamily = emphasize ? "OpenSansSemibold" : "OpenSansRegular",
            FontSize = emphasize ? 16 : 13,
            TextColor = emphasize ? Color.FromArgb("#059669") : Color.FromArgb("#1E293B")
        };
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(140)),
                new ColumnDefinition(GridLength.Star)
            }
        };
        grid.Add(new Label
        {
            Text = label,
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B")
        });
        grid.Add(valueLabel, 1);
        return grid;
    }

    private static Button ActionButton(string text, string background, string foreground) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb(background),
        TextColor = Color.FromArgb(foreground),
        FontFamily = "OpenSansSemibold",
        FontSize = 15,
        CornerRadius = 10,
        HeightRequest = 48
    };

    private static string Money(decimal value) => $"£{value:F2}";

    private static string Display(string? value, string fallback = "—") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
