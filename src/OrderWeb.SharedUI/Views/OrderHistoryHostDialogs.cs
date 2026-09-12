using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Shared Order History date / search pickers so Mother and Client match.</summary>
public static class OrderHistoryPickers
{
    public static async Task<DateTime?> PickDateAsync(INavigation navigation, DateTime selectedDate)
    {
        var tcs = new TaskCompletionSource<DateTime?>();
        var picker = new DatePicker
        {
            Date = selectedDate.Date,
            Format = "D",
            FontSize = 18,
            TextColor = Color.FromArgb("#14532D"),
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = DateTime.Today
        };

        var apply = new Button
        {
            Text = "Apply",
            BackgroundColor = Color.FromArgb("#10B981"),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.None,
            FontSize = 15,
            HeightRequest = 48,
            CornerRadius = 8,
            BorderWidth = 0,
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0
        };
        var cancel = new Button
        {
            Text = "Cancel",
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            TextColor = Color.FromArgb("#374151"),
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.None,
            FontSize = 15,
            HeightRequest = 48,
            CornerRadius = 8,
            BorderWidth = 0,
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0
        };

        apply.Clicked += async (_, _) =>
        {
            tcs.TrySetResult(picker.Date is DateTime d ? d.Date : selectedDate.Date);
            await navigation.PopModalAsync(false);
        };
        cancel.Clicked += async (_, _) =>
        {
            tcs.TrySetResult(null);
            await navigation.PopModalAsync(false);
        };

        var actions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
            Padding = new Thickness(20),
            Children = { cancel }
        };
        actions.Add(apply, 1);

        var panel = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            WidthRequest = 420,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#10B981"),
                        StrokeThickness = 0,
                        Padding = new Thickness(20),
                        Content = new Label
                        {
                            Text = "Select Date",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            HorizontalOptions = LayoutOptions.Center
                        }
                    },
                    new VerticalStackLayout
                    {
                        Padding = new Thickness(24, 20),
                        Spacing = 12,
                        Children =
                        {
                            new Label
                            {
                                Text = "FILTER BY DATE",
                                FontSize = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#16A34A")
                            },
                            picker
                        }
                    },
                    actions
                }
            }
        };

        var page = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Content = panel
        };
        await navigation.PushModalAsync(page, false);
        return await tcs.Task;
    }

    public static async Task<string?> PickSearchAsync(ContentPage hostPage, string? currentQuery)
    {
        var keyboard = new NumericKeyboardDialog();
        var value = await keyboard.ShowDigitsAsync(
            currentQuery,
            title: "Order number or phone",
            maxDigits: 24,
            hostPage: hostPage);
        return value?.Trim().TrimStart('#');
    }
}

/// <summary>Host-neutral order history detail for SharedUI Mother-look modal.</summary>
public sealed record OrderHistoryDetailLinePresentation(
    string QuantityDisplay,
    string Name,
    string? Details,
    string TotalDisplay);

public sealed record OrderHistoryDetailPresentation(
    string OrderNumber,
    string OrderDateTime,
    string OrderTypeDisplay,
    string StatusDisplay,
    string CustomerName,
    string CustomerPhone,
    string CustomerAddress,
    string PaymentMethod,
    string PaymentStatus,
    string AmountPaid,
    string PaymentProvider,
    string PaymentReference,
    string Scheduled,
    string Instructions,
    string Promo,
    string GiftCard,
    string Loyalty,
    string Subtotal,
    string Vat,
    string Discount,
    string DeliveryFee,
    string ServiceCharge,
    string Tips,
    string Total,
    bool CanPrint,
    string PrintButtonText,
    IReadOnlyList<OrderHistoryDetailLinePresentation> Lines,
    object? Tag = null);

/// <summary>Mother Order Details modal look: green header, Print + Close. Hosts own print.</summary>
public sealed class OrderHistoryDetailDialog : ContentPage
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    public event Func<OrderHistoryDetailPresentation, Task>? PrintRequested;

    public OrderHistoryDetailDialog(OrderHistoryDetailPresentation order)
    {
        BackgroundColor = Color.FromArgb("#80000000");
        Build(order);
    }

    public async Task ShowAsync(INavigation navigation)
    {
        await navigation.PushModalAsync(this, false);
        await _done.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private void Build(OrderHistoryDetailPresentation order)
    {
        var lines = new VerticalStackLayout { Spacing = 4 };
        foreach (var line in order.Lines)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(8, 6)
            };
            row.Add(new Label { Text = line.QuantityDisplay, FontSize = 14, TextColor = Color.FromArgb("#6B7280"), VerticalOptions = LayoutOptions.Start });
            var nameStack = new VerticalStackLayout
            {
                Margin = new Thickness(8, 0, 0, 0),
                Spacing = 2,
                Children = { new Label { Text = line.Name, FontSize = 14, TextColor = Color.FromArgb("#1F2937") } }
            };
            if (!string.IsNullOrWhiteSpace(line.Details))
            {
                nameStack.Children.Add(new Label
                {
                    Text = line.Details,
                    FontSize = 11,
                    TextColor = Color.FromArgb("#64748B"),
                    LineBreakMode = LineBreakMode.WordWrap
                });
            }

            row.Add(nameStack, 1);
            row.Add(new Label
            {
                Text = line.TotalDisplay,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1F2937"),
                VerticalOptions = LayoutOptions.Start
            }, 2);
            lines.Children.Add(row);
        }

        if (lines.Children.Count == 0)
        {
            lines.Children.Add(new Label
            {
                Text = "No line items on this order.",
                FontSize = 14,
                TextColor = Color.FromArgb("#64748B")
            });
        }

        var print = ActionChip(order.PrintButtonText, "#8B5CF6");
        print.IsEnabled = order.CanPrint;
        print.Opacity = order.CanPrint ? 1 : 0.45;
        print.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                if (PrintRequested is not null)
                {
                    await PrintRequested.Invoke(order);
                }
            })
        });
        var close = ActionChip("Close", "#6B7280");
        close.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseAsync())
        });

        var footer = new Grid
        {
            Padding = 20,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            Children = { print }
        };
        footer.Add(close, 1);

        var scrollBody = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = 20,
            Children =
            {
                InfoGrid(
                    ("Order Number:", order.OrderNumber),
                    ("Date & Time:", order.OrderDateTime),
                    ("Type:", order.OrderTypeDisplay),
                    ("Status:", order.StatusDisplay)),
                SoftCard("Customer Information",
                    $"Name: {Blank(order.CustomerName)}",
                    $"Phone: {Blank(order.CustomerPhone)}",
                    $"Address: {Blank(order.CustomerAddress)}"),
                Divider(),
                new Label { Text = "Items:", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1F2937") },
                lines,
                Divider(),
                PaymentCard(order),
                Divider(),
                SoftCard("Order Information",
                    $"Scheduled: {Blank(order.Scheduled)}",
                    $"Instructions: {Blank(order.Instructions)}",
                    $"Promo code: {Blank(order.Promo)}",
                    $"Gift card: {Blank(order.GiftCard)}",
                    $"Loyalty: {Blank(order.Loyalty)}"),
                Divider(),
                TotalsBlock(order)
            }
        };

        var frame = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Margin = 40,
            Padding = 0,
            WidthRequest = 600,
            MaximumHeightRequest = 700,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#10B981"),
                        StrokeThickness = 0,
                        Padding = 20,
                        Content = new Label
                        {
                            Text = "Order Details",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            VerticalOptions = LayoutOptions.Center
                        }
                    }
                }
            }
        };
        var root = (Grid)frame.Content!;
        var scroll = new ScrollView { Content = scrollBody };
        Grid.SetRow(scroll, 1);
        root.Add(scroll);
        Grid.SetRow(footer, 2);
        root.Add(footer);

        Content = frame;
    }

    private async Task CloseAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync(false);
            }
        }
        finally
        {
            _done.TrySetResult(true);
        }
    }

    private static Border ActionChip(string text, string background) =>
        new()
        {
            BackgroundColor = Color.FromArgb(background),
            StrokeThickness = 0,
            Padding = new Thickness(16, 12),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Label
            {
                Text = text,
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

    private static BoxView Divider() =>
        new() { BackgroundColor = Color.FromArgb("#E5E7EB"), HeightRequest = 1 };

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static Grid InfoGrid(params (string Label, string Value)[] rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            RowSpacing = 8
        };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(new Label
            {
                Text = rows[i].Label,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#6B7280")
            }, 0, i);
            grid.Add(new Label
            {
                Text = Blank(rows[i].Value),
                FontSize = 14,
                TextColor = Color.FromArgb("#1F2937"),
                Margin = new Thickness(10, 0, 0, 0)
            }, 1, i);
        }

        return grid;
    }

    private static Border SoftCard(string title, params string[] lines)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1F2937") }
            }
        };
        foreach (var line in lines)
        {
            stack.Children.Add(new Label { Text = line, FontSize = 13, TextColor = Color.FromArgb("#6B7280") });
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Padding = 12,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = stack
        };
    }

    private static Border PaymentCard(OrderHistoryDetailPresentation order)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 6,
            ColumnSpacing = 16
        };
        grid.Add(PaymentCell("Method", order.PaymentMethod), 0, 0);
        grid.Add(PaymentCell("Payment status", order.PaymentStatus), 1, 0);
        grid.Add(PaymentCell("Amount paid", order.AmountPaid), 0, 1);
        grid.Add(PaymentCell("Provider", order.PaymentProvider), 1, 1);
        var reference = PaymentCell("Payment reference", order.PaymentReference);
        Grid.SetColumnSpan(reference, 2);
        grid.Add(reference, 0, 2);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            Stroke = Color.FromArgb("#BFDBFE"),
            StrokeThickness = 1,
            Padding = 12,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Payment Information",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1E3A8A")
                    },
                    grid
                }
            }
        };
    }

    private static VerticalStackLayout PaymentCell(string label, string value) =>
        new()
        {
            Spacing = 1,
            Children =
            {
                new Label { Text = label, FontSize = 11, TextColor = Color.FromArgb("#64748B") },
                new Label
                {
                    Text = Blank(value),
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A"),
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
        };

    private static VerticalStackLayout TotalsBlock(OrderHistoryDetailPresentation order)
    {
        var block = new VerticalStackLayout { Spacing = 8 };
        void AddRow(string label, string value, bool danger = false, bool strong = false)
        {
            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            grid.Add(new Label
            {
                Text = label,
                FontSize = strong ? 18 : 14,
                FontAttributes = strong ? FontAttributes.Bold : FontAttributes.None,
                TextColor = Color.FromArgb(strong ? "#1F2937" : "#6B7280")
            });
            grid.Add(new Label
            {
                Text = value,
                FontSize = strong ? 20 : 14,
                FontAttributes = strong || danger ? FontAttributes.Bold : FontAttributes.None,
                TextColor = Color.FromArgb(strong ? "#10B981" : danger ? "#DC2626" : "#1F2937")
            }, 1);
            block.Children.Add(grid);
        }

        AddRow("Subtotal:", order.Subtotal);
        AddRow("VAT:", order.Vat);
        AddRow("Discount:", order.Discount, danger: true);
        AddRow("Delivery fee:", order.DeliveryFee);
        AddRow("Service charge:", order.ServiceCharge);
        AddRow("Tips:", order.Tips);
        block.Children.Add(new BoxView { BackgroundColor = Color.FromArgb("#E5E7EB"), HeightRequest = 1, Margin = new Thickness(0, 8) });
        AddRow("Total:", order.Total, strong: true);
        return block;
    }
}
