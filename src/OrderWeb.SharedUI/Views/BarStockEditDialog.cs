using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

public sealed record BarStockEditDialogResult(
    bool Confirmed,
    bool Deleted = false,
    string? StockId = null,
    string? Sku = null,
    string? Name = null,
    string? Section = null,
    decimal MlPerBottle = 750m,
    decimal LowLevel = 0m,
    decimal MaxLevel = 0m);

/// <summary>
/// Admin full edit — same fields as Add stock (SKU, name, category, ml) + Delete.
/// Does not change on-hand (use + Stock / Receive for qty).
/// </summary>
public sealed class BarStockEditDialog : ContentView
{
    private readonly Entry _skuEntry = FieldEntry("e.g. ROSE-750");
    private readonly Entry _nameEntry = FieldEntry("e.g. House Rosé");
    private readonly Entry _customSectionEntry = FieldEntry("Or type a category…");
    private readonly Entry _mlEntry = FieldEntry("750");
    private readonly Entry _lowEntry = FieldEntry("e.g. 3");
    private readonly Entry _maxEntry = FieldEntry("e.g. 10");
    private readonly Label _statusLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#DC2626"),
        IsVisible = false,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly HorizontalStackLayout _sectionChipRow = new() { Spacing = 8 };
    private readonly Dictionary<string, Border> _chips = new(StringComparer.OrdinalIgnoreCase);
    private View? _customCategoryBlock;
    private readonly Button _saveButton = new()
    {
        Text = "Save",
        Style = null,
        BackgroundColor = Color.FromArgb("#2563EB"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 46,
        FontSize = 15,
        HorizontalOptions = LayoutOptions.Fill
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
        FontSize = 15,
        HorizontalOptions = LayoutOptions.Fill
    };
    private readonly Button _deleteButton = new()
    {
        Text = "Delete stock",
        Style = null,
        BackgroundColor = Color.FromArgb("#FEF2F2"),
        TextColor = Color.FromArgb("#B91C1C"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 44,
        FontSize = 14,
        HorizontalOptions = LayoutOptions.Fill,
        BorderColor = Color.FromArgb("#FECACA"),
        BorderWidth = 1
    };

    private TaskCompletionSource<BarStockEditDialogResult>? _tcs;
    private ContentPage? _page;
    private string _stockId = string.Empty;
    private string _selectedSection = BarStockSections.Wine;
    private bool _busy;
    private bool _deleteArmed;
    private Func<BarStockEditDialogResult, Task<(bool Ok, string? Error)>>? _saveAsync;

    public BarStockEditDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        _mlEntry.Keyboard = Keyboard.Numeric;
        _lowEntry.Keyboard = Keyboard.Numeric;
        _maxEntry.Keyboard = Keyboard.Numeric;
        _saveButton.Clicked += async (_, _) => await ConfirmAsync(deleted: false);
        _cancelButton.Clicked += async (_, _) => await CompleteAsync(new BarStockEditDialogResult(false));
        _deleteButton.Clicked += async (_, _) => await OnDeleteClickedAsync();

        foreach (var (key, label) in BarStockSections.PresetChips)
        {
            var chip = BuildChip(key, label);
            _chips[key] = chip;
            _sectionChipRow.Children.Add(chip);
        }

        DisableNestedKeyboard(_skuEntry);
        DisableNestedKeyboard(_nameEntry);
        DisableNestedKeyboard(_customSectionEntry);
        DisableNestedKeyboard(_mlEntry);
        DisableNestedKeyboard(_lowEntry);
        DisableNestedKeyboard(_maxEntry);
        _skuEntry.Focused += async (_, _) => await EditTextFieldAsync(_skuEntry, "SKU / inventory number", VirtualKeyboardTextMode.Text);
        _nameEntry.Focused += async (_, _) => await EditTextFieldAsync(_nameEntry, "Stock name", VirtualKeyboardTextMode.Text);
        _customSectionEntry.Focused += async (_, _) => await EditTextFieldAsync(_customSectionEntry, "Custom category", VirtualKeyboardTextMode.Text);
        _mlEntry.Focused += async (_, _) => await EditMlAsync();
        _lowEntry.Focused += async (_, _) => await EditNumberAsync(_lowEntry, "Low limit");
        _maxEntry.Focused += async (_, _) => await EditNumberAsync(_maxEntry, "Max limit");

        _customCategoryBlock = Labeled("Custom category", _customSectionEntry);
        _customCategoryBlock.IsVisible = false;

        var identityRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14
        };
        identityRow.Add(Labeled("SKU / inventory number *", _skuEntry), 0);
        identityRow.Add(Labeled("Name *", _nameEntry), 1);

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

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14
        };
        var icon = MotherDialogVisuals.IconCircle("✎", size: 44, fontSize: 18, motherBlue: true);
        icon.Margin = new Thickness(0);
        header.Add(icon, 0);
        header.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Edit stock",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A")
                },
                new Label
                {
                    Text = "Same as Add — SKU · category · bottle size. Qty stays via + Stock.",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#64748B")
                }
            }
        }, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                header,
                identityRow,
                new Label
                {
                    Text = "Category",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#475569")
                },
                _sectionChipRow,
                _customCategoryBlock,
                Labeled("1 bottle = ml *", _mlEntry),
                TwoCol(Labeled("Low limit *", _lowEntry), Labeled("Max limit *", _maxEntry)),
                _statusLabel,
                buttons,
                _deleteButton
            }
        };

        var panel = MotherDialogVisuals.Panel(720, 900, body, padding: 22);
        panel.VerticalOptions = LayoutOptions.Center;
        panel.HorizontalOptions = LayoutOptions.Center;
        var overlay = MotherDialogVisuals.OverlayGrid(panel);
        overlay.HorizontalOptions = LayoutOptions.Fill;
        overlay.VerticalOptions = LayoutOptions.Fill;
        Content = overlay;
    }

    public Task<BarStockEditDialogResult> ShowAsync(
        ContentPage page,
        BarStockBoardRowPresentation row,
        Func<BarStockEditDialogResult, Task<(bool Ok, string? Error)>> saveAsync)
    {
        _tcs = new TaskCompletionSource<BarStockEditDialogResult>();
        _page = page;
        _saveAsync = saveAsync;
        _busy = false;
        _deleteArmed = false;
        _stockId = row.StockId;
        _skuEntry.Text = row.Sku ?? string.Empty;
        _nameEntry.Text = row.Name;
        _mlEntry.Text = row.PackSize > 0 ? row.PackSize.ToString("0.###") : "750";
        _lowEntry.Text = row.LowLevel > 0 ? row.LowLevel.ToString("0.###") : string.Empty;
        _maxEntry.Text = row.MaxLevel > 0 ? row.MaxLevel.ToString("0.###") : string.Empty;
        _customSectionEntry.Text = string.Empty;
        _statusLabel.IsVisible = false;
        _deleteButton.Text = "Delete stock";
        _deleteButton.BackgroundColor = Color.FromArgb("#FEF2F2");
        _deleteButton.TextColor = Color.FromArgb("#B91C1C");

        var key = string.IsNullOrWhiteSpace(row.SectionKey)
            ? BarStockSections.Other
            : BarStockSections.Normalize(row.SectionKey);
        if (BarStockSections.IsPreset(key) && key != BarStockSections.Other)
        {
            _selectedSection = key;
        }
        else if (key == BarStockSections.Other)
        {
            _selectedSection = BarStockSections.Other;
        }
        else
        {
            _selectedSection = BarStockSections.Other;
            _customSectionEntry.Text = BarStockSections.DisplayName(key);
        }

        ApplySectionChipStyles();
        RefreshCustomCategoryVisibility();
        SetBusy(false);
        SharedTouchKeyboard.SuppressAllBriefly(400);
        AttachOverlay(page);
        IsVisible = true;
        return _tcs.Task;
    }

    private async Task OnDeleteClickedAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!_deleteArmed)
        {
            _deleteArmed = true;
            _deleteButton.Text = "Tap again to confirm delete";
            _deleteButton.BackgroundColor = Color.FromArgb("#DC2626");
            _deleteButton.TextColor = Colors.White;
            ShowError("This removes the stock from Bar Inventory (soft delete). Tap Delete again to confirm.");
            return;
        }

        await ConfirmAsync(deleted: true);
    }

    private static void DisableNestedKeyboard(Entry field)
    {
        SharedTouchKeyboard.SetEnabled(field, false);
        field.HandlerChanged += (_, _) => SharedTouchKeyboard.SetEnabled(field, false);
    }

    private async Task EditTextFieldAsync(Entry field, string title, VirtualKeyboardTextMode mode)
    {
        if (_busy || _page is null)
        {
            return;
        }

        field.Unfocus();
        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(title, "Done");
        keyboard.SetTextMode(mode);
        keyboard.SetPlaceholder(field.Placeholder);
        keyboard.SetInitialText(field.Text ?? string.Empty);
        var value = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (value is not null)
        {
            field.Text = value;
        }
    }

    private async Task EditMlAsync()
    {
        if (_busy || _page is null)
        {
            return;
        }

        _mlEntry.Unfocus();
        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt("ml per bottle", "Done");
        keyboard.SetPlaceholder("e.g. 750");
        keyboard.SetNumericMode(VirtualKeyboardNumericMode.WholeNumber, minimum: 1, maximum: 5000);
        keyboard.SetInitialText(_mlEntry.Text ?? "750");
        var value = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (value is not null)
        {
            _mlEntry.Text = value;
        }
    }

    private async Task ConfirmAsync(bool deleted)
    {
        if (_busy)
        {
            return;
        }

        string? sku = null;
        string? name = null;
        string? section = null;
        var ml = 750m;

        if (!deleted)
        {
            sku = (_skuEntry.Text ?? string.Empty).Trim();
            name = (_nameEntry.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                ShowError("Enter a SKU / inventory number.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Enter a stock name.");
                return;
            }

            if (!decimal.TryParse((_mlEntry.Text ?? string.Empty).Trim(), out ml) || ml <= 0)
            {
                ShowError("ml per bottle must be greater than zero.");
                return;
            }

            if (!decimal.TryParse((_lowEntry.Text ?? string.Empty).Trim(), out var low) || low <= 0)
            {
                ShowError("Low limit is required and must be greater than zero.");
                return;
            }

            if (!decimal.TryParse((_maxEntry.Text ?? string.Empty).Trim(), out var max) || max <= 0)
            {
                ShowError("Max limit is required and must be greater than zero.");
                return;
            }

            if (low > max)
            {
                ShowError("Low limit cannot be greater than Max limit.");
                return;
            }

            section = string.Equals(_selectedSection, BarStockSections.Other, StringComparison.OrdinalIgnoreCase) &&
                      !string.IsNullOrWhiteSpace(_customSectionEntry.Text)
                ? BarStockSections.Normalize(_customSectionEntry.Text)
                : _selectedSection;

            var draft = new BarStockEditDialogResult(
                true,
                false,
                _stockId,
                sku,
                name,
                section,
                ml,
                low,
                max);

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

                ShowError(error ?? "Could not save.");
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

            return;
        }

        var deleteDraft = new BarStockEditDialogResult(
            true,
            true,
            _stockId);

        if (_saveAsync is null)
        {
            await CompleteAsync(deleteDraft);
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            var (ok, error) = await _saveAsync(deleteDraft);
            if (ok)
            {
                SharedTouchKeyboard.SuppressAllBriefly();
                await CompleteAsync(deleteDraft);
                return;
            }

            ShowError(error ?? "Could not delete.");
            _deleteArmed = false;
            _deleteButton.Text = "Delete stock";
            _deleteButton.BackgroundColor = Color.FromArgb("#FEF2F2");
            _deleteButton.TextColor = Color.FromArgb("#B91C1C");
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

    private async Task EditNumberAsync(Entry field, string title)
    {
        if (_busy || _page is null)
        {
            return;
        }

        field.Unfocus();
        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(title, "Done");
        keyboard.SetPlaceholder("e.g. 10");
        keyboard.SetNumericMode(VirtualKeyboardNumericMode.WholeNumber, minimum: 1, maximum: 100000);
        keyboard.SetInitialText(field.Text ?? string.Empty);
        var value = await keyboard.ShowAsync(_page);
        SharedTouchKeyboard.SuppressAllBriefly();
        if (value is not null)
        {
            field.Text = value;
        }
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.TextColor = Color.FromArgb("#DC2626");
        _statusLabel.IsVisible = true;
    }

    private void SetBusy(bool busy)
    {
        _saveButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = !busy;
        _deleteButton.IsEnabled = !busy;
        _saveButton.Text = busy ? "Saving…" : "Save";
    }

    private async Task CompleteAsync(BarStockEditDialogResult result)
    {
        IsVisible = false;
        if (_page?.Content is Grid grid && grid.Children.Contains(this))
        {
            grid.Children.Remove(this);
        }

        _tcs?.TrySetResult(result);
        await Task.CompletedTask;
    }

    private Border BuildChip(string key, string label)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _selectedSection = key;
            if (!string.Equals(key, BarStockSections.Other, StringComparison.OrdinalIgnoreCase))
            {
                _customSectionEntry.Text = string.Empty;
            }

            ApplySectionChipStyles();
            RefreshCustomCategoryVisibility();
        };
        var border = new Border
        {
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12, 7),
            Content = new Label
            {
                Text = label,
                FontFamily = "OpenSansSemibold",
                FontSize = 12
            }
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void RefreshCustomCategoryVisibility()
    {
        var show = string.Equals(_selectedSection, BarStockSections.Other, StringComparison.OrdinalIgnoreCase);
        if (_customCategoryBlock is not null)
        {
            _customCategoryBlock.IsVisible = show;
        }

        if (!show)
        {
            _customSectionEntry.Text = string.Empty;
        }
    }

    private void ApplySectionChipStyles()
    {
        foreach (var (key, chip) in _chips)
        {
            var selected = string.Equals(key, _selectedSection, StringComparison.OrdinalIgnoreCase);
            chip.BackgroundColor = Color.FromArgb(selected ? "#0F766E" : "#FFFFFF");
            chip.Stroke = Color.FromArgb(selected ? "#0F766E" : "#CBD5E1");
            if (chip.Content is Label label)
            {
                label.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#334155");
            }
        }
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

    private static Entry FieldEntry(string placeholder) =>
        new()
        {
            Placeholder = placeholder,
            FontSize = 14,
            TextColor = Color.FromArgb("#0F172A"),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 38
        };

    private static View Labeled(string label, View input) =>
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
                    HeightRequest = 42,
                    Content = input
                }
            }
        };

    private static Grid TwoCol(View left, View right, double leftWeight = 1, double rightWeight = 1)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(leftWeight, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(rightWeight, GridUnitType.Star))
            },
            ColumnSpacing = 14
        };
        grid.Add(left, 0);
        grid.Add(right, 1);
        return grid;
    }
}
