using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Views;

public sealed record BarStockQuickAddDialogResult(
    bool Confirmed,
    string? StockId = null,
    decimal QtyBottles = 0m);

/// <summary>
/// Stock IN picker — chips only (no Entry), so Search keyboard never steals focus.
/// “Other…” opens the shared numeric keypad with a clear title.
/// </summary>
public sealed class BarStockQuickAddDialog : ContentView
{
    private readonly Label _titleLabel = new()
    {
        FontSize = 20,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#0F172A"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _metaLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#64748B"),
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Label _qtyLabel = new()
    {
        FontSize = 28,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#0F172A"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _calcLabel = new()
    {
        FontSize = 13,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#1D4ED8"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _statusLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#DC2626"),
        IsVisible = false,
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _promptLabel = new()
    {
        Text = "How many bottles?",
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#475569"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Button _saveButton = new()
    {
        Text = "+ Add bottles",
        Style = null,
        BackgroundColor = Color.FromArgb("#2563EB"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 46,
        FontSize = 15
    };
    private readonly Button _cancelButton = new()
    {
        Text = "Cancel",
        Style = null,
        BackgroundColor = Color.FromArgb("#E2E8F0"),
        TextColor = Color.FromArgb("#334155"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 46,
        FontSize = 15
    };

    private TaskCompletionSource<BarStockQuickAddDialogResult>? _tcs;
    private ContentPage? _page;
    private string _stockId = string.Empty;
    private decimal _mlPerBottle = 750m;
    private decimal _onHand;
    private decimal _qty;
    private bool _busy;
    private bool _deliveryFromSuggest;
    private Func<BarStockQuickAddDialogResult, Task<(bool Ok, string? Error)>>? _saveAsync;

    public BarStockQuickAddDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        _saveButton.Clicked += async (_, _) => await ConfirmAsync();
        _cancelButton.Clicked += async (_, _) =>
        {
            SharedTouchKeyboard.SuppressAllBriefly();
            await CompleteAsync(new BarStockQuickAddDialogResult(false));
        };

        var chips = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                QtyChip(6),
                QtyChip(12),
                QtyChip(24),
                OtherChip()
            }
        };

        var buttons = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        buttons.Add(_saveButton, 0);
        buttons.Add(_cancelButton, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                MotherDialogVisuals.IconCircle("＋", size: 44, fontSize: 20, motherBlue: true),
                _titleLabel,
                _metaLabel,
                _promptLabel,
                chips,
                _qtyLabel,
                new Border
                {
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#BFDBFE"),
                    BackgroundColor = Color.FromArgb("#EFF6FF"),
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(12, 10),
                    Content = _calcLabel
                },
                _statusLabel,
                buttons
            }
        };

        var panel = MotherDialogVisuals.Panel(460, 540, body, padding: 22);
        panel.VerticalOptions = LayoutOptions.Center;
        panel.HorizontalOptions = LayoutOptions.Center;
        var overlay = MotherDialogVisuals.OverlayGrid(panel);
        overlay.HorizontalOptions = LayoutOptions.Fill;
        overlay.VerticalOptions = LayoutOptions.Fill;
        Content = overlay;
    }

    public Task<BarStockQuickAddDialogResult> ShowAsync(
        ContentPage page,
        BarStockBoardRowPresentation row,
        Func<BarStockQuickAddDialogResult, Task<(bool Ok, string? Error)>> saveAsync,
        bool deliveryFromSuggest = false)
    {
        _tcs = new TaskCompletionSource<BarStockQuickAddDialogResult>();
        _page = page;
        _saveAsync = saveAsync;
        _busy = false;
        _deliveryFromSuggest = deliveryFromSuggest;
        _stockId = row.StockId;
        _onHand = row.OnHand;
        _mlPerBottle = row.PackSize > 0 ? row.PackSize : 750m;
        _qty = deliveryFromSuggest && row.SuggestOrderQty > 0m ? row.SuggestOrderQty : 0m;
        _titleLabel.Text = row.Name;
        _metaLabel.Text = string.IsNullOrWhiteSpace(row.Sku)
            ? $"Now {_onHand:0.###} {row.Unit} · {_mlPerBottle:0.###} ml/bottle"
            : $"{row.Sku} · now {_onHand:0.###} {row.Unit} · {_mlPerBottle:0.###} ml/bottle";
        if (deliveryFromSuggest)
        {
            _promptLabel.Text = row.SuggestOrderQty > 0m
                ? $"How many came in? (suggest {row.SuggestOrderQty:0.###})"
                : "How many came in?";
            _saveButton.Text = "Add to stock";
            _saveButton.BackgroundColor = Color.FromArgb("#047857");
        }
        else
        {
            _promptLabel.Text = "How many bottles?";
            _saveButton.Text = "+ Add bottles";
            _saveButton.BackgroundColor = Color.FromArgb("#2563EB");
        }

        _statusLabel.IsVisible = false;
        SetBusy(false);
        RefreshCalc();
        SharedTouchKeyboard.SuppressAllBriefly(400);
        AttachOverlay(page);
        IsVisible = true;
        return _tcs.Task;
    }

    private Button QtyChip(int qty)
    {
        var button = new Button
        {
            Text = $"+{qty}",
            Style = null,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#334155"),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            CornerRadius = 16,
            Padding = new Thickness(14, 8),
            BorderColor = Color.FromArgb("#CBD5E1"),
            BorderWidth = 1.5,
            HeightRequest = 40
        };
        button.Clicked += (_, _) =>
        {
            _qty = qty;
            RefreshCalc();
        };
        return button;
    }

    private Button OtherChip()
    {
        var button = new Button
        {
            Text = "Other…",
            Style = null,
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            TextColor = Color.FromArgb("#1D4ED8"),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            CornerRadius = 16,
            Padding = new Thickness(14, 8),
            BorderColor = Color.FromArgb("#BFDBFE"),
            BorderWidth = 1.5,
            HeightRequest = 40
        };
        button.Clicked += async (_, _) => await PickOtherAmountAsync();
        return button;
    }

    private async Task PickOtherAmountAsync()
    {
        if (_page is null || _busy)
        {
            return;
        }

        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(
            _deliveryFromSuggest ? "Bottles that arrived" : "Bottles to add (Stock IN)",
            "Done");
        keyboard.SetPlaceholder("e.g. 18");
        keyboard.SetNumericMode(VirtualKeyboardNumericMode.Quantity, minimum: 1, maximum: 9999);
        keyboard.SetInitialText(_qty > 0 ? _qty.ToString("0.###") : string.Empty);
        var value = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (decimal.TryParse(value, out var qty) && qty > 0)
        {
            _qty = qty;
            RefreshCalc();
        }
    }

    private void RefreshCalc()
    {
        if (_qty <= 0)
        {
            _qtyLabel.Text = "—";
            _calcLabel.Text = _deliveryFromSuggest
                ? "Tap +6 / +12 / +24 or Other… for bottles that arrived"
                : "Tap +6 / +12 / +24 or Other…";
            return;
        }

        var after = _onHand + _qty;
        var ml = _qty * _mlPerBottle;
        _qtyLabel.Text = $"+{_qty:0.###}";
        _calcLabel.Text = _deliveryFromSuggest
            ? $"Delivery +{_qty:0.###} bottles ({ml:0.###} ml) → {_onHand:0.###} → {after:0.###} on hand"
            : $"+{_qty:0.###} bottles ({ml:0.###} ml) → {_onHand:0.###} → {after:0.###} on hand";
    }

    private async Task ConfirmAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_qty <= 0)
        {
            ShowError(_deliveryFromSuggest
                ? "Pick how many bottles came in."
                : "Pick how many bottles to add.");
            return;
        }

        var draft = new BarStockQuickAddDialogResult(true, _stockId, _qty);
        if (_saveAsync is null)
        {
            await CompleteAsync(draft);
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            var (ok, error) = await _saveAsync(draft);
            if (ok)
            {
                SharedTouchKeyboard.SuppressAllBriefly();
                await CompleteAsync(draft);
                return;
            }

            ShowError(error ?? "Could not add stock.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.IsVisible = true;
    }

    private void SetBusy(bool busy)
    {
        _saveButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = !busy;
        _saveButton.Text = busy ? "Saving…" : "+ Add bottles";
    }

    private async Task CompleteAsync(BarStockQuickAddDialogResult result)
    {
        IsVisible = false;
        if (_page?.Content is Grid grid && grid.Children.Contains(this))
        {
            grid.Children.Remove(this);
        }

        _tcs?.TrySetResult(result);
        await Task.CompletedTask;
    }

    private void AttachOverlay(ContentPage page)
    {
        if (page.Content is Grid host && host.Children.Contains(this))
        {
            ApplyFullBleed(host);
            return;
        }

        var outer = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        if (page.Content != null)
        {
            outer.Children.Add(page.Content);
        }

        outer.Children.Add(this);
        page.Content = outer;
        ApplyFullBleed(outer);
    }

    private void ApplyFullBleed(Grid host)
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        var rows = Math.Max(1, host.RowDefinitions.Count);
        var cols = Math.Max(1, host.ColumnDefinitions.Count);
        Grid.SetRow(this, 0);
        Grid.SetColumn(this, 0);
        Grid.SetRowSpan(this, rows);
        Grid.SetColumnSpan(this, cols);
        host.Children.Remove(this);
        host.Children.Add(this);
    }
}
