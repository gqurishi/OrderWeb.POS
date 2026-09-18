using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

public sealed record BarStockWasteDialogResult(
    bool Confirmed,
    string? StockId = null,
    decimal QtyInStockUnit = 0m,
    string? Reason = null,
    string? IdempotencyKey = null);

/// <summary>
/// Waste chrome: search name (must pick a real stock) → bottles or ml → reason → host deducts.
/// </summary>
public sealed class BarStockWasteDialog : ContentView
{
    private static readonly string[] ReasonChips =
        ["Break", "Extra can", "Over-pour", "Other"];

    private readonly Label _titleLabel = MotherDialogVisuals.Title("Waste");
    private readonly Label _hintLabel = MotherDialogVisuals.Message(
        "Type 2+ letters to find stock, select it, then bottles/ml and reason.");
    private readonly Label _searchDisplay = new()
    {
        Text = "Stock name…",
        FontSize = 15,
        TextColor = Color.FromArgb("#94A3B8"),
        VerticalTextAlignment = TextAlignment.Center,
        VerticalOptions = LayoutOptions.Center,
        HeightRequest = 44,
        InputTransparent = true
    };
    private string _searchText = string.Empty;
    private readonly VerticalStackLayout _suggestionList = new() { Spacing = 4 };
    private readonly Label _searchHintLabel = new()
    {
        Text = "Type at least 2 letters to search.",
        FontSize = 12,
        TextColor = Color.FromArgb("#94A3B8"),
        Margin = new Thickness(0, 2, 0, 0)
    };
    private readonly Label _selectedLabel = new()
    {
        FontSize = 14,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#0F766E"),
        IsVisible = false,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly VerticalStackLayout _detailsBlock;
    private readonly Label _qtyPrompt = new()
    {
        Text = "How much?",
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#475569"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly HorizontalStackLayout _unitRow = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.Center
    };
    private readonly HorizontalStackLayout _qtyChipRow = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.Center
    };
    private readonly Label _qtyLabel = new()
    {
        FontSize = 26,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#0F172A"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _previewLabel = new()
    {
        FontSize = 13,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#BE185D"),
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly HorizontalStackLayout _reasonRow = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.Center
    };
    private readonly Label _otherReasonDisplay = new()
    {
        Text = "Other reason…",
        FontSize = 14,
        TextColor = Color.FromArgb("#94A3B8"),
        VerticalTextAlignment = TextAlignment.Center,
        HeightRequest = 40,
        InputTransparent = true
    };
    private string _otherReasonText = string.Empty;
    private View? _otherReasonField;
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
        Text = "Log waste",
        Style = null,
        BackgroundColor = Color.FromArgb("#BE185D"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 48,
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

    private TaskCompletionSource<BarStockWasteDialogResult>? _tcs;
    private ContentPage? _page;
    private IReadOnlyList<BarStockPickerItem> _items = Array.Empty<BarStockPickerItem>();
    private BarStockPickerItem? _selected;
    private bool _useMl;
    private decimal _qtyInput;
    private string _reasonKey = string.Empty;
    private bool _busy;
    private string _idempotencyKey = Guid.NewGuid().ToString("N");
    private Func<BarStockWasteDialogResult, Task<(bool Ok, string? Error)>>? _applyAsync;
    private Border? _bottleUnitChip;
    private Border? _mlUnitChip;
    private readonly Dictionary<string, Border> _reasonBorders = new(StringComparer.OrdinalIgnoreCase);

    public BarStockWasteDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _confirmButton.Clicked += async (_, _) => await ConfirmAsync();
        _cancelButton.Clicked += async (_, _) =>
        {
            SharedTouchKeyboard.SuppressAllBriefly();
            await CompleteAsync(new BarStockWasteDialogResult(false));
        };

        _bottleUnitChip = UnitChip("Bottles", bottles: true);
        _mlUnitChip = UnitChip("ml", bottles: false);
        _unitRow.Children.Add(_bottleUnitChip);
        _unitRow.Children.Add(_mlUnitChip);

        foreach (var reason in ReasonChips)
        {
            var chip = ReasonChip(reason);
            _reasonBorders[reason] = chip;
            _reasonRow.Children.Add(chip);
        }

        var buttons = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        buttons.Add(_confirmButton, 0);
        buttons.Add(_cancelButton, 1);

        _otherReasonField = Field("Details", BuildTappableField(_otherReasonDisplay, OpenOtherReasonKeyboardAsync));
        _otherReasonField.IsVisible = false;

        _detailsBlock = new VerticalStackLayout
        {
            Spacing = 10,
            IsVisible = false,
            Children =
            {
                _qtyPrompt,
                _unitRow,
                _qtyChipRow,
                _qtyLabel,
                new Border
                {
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#FBCFE8"),
                    BackgroundColor = Color.FromArgb("#FDF2F8"),
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(12, 8),
                    Content = _previewLabel
                },
                new Label
                {
                    Text = "Reason",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#475569"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                _reasonRow,
                _otherReasonField
            }
        };

        var body = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                MotherDialogVisuals.IconCircle("W", size: 44, fontSize: 18, motherBlue: false),
                _titleLabel,
                _hintLabel,
                Field("Name", BuildTappableField(_searchDisplay, OpenSearchKeyboardAsync)),
                _searchHintLabel,
                _suggestionList,
                _selectedLabel,
                _detailsBlock,
                _statusLabel,
                buttons
            }
        };

        var panel = MotherDialogVisuals.Panel(460, 540, body, padding: 20, maxHeight: 720);
        panel.VerticalOptions = LayoutOptions.Center;
        panel.HorizontalOptions = LayoutOptions.Center;
        Content = MotherDialogVisuals.OverlayGrid(panel);
    }

    public Task<BarStockWasteDialogResult> ShowAsync(
        ContentPage page,
        IReadOnlyList<BarStockPickerItem> items,
        Func<BarStockWasteDialogResult, Task<(bool Ok, string? Error)>> applyAsync,
        string? stickyIdempotencyKey = null)
    {
        _tcs = new TaskCompletionSource<BarStockWasteDialogResult>();
        _page = page;
        _items = items ?? Array.Empty<BarStockPickerItem>();
        _applyAsync = applyAsync;
        _busy = false;
        _selected = null;
        _useMl = false;
        _qtyInput = 0;
        _reasonKey = string.Empty;
        _idempotencyKey = string.IsNullOrWhiteSpace(stickyIdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : stickyIdempotencyKey.Trim();
        SetSearchText(string.Empty);
        SetOtherReasonText(string.Empty);
        if (_otherReasonField is not null)
        {
            _otherReasonField.IsVisible = false;
        }

        _selectedLabel.IsVisible = false;
        _detailsBlock.IsVisible = false;
        _searchHintLabel.IsVisible = true;
        _statusLabel.IsVisible = false;
        SetBusy(false);
        ApplyUnitChipStyles();
        ApplyReasonChipStyles();
        RebuildQtyChips();
        RefreshSuggestions();
        RefreshPreview();
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

    private const int MinSearchChars = 2;

    private void RefreshSuggestions()
    {
        _suggestionList.Children.Clear();
        if (_selected is not null)
        {
            _searchHintLabel.IsVisible = false;
            return;
        }

        var q = _searchText.Trim();
        if (q.Length < MinSearchChars)
        {
            _searchHintLabel.Text = "Type at least 2 letters to search.";
            _searchHintLabel.IsVisible = true;
            return;
        }

        _searchHintLabel.IsVisible = false;
        var matches = _items
            .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        if (matches.Count == 0)
        {
            _suggestionList.Children.Add(new Label
            {
                Text = "No match — try another name.",
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8")
            });
            return;
        }

        foreach (var item in matches)
        {
            var local = item;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SelectItem(local);
            var row = new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#E2E8F0"),
                BackgroundColor = Colors.White,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12, 8),
                Content = new Label
                {
                    Text = $"{item.Name} · now {item.OnHand:0.###} {item.StockUnit}",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#0F172A"),
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            };
            row.GestureRecognizers.Add(tap);
            _suggestionList.Children.Add(row);
        }
    }

    private void SelectItem(BarStockPickerItem item)
    {
        _selected = item;
        SetSearchText(item.Name);
        _selectedLabel.Text = $"Selected · now {item.OnHand:0.###} {item.StockUnit}" +
                              (item.PackSize > 0 ? $" · {item.PackSize:0.###} ml/btl" : string.Empty);
        _selectedLabel.IsVisible = true;
        _searchHintLabel.IsVisible = false;
        _suggestionList.Children.Clear();
        _detailsBlock.IsVisible = true;

        // ml only when bottle stock has a real pack volume.
        var canMl = string.Equals(item.StockUnit, "bottle", StringComparison.OrdinalIgnoreCase) &&
                    item.PackSize > 1m;
        if (_mlUnitChip is not null)
        {
            _mlUnitChip.IsVisible = canMl;
        }

        if (!canMl)
        {
            _useMl = false;
        }

        ApplyUnitChipStyles();
        RebuildQtyChips();
        RefreshPreview();
    }

    private void RebuildQtyChips()
    {
        _qtyChipRow.Children.Clear();
        if (_useMl)
        {
            foreach (var ml in new[] { 25, 50, 175, 250 })
            {
                _qtyChipRow.Children.Add(QtyChip(ml, $"{ml} ml"));
            }
        }
        else
        {
            foreach (var n in new[] { 1, 2, 3, 6 })
            {
                _qtyChipRow.Children.Add(QtyChip(n, $"+{n}"));
            }
        }

        _qtyChipRow.Children.Add(OtherQtyChip());
    }

    private Button QtyChip(decimal value, string label)
    {
        var button = new Button
        {
            Text = label,
            Style = null,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#334155"),
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            CornerRadius = 14,
            Padding = new Thickness(12, 6),
            BorderColor = Color.FromArgb("#CBD5E1"),
            BorderWidth = 1.5,
            HeightRequest = 36
        };
        button.Clicked += (_, _) =>
        {
            _qtyInput = value;
            RefreshPreview();
        };
        return button;
    }

    private Button OtherQtyChip()
    {
        var button = new Button
        {
            Text = "Other…",
            Style = null,
            BackgroundColor = Color.FromArgb("#FDF2F8"),
            TextColor = Color.FromArgb("#BE185D"),
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            CornerRadius = 14,
            Padding = new Thickness(12, 6),
            BorderColor = Color.FromArgb("#FBCFE8"),
            BorderWidth = 1.5,
            HeightRequest = 36
        };
        button.Clicked += async (_, _) => await PickOtherQtyAsync();
        return button;
    }

    private async Task PickOtherQtyAsync()
    {
        if (_page is null || _busy)
        {
            return;
        }

        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(_useMl ? "ml to waste" : "Bottles to waste", "Done");
        keyboard.SetPlaceholder(_useMl ? "e.g. 175" : "e.g. 1");
        keyboard.SetNumericMode(VirtualKeyboardNumericMode.Quantity, minimum: 0.001m, maximum: 9999);
        keyboard.SetInitialText(_qtyInput > 0 ? _qtyInput.ToString("0.###") : string.Empty);
        var value = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (decimal.TryParse(value, out var qty) && qty > 0)
        {
            _qtyInput = qty;
            RefreshPreview();
        }
    }

    private Border UnitChip(string label, bool bottles)
    {
        var text = new Label
        {
            Text = label,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var chip = new Border
        {
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(14, 6),
            Content = text
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _useMl = !bottles;
            _qtyInput = 0;
            ApplyUnitChipStyles();
            RebuildQtyChips();
            RefreshPreview();
        };
        chip.GestureRecognizers.Add(tap);
        return chip;
    }

    private void ApplyUnitChipStyles()
    {
        StyleUnitChip(_bottleUnitChip, !_useMl);
        StyleUnitChip(_mlUnitChip, _useMl);
    }

    private static void StyleUnitChip(Border? chip, bool selected)
    {
        if (chip is null)
        {
            return;
        }

        chip.BackgroundColor = Color.FromArgb(selected ? "#BE185D" : "#FFFFFF");
        chip.Stroke = Color.FromArgb(selected ? "#BE185D" : "#CBD5E1");
        if (chip.Content is Label label)
        {
            label.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#334155");
        }
    }

    private Border ReasonChip(string reason)
    {
        var text = new Label
        {
            Text = reason,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center
        };
        var chip = new Border
        {
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 6),
            Content = text
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _reasonKey = reason;
            if (_otherReasonField is not null)
            {
                _otherReasonField.IsVisible = string.Equals(reason, "Other", StringComparison.OrdinalIgnoreCase);
            }

            ApplyReasonChipStyles();
        };
        chip.GestureRecognizers.Add(tap);
        return chip;
    }

    private void ApplyReasonChipStyles()
    {
        foreach (var (key, chip) in _reasonBorders)
        {
            var selected = string.Equals(key, _reasonKey, StringComparison.OrdinalIgnoreCase);
            chip.BackgroundColor = Color.FromArgb(selected ? "#BE185D" : "#FFFFFF");
            chip.Stroke = Color.FromArgb(selected ? "#BE185D" : "#CBD5E1");
            if (chip.Content is Label label)
            {
                label.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#334155");
            }
        }
    }

    private void RefreshPreview()
    {
        if (_selected is null)
        {
            _qtyLabel.Text = "—";
            _previewLabel.Text = "—";
            return;
        }

        if (_qtyInput <= 0)
        {
            _qtyLabel.Text = "—";
            _previewLabel.Text = _useMl
                ? "Tap ml chips or Other…"
                : "Tap bottle chips or Other…";
            return;
        }

        if (!TryResolveQtyInStockUnit(out var stockQty, out var error))
        {
            _qtyLabel.Text = _useMl ? $"{_qtyInput:0.###} ml" : $"+{_qtyInput:0.###}";
            _previewLabel.Text = error ?? "Invalid qty.";
            return;
        }

        var after = _selected.OnHand - stockQty;
        _qtyLabel.Text = _useMl
            ? $"{_qtyInput:0.###} ml (−{stockQty:0.###} {_selected.StockUnit})"
            : $"−{_qtyInput:0.###} {_selected.StockUnit}";
        _previewLabel.Text = after < 0
            ? $"Too much — only {_selected.OnHand:0.###} {_selected.StockUnit} on hand."
            : $"Now {_selected.OnHand:0.###} → {after:0.###} {_selected.StockUnit} after waste";
    }

    private bool TryResolveQtyInStockUnit(out decimal stockQty, out string? error)
    {
        stockQty = 0;
        error = null;
        if (_selected is null)
        {
            error = "Pick a stock item.";
            return false;
        }

        if (_qtyInput <= 0)
        {
            error = "Enter how much was wasted.";
            return false;
        }

        if (_useMl)
        {
            if (_selected.PackSize <= 0)
            {
                error = "This stock has no ml/bottle size — use bottles.";
                return false;
            }

            stockQty = _qtyInput / _selected.PackSize;
            return true;
        }

        stockQty = _qtyInput;
        return true;
    }

    private string? ResolveReasonText()
    {
        if (string.IsNullOrWhiteSpace(_reasonKey))
        {
            return null;
        }

        if (string.Equals(_reasonKey, "Other", StringComparison.OrdinalIgnoreCase))
        {
            var other = _otherReasonText.Trim();
            return string.IsNullOrWhiteSpace(other) ? null : other;
        }

        return _reasonKey;
    }

    private async Task ConfirmAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_selected is null)
        {
            ShowError("Search and pick a stock name from the list.");
            return;
        }

        // Re-resolve by name in case search text drifted.
        var name = _searchText.Trim();
        var match = _items.FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.StockId, _selected.StockId, StringComparison.Ordinal));
        if (match is null)
        {
            ShowError("Name must match a tracked stock item.");
            return;
        }

        _selected = match;

        if (!TryResolveQtyInStockUnit(out var stockQty, out var qtyError))
        {
            ShowError(qtyError ?? "Enter a valid quantity.");
            return;
        }

        if (stockQty > _selected.OnHand)
        {
            ShowError($"Cannot waste more than on-hand ({_selected.OnHand:0.###} {_selected.StockUnit}).");
            return;
        }

        var reason = ResolveReasonText();
        if (string.IsNullOrWhiteSpace(reason))
        {
            ShowError(string.Equals(_reasonKey, "Other", StringComparison.OrdinalIgnoreCase)
                ? "Type the other reason."
                : "Pick a waste reason.");
            return;
        }

        var draft = new BarStockWasteDialogResult(true, _selected.StockId, stockQty, reason, _idempotencyKey);
        if (_applyAsync is null)
        {
            await CompleteAsync(draft);
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            var (ok, error) = await _applyAsync(draft);
            if (ok)
            {
                SharedTouchKeyboard.SuppressAllBriefly();
                await CompleteAsync(draft);
                return;
            }

            ShowError(error ?? "Could not log waste.");
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
        _confirmButton.Text = busy ? "Saving…" : "Log waste";
        _confirmButton.Opacity = busy ? 0.7 : 1;
    }

    private async Task CompleteAsync(BarStockWasteDialogResult result)
    {
        if (_page?.Content is Grid host && host.Children.Contains(this))
        {
            host.Children.Remove(this);
            if (host.Children.Count == 1)
            {
                _page.Content = host.Children[0] as View;
            }
        }

        IsVisible = false;
        _tcs?.TrySetResult(result);
        await Task.CompletedTask;
    }

    private static View Field(string label, View content) =>
        new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = label,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#475569")
                },
                new Border
                {
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#E2E8F0"),
                    BackgroundColor = Color.FromArgb("#F8FAFC"),
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(10, 0),
                    Content = content
                }
            }
        };

    private static View BuildTappableField(Label display, Func<Task> onTap)
    {
        // Windows Entry swallows taps — use a button overlay so SharedUI keyboard opens.
        var open = new Button
        {
            Style = null,
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            Margin = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        open.Clicked += async (_, _) => await onTap();
        return new Grid
        {
            MinimumHeightRequest = 44,
            Children = { display, open }
        };
    }

    private void SetSearchText(string value)
    {
        _searchText = value ?? string.Empty;
        var has = !string.IsNullOrWhiteSpace(_searchText);
        _searchDisplay.Text = has ? _searchText : "Stock name…";
        _searchDisplay.TextColor = Color.FromArgb(has ? "#0F172A" : "#94A3B8");
    }

    private void SetOtherReasonText(string value)
    {
        _otherReasonText = value ?? string.Empty;
        var has = !string.IsNullOrWhiteSpace(_otherReasonText);
        _otherReasonDisplay.Text = has ? _otherReasonText : "Other reason…";
        _otherReasonDisplay.TextColor = Color.FromArgb(has ? "#0F172A" : "#94A3B8");
    }

    private async Task OpenSearchKeyboardAsync()
    {
        if (_page is null || _busy)
        {
            return;
        }

        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt("Stock name", "Done");
        keyboard.SetTextMode(VirtualKeyboardTextMode.Name);
        keyboard.SetPlaceholder("Type to search…");
        keyboard.SetInitialText(_searchText);
        var result = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (result is null)
        {
            return;
        }

        SetSearchText(result.Trim());
        if (_selected is not null &&
            !string.Equals(_searchText, _selected.Name, StringComparison.OrdinalIgnoreCase))
        {
            _selected = null;
            _selectedLabel.IsVisible = false;
            _detailsBlock.IsVisible = false;
        }

        RefreshSuggestions();
        RefreshPreview();
    }

    private async Task OpenOtherReasonKeyboardAsync()
    {
        if (_page is null || _busy)
        {
            return;
        }

        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt("Waste reason", "Done");
        keyboard.SetTextMode(VirtualKeyboardTextMode.Notes);
        keyboard.SetPlaceholder("Describe the waste…");
        keyboard.SetInitialText(_otherReasonText);
        var result = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (result is not null)
        {
            SetOtherReasonText(result.Trim());
        }
    }
}
