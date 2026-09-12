using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// Mother-chrome Pay By Items dialog (SharedUI — Mother + Client Order Place).
/// Back / CLOSE / scrim always dismiss cleanly so the till cannot get stuck.
/// </summary>
public sealed class PaymentPayByItemsDialog : ContentView
{
    private const double PreferredWidth = 820;
    private const double PreferredHeight = 760;

    private readonly Border _dialogCard;
    private readonly VerticalStackLayout _itemsContainer = new() { Spacing = 10 };
    private readonly Label _emptyItemsLabel = new()
    {
        Text = "No payable items on this order.",
        FontSize = 15,
        TextColor = Color.FromArgb("#64748B"),
        HorizontalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 24),
        IsVisible = false
    };
    private readonly Label _hintLabel = new()
    {
        Text = "Tip: tap + (or the item name) to choose what this guest pays now.",
        FontSize = 13,
        TextColor = Color.FromArgb("#64748B"),
        Margin = new Thickness(0, 4, 0, 0)
    };
    private readonly Label _selectedItemsLabel = new() { Text = "No items selected", FontSize = 14, TextColor = Color.FromArgb("#64748B") };
    private readonly Label _selectedSubtotalLabel = new() { Text = "Selected items: £0.00", FontSize = 14, TextColor = Color.FromArgb("#334155") };
    private readonly Label _adjustmentsLabel = new() { Text = "Service/discount share: £0.00", FontSize = 14, TextColor = Color.FromArgb("#334155") };
    private readonly Label _amountDueLabel = new()
    {
        Text = "£0.00",
        FontSize = 34,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#065F46"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _remainingAfterLabel = new()
    {
        Text = "Remaining after: £0.00",
        FontSize = 13,
        TextColor = Color.FromArgb("#64748B"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Button _takePaymentButton;
    private readonly Button _closeButton;
    private readonly Button _selectAllButton;
    private readonly Button _clearButton;

    private readonly List<LineState> _lines = new();
    private TaskCompletionSource<PaymentPayByItemsResult>? _tcs;
    private Grid? _parent;
    private decimal _orderSubtotal;
    private decimal _serviceCharge;
    private decimal _deliveryFee;
    private decimal _discount;
    private decimal _remainingBalance;
    private decimal _amountDue;
    private bool _completed;

    public PaymentPayByItemsDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        InputTransparent = false;
        ZIndex = 16000;

        var scrim = new BoxView
        {
            Color = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(CompleteCancel)
        });

        _closeButton = new Button
        {
            Text = "CLOSE",
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            CornerRadius = 14,
            WidthRequest = 100,
            MinimumWidthRequest = 100,
            HeightRequest = 45,
            MinimumHeightRequest = 45,
            Padding = new Thickness(12, 0),
            ZIndex = 20
        };
        _closeButton.Clicked += (_, _) => CompleteCancel();

        _takePaymentButton = new Button
        {
            Text = "Take Payment",
            BackgroundColor = Color.FromArgb("#9CA3AF"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 14,
            HeightRequest = 58,
            MinimumHeightRequest = 58,
            IsEnabled = false
        };
        _takePaymentButton.Clicked += (_, _) => CompleteSuccess();

        _selectAllButton = new Button
        {
            Text = "Select all",
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            TextColor = Color.FromArgb("#1D4ED8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            CornerRadius = 10,
            HeightRequest = 40,
            Padding = new Thickness(12, 0)
        };
        _selectAllButton.Clicked += (_, _) => SelectAll();

        _clearButton = new Button
        {
            Text = "Clear",
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            TextColor = Color.FromArgb("#334155"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            CornerRadius = 10,
            HeightRequest = 40,
            Padding = new Thickness(12, 0)
        };
        _clearButton.Clicked += (_, _) => ClearSelection();

        var titleBlock = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Pay By Items",
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A")
                },
                new Label
                {
                    Text = "Select the items or quantities this customer wants to pay for.",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#64748B")
                }
            }
        };

        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 12
        };
        header.Add(titleBlock, 0);
        header.Add(_closeButton, 1);

        var columns = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Star),
                new(new GridLength(88)),
                new(new GridLength(140)),
                new(new GridLength(96))
            },
            ColumnSpacing = 12,
            Padding = new Thickness(12, 0)
        };
        columns.Add(MakeCol("ITEM"), 0);
        columns.Add(MakeCol("QTY"), 1);
        columns.Add(MakeCol("PAY"), 2);
        columns.Add(MakeCol("TOTAL"), 3);

        var quickActions = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _selectAllButton, _clearButton }
        };

        var amountCard = new Border
        {
            BackgroundColor = Color.FromArgb("#ECFDF5"),
            Stroke = Color.FromArgb("#10B981"),
            StrokeThickness = 2,
            Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = "AMOUNT DUE",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#047857"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    _amountDueLabel
                }
            }
        };

        var footer = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 18,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    VerticalOptions = LayoutOptions.End,
                    Children = { _hintLabel, _selectedItemsLabel, _selectedSubtotalLabel, _adjustmentsLabel }
                }
            }
        };
        footer.Add(new VerticalStackLayout
        {
            WidthRequest = 260,
            Spacing = 10,
            Children = { amountCard, _remainingAfterLabel, _takePaymentButton }
        }, 1);

        _dialogCard = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = 28,
            WidthRequest = PreferredWidth,
            MaximumWidthRequest = PreferredWidth,
            MaximumHeightRequest = PreferredHeight,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Opacity = 0.16f,
                Radius = 20,
                Offset = new Point(0, 8)
            },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new(GridLength.Auto),
                    new(GridLength.Auto),
                    new(GridLength.Auto),
                    new(GridLength.Star),
                    new(GridLength.Auto)
                },
                RowSpacing = 14
            }
        };

        var body = (Grid)_dialogCard.Content!;
        body.Add(header, 0, 0);
        body.Add(columns, 0, 1);
        body.Add(quickActions, 0, 2);
        body.Add(new ScrollView
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { _emptyItemsLabel, _itemsContainer }
            }
        }, 0, 3);
        body.Add(footer, 0, 4);

        Content = new Grid
        {
            Padding = 12,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { scrim, _dialogCard }
        };

        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    public void SetOrder(
        IEnumerable<PaymentPayByItemsLine> items,
        decimal orderSubtotal,
        decimal serviceCharge,
        decimal deliveryFee,
        decimal discount,
        decimal remainingBalance)
    {
        _serviceCharge = Math.Max(0, serviceCharge);
        _deliveryFee = Math.Max(0, deliveryFee);
        _discount = Math.Max(0, discount);
        _remainingBalance = Math.Max(0, remainingBalance);

        _lines.Clear();
        _itemsContainer.Children.Clear();

        foreach (var item in items.Where(i => i.Quantity > 0 && i.UnitAmount >= 0))
        {
            var line = new LineState(item);
            _lines.Add(line);
            _itemsContainer.Children.Add(CreateLineView(line));
        }

        var linesSubtotal = _lines.Sum(l => Math.Round(l.Item.UnitAmount * l.Item.Quantity, 2));
        _orderSubtotal = orderSubtotal > 0.009m ? Math.Max(0, orderSubtotal) : Math.Max(0, linesSubtotal);

        _emptyItemsLabel.IsVisible = _lines.Count == 0;
        _selectAllButton.IsEnabled = _lines.Count > 0;
        _clearButton.IsEnabled = _lines.Count > 0;
        UpdateSummary();
    }

    public async Task<PaymentPayByItemsResult> ShowAsync(ContentPage? hostPage = null)
    {
        _completed = false;
        _tcs = new TaskCompletionSource<PaymentPayByItemsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return new PaymentPayByItemsResult { Success = false };
        }

        ApplyResponsiveLayout();
        return await _tcs.Task;
    }

    private View CreateLineView(LineState line)
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#D8E1ED"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 12 }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Star),
                new(new GridLength(88)),
                new(new GridLength(140)),
                new(new GridLength(96))
            },
            ColumnSpacing = 12
        };

        var itemStack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = line.Item.Name,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#111827")
                },
                new Label
                {
                    Text = line.Item.Detail ?? string.Empty,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#64748B"),
                    IsVisible = !string.IsNullOrWhiteSpace(line.Item.Detail),
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
        };
        itemStack.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => AdjustQuantity(line, +1))
        });
        grid.Add(itemStack, 0);

        grid.Add(new Label
        {
            Text = $"x{line.Item.Quantity}",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#334155"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        }, 1);

        var selector = new Grid
        {
            ColumnDefinitions = { new(new GridLength(44)), new(GridLength.Star), new(new GridLength(44)) },
            ColumnSpacing = 8
        };
        var minus = QtyButton("−");
        minus.Clicked += (_, _) => AdjustQuantity(line, -1);
        selector.Add(minus, 0);
        line.SelectedQuantityLabel = new Label
        {
            Text = "0",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#111827"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        selector.Add(line.SelectedQuantityLabel, 1);
        var plus = QtyButton("+");
        plus.Clicked += (_, _) => AdjustQuantity(line, +1);
        selector.Add(plus, 2);
        grid.Add(selector, 2);

        line.AmountLabel = new Label
        {
            Text = "£0.00",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#059669"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End
        };
        grid.Add(line.AmountLabel, 3);

        border.Content = grid;
        return border;
    }

    private void SelectAll()
    {
        foreach (var line in _lines)
        {
            line.SelectedQuantity = line.Item.Quantity;
            RefreshLine(line, refreshSummary: false);
        }

        UpdateSummary();
    }

    private void ClearSelection()
    {
        foreach (var line in _lines)
        {
            line.SelectedQuantity = 0;
            RefreshLine(line, refreshSummary: false);
        }

        UpdateSummary();
    }

    private void AdjustQuantity(LineState line, int delta)
    {
        if (_completed)
        {
            return;
        }

        var next = Math.Clamp(line.SelectedQuantity + delta, 0, line.Item.Quantity);
        if (next == line.SelectedQuantity)
        {
            return;
        }

        line.SelectedQuantity = next;
        RefreshLine(line);
    }

    private void RefreshLine(LineState line, bool refreshSummary = true)
    {
        if (line.SelectedQuantityLabel is not null)
        {
            line.SelectedQuantityLabel.Text = line.SelectedQuantity.ToString();
        }

        if (line.AmountLabel is not null)
        {
            line.AmountLabel.Text = $"£{line.SelectedAmount:F2}";
        }

        if (refreshSummary)
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var selectedQuantity = _lines.Sum(l => l.SelectedQuantity);
        var selectedSubtotal = _lines.Sum(l => l.SelectedAmount);
        var shareRatio = _orderSubtotal > 0 ? selectedSubtotal / _orderSubtotal : 0m;
        var serviceShare = Math.Round(_serviceCharge * shareRatio, 2, MidpointRounding.AwayFromZero);
        var deliveryShare = Math.Round(_deliveryFee * shareRatio, 2, MidpointRounding.AwayFromZero);
        var discountShare = Math.Round(_discount * shareRatio, 2, MidpointRounding.AwayFromZero);
        var adjustments = serviceShare + deliveryShare - discountShare;
        _amountDue = Math.Round(selectedSubtotal + adjustments, 2, MidpointRounding.AwayFromZero);
        if (_amountDue > _remainingBalance)
        {
            _amountDue = _remainingBalance;
        }

        if (_amountDue < 0)
        {
            _amountDue = 0;
        }

        _selectedItemsLabel.Text = selectedQuantity == 0
            ? "No items selected"
            : $"{selectedQuantity} item(s) selected";
        _selectedSubtotalLabel.Text = $"Selected items: £{selectedSubtotal:F2}";
        _adjustmentsLabel.Text = $"Service/discount share: £{adjustments:F2}";
        _amountDueLabel.Text = $"£{_amountDue:F2}";
        _remainingAfterLabel.Text = $"Remaining after: £{Math.Max(0, _remainingBalance - _amountDue):F2}";
        _hintLabel.IsVisible = selectedQuantity == 0 && _lines.Count > 0;

        var canTake = _amountDue > 0.009m;
        _takePaymentButton.IsEnabled = canTake && !_completed;
        _takePaymentButton.BackgroundColor = canTake
            ? Color.FromArgb("#059669")
            : Color.FromArgb("#9CA3AF");
    }

    private void CompleteSuccess()
    {
        if (_completed || _amountDue <= 0.009m)
        {
            return;
        }

        Finish(new PaymentPayByItemsResult
        {
            Success = true,
            Amount = _amountDue,
            SelectedItems = _lines
                .Where(l => l.SelectedQuantity > 0)
                .Select(l => new PaymentPayByItemsSelection
                {
                    ItemId = l.Item.ItemId,
                    ItemName = l.Item.Name,
                    Quantity = l.SelectedQuantity,
                    Amount = l.SelectedAmount
                })
                .ToList()
        });
    }

    private void CompleteCancel()
    {
        if (_completed)
        {
            return;
        }

        Finish(new PaymentPayByItemsResult { Success = false });
    }

    private void Finish(PaymentPayByItemsResult result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _closeButton.IsEnabled = false;
        _takePaymentButton.IsEnabled = false;

        var parent = _parent;
        _parent = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PaymentOverlayHost.Detach(this, parent);
            _tcs?.TrySetResult(result);
        });
    }

    private void ApplyResponsiveLayout()
    {
        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var tablet = width < 1280;
        var margin = tablet ? 12d : 16d;
        var availableWidth = Math.Max(320, width - (margin * 2));
        var cardWidth = Math.Min(PreferredWidth, availableWidth);

        _dialogCard.WidthRequest = cardWidth;
        _dialogCard.MaximumWidthRequest = cardWidth;
        _dialogCard.Margin = new Thickness(margin);
        _dialogCard.Padding = new Thickness(tablet ? 20 : 28);
        _dialogCard.MaximumHeightRequest = Math.Max(
            360,
            Math.Min(PreferredHeight, height - (margin * 2) - 24));
    }

    private static Label MakeCol(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#64748B")
    };

    private static Button QtyButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#E0ECFF"),
        TextColor = Color.FromArgb("#1D4ED8"),
        FontSize = 20,
        FontAttributes = FontAttributes.Bold,
        WidthRequest = 44,
        HeightRequest = 44,
        MinimumWidthRequest = 44,
        MinimumHeightRequest = 44,
        Padding = 0,
        CornerRadius = 10
    };

    private sealed class LineState
    {
        public LineState(PaymentPayByItemsLine item) => Item = item;
        public PaymentPayByItemsLine Item { get; }
        public int SelectedQuantity { get; set; }
        public Label? SelectedQuantityLabel { get; set; }
        public Label? AmountLabel { get; set; }
        public decimal SelectedAmount =>
            Math.Round(Item.UnitAmount * SelectedQuantity, 2, MidpointRounding.AwayFromZero);
    }
}
