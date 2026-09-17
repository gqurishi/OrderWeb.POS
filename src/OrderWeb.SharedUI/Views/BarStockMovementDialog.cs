using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

public enum BarStockMovementKind
{
    Receive,
    Count,
    Waste
}

/// <summary>Picker row for movement dialogs (host maps from board DTOs).</summary>
public sealed record BarStockPickerItem(
    string StockId,
    string Name,
    string StockUnit,
    decimal OnHand,
    decimal PackSize,
    decimal ParLevel);

public sealed record BarStockMovementDialogResult(
    bool Confirmed,
    string? StockId = null,
    decimal Qty = 0,
    string? InputUnit = null,
    string? NoteOrReason = null,
    string? IdempotencyKey = null);

/// <summary>
/// SharedUI Receive / Count / Waste dialog — chrome only; host applies Mother ledger.
/// Busy / double-tap guarded Confirm.
/// </summary>
public sealed class BarStockMovementDialog : ContentView
{
    private readonly Label _titleLabel = MotherDialogVisuals.Title("Receive Stock");
    private readonly Label _hintLabel = MotherDialogVisuals.Message();
    private readonly Picker _stockPicker = new()
    {
        Title = "Stock item",
        FontSize = 15,
        HeightRequest = 44
    };
    private readonly Entry _qtyEntry = new()
    {
        Placeholder = "Quantity",
        Keyboard = Keyboard.Numeric,
        FontSize = 16,
        HeightRequest = 44,
        BackgroundColor = Colors.Transparent
    };
    private readonly Picker _unitPicker = new()
    {
        Title = "Unit",
        FontSize = 15,
        HeightRequest = 44,
        IsVisible = false
    };
    private readonly Entry _noteEntry = new()
    {
        Placeholder = "Note (optional)",
        FontSize = 15,
        HeightRequest = 44,
        BackgroundColor = Colors.Transparent
    };
    private readonly Label _statusLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#DC2626"),
        IsVisible = false,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Button _confirmButton = new()
    {
        Text = "Confirm",
        Style = null,
        BackgroundColor = Color.FromArgb("#059669"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 52,
        FontSize = 16
    };
    private readonly Button _cancelButton = new()
    {
        Text = "Cancel",
        Style = null,
        BackgroundColor = Color.FromArgb("#E2E8F0"),
        TextColor = Color.FromArgb("#334155"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 48,
        FontSize = 15
    };

    private TaskCompletionSource<BarStockMovementDialogResult>? _tcs;
    private ContentPage? _page;
    private BarStockMovementKind _kind;
    private IReadOnlyList<BarStockPickerItem> _items = Array.Empty<BarStockPickerItem>();
    private bool _busy;
    private string _idempotencyKey = Guid.NewGuid().ToString("N");
    private Func<BarStockMovementDialogResult, Task<(bool Ok, string? Error)>>? _applyAsync;

    public BarStockMovementDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        _confirmButton.Clicked += async (_, _) => await ConfirmAsync();
        _cancelButton.Clicked += async (_, _) => await CompleteAsync(new BarStockMovementDialogResult(false));
        _stockPicker.SelectedIndexChanged += (_, _) => RefreshUnitOptions();

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                MotherDialogVisuals.IconCircle("📦", size: 56, fontSize: 24, motherBlue: true),
                _titleLabel,
                _hintLabel,
                Field("Item", _stockPicker),
                Field("Quantity", _qtyEntry),
                Field("Unit", _unitPicker),
                Field("Note / reason", _noteEntry),
                _statusLabel,
                _confirmButton,
                _cancelButton
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(420, 480, body, padding: 24, maxHeight: 640));
    }

    public Task<BarStockMovementDialogResult> ShowAsync(
        ContentPage page,
        BarStockMovementKind kind,
        IReadOnlyList<BarStockPickerItem> items,
        Func<BarStockMovementDialogResult, Task<(bool Ok, string? Error)>> applyAsync,
        string? stickyIdempotencyKey = null)
    {
        _tcs = new TaskCompletionSource<BarStockMovementDialogResult>();
        _page = page;
        _kind = kind;
        _items = items ?? Array.Empty<BarStockPickerItem>();
        _applyAsync = applyAsync;
        _busy = false;
        _idempotencyKey = string.IsNullOrWhiteSpace(stickyIdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : stickyIdempotencyKey.Trim();
        _statusLabel.IsVisible = false;
        _qtyEntry.Text = string.Empty;
        _noteEntry.Text = string.Empty;
        SetBusy(false);

        _titleLabel.Text = kind switch
        {
            BarStockMovementKind.Receive => "Receive Stock",
            BarStockMovementKind.Count => "Stock Count",
            BarStockMovementKind.Waste => "Waste / Breakage",
            _ => "Stock movement"
        };
        _hintLabel.Text = kind switch
        {
            BarStockMovementKind.Receive => "Add qty to on-hand. Cases convert using pack size.",
            BarStockMovementKind.Count => "Set absolute on-hand. Variance is audited.",
            BarStockMovementKind.Waste => "Subtract qty. Reason is required.",
            _ => string.Empty
        };
        _noteEntry.Placeholder = kind == BarStockMovementKind.Waste ? "Reason (required)" : "Note (optional)";
        _confirmButton.Text = kind switch
        {
            BarStockMovementKind.Receive => "Receive",
            BarStockMovementKind.Count => "Save count",
            BarStockMovementKind.Waste => "Log waste",
            _ => "Confirm"
        };
        _confirmButton.BackgroundColor = Color.FromArgb(kind == BarStockMovementKind.Waste ? "#BE185D" : "#059669");

        _stockPicker.Items.Clear();
        foreach (var item in _items)
        {
            _stockPicker.Items.Add($"{item.Name} · {item.OnHand:0.###} {item.StockUnit}");
        }

        if (_stockPicker.Items.Count > 0)
        {
            _stockPicker.SelectedIndex = 0;
        }

        RefreshUnitOptions();

        AttachOverlay(page);
        IsVisible = true;
        return _tcs.Task;
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

    private void RefreshUnitOptions()
    {
        _unitPicker.Items.Clear();
        var item = SelectedItem();
        if (_kind != BarStockMovementKind.Receive || item is null)
        {
            _unitPicker.IsVisible = false;
            return;
        }

        _unitPicker.IsVisible = true;
        _unitPicker.Items.Add(item.StockUnit);
        // Case convert only when pack_size is bottles-per-case (small).
        // Admin Add stock stores ml/bottle in pack_size (e.g. 750) — do not treat as case.
        var packLooksLikeCaseSize = item.PackSize > 1m && item.PackSize < 100m;
        if (packLooksLikeCaseSize &&
            !string.Equals(item.StockUnit, "case", StringComparison.OrdinalIgnoreCase))
        {
            _unitPicker.Items.Add("case");
        }

        _unitPicker.SelectedIndex = 0;
    }

    private BarStockPickerItem? SelectedItem()
    {
        var index = _stockPicker.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            return null;
        }

        return _items[index];
    }

    private async Task ConfirmAsync()
    {
        if (_busy)
        {
            return;
        }

        var item = SelectedItem();
        if (item is null)
        {
            ShowError("Select a stock item.");
            return;
        }

        if (!decimal.TryParse((_qtyEntry.Text ?? string.Empty).Trim(), out var qty))
        {
            ShowError("Enter a valid quantity.");
            return;
        }

        if (_kind == BarStockMovementKind.Waste && string.IsNullOrWhiteSpace(_noteEntry.Text))
        {
            ShowError("Waste reason is required.");
            return;
        }

        if (_kind == BarStockMovementKind.Count && qty < 0)
        {
            ShowError("Counted quantity cannot be negative.");
            return;
        }

        if (_kind != BarStockMovementKind.Count && qty <= 0)
        {
            ShowError("Quantity must be greater than zero.");
            return;
        }

        string? inputUnit = null;
        if (_kind == BarStockMovementKind.Receive && _unitPicker.IsVisible && _unitPicker.SelectedIndex >= 0)
        {
            inputUnit = _unitPicker.Items[_unitPicker.SelectedIndex];
        }

        var draft = new BarStockMovementDialogResult(
            Confirmed: true,
            StockId: item.StockId,
            Qty: qty,
            InputUnit: inputUnit,
            NoteOrReason: string.IsNullOrWhiteSpace(_noteEntry.Text) ? null : _noteEntry.Text.Trim(),
            IdempotencyKey: _idempotencyKey);

        if (_applyAsync is null)
        {
            await CompleteAsync(draft);
            return;
        }

        _busy = true;
        SetBusy(true);
        _statusLabel.IsVisible = false;
        try
        {
            var (ok, error) = await _applyAsync(draft);
            if (ok)
            {
                await CompleteAsync(draft);
                return;
            }

            ShowError(error ?? "Could not update stock.");
            // Keep sticky idempotency key for retry.
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
        _confirmButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = !busy;
        _stockPicker.IsEnabled = !busy;
        _qtyEntry.IsEnabled = !busy;
        _unitPicker.IsEnabled = !busy;
        _noteEntry.IsEnabled = !busy;
        _confirmButton.Text = busy
            ? "Working…"
            : _kind switch
            {
                BarStockMovementKind.Receive => "Receive",
                BarStockMovementKind.Count => "Save count",
                BarStockMovementKind.Waste => "Log waste",
                _ => "Confirm"
            };
    }

    private async Task CompleteAsync(BarStockMovementDialogResult result)
    {
        IsVisible = false;
        if (_page?.Content is Grid grid && grid.Children.Contains(this))
        {
            grid.Children.Remove(this);
        }

        _tcs?.TrySetResult(result);
        await Task.CompletedTask;
    }

    private static Border Field(string label, View input) =>
        new()
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = label,
                        FontSize = 12,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    input
                }
            }
        };
}
