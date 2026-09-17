using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

public sealed record BarStockCreateDialogResult(
    bool Confirmed,
    string? Sku = null,
    string? Name = null,
    string? Section = null,
    decimal MlPerBottle = 750m,
    /// <summary>Bottles added now (Stock IN) — becomes opening on-hand.</summary>
    decimal StockInBottles = 0m,
    decimal LowLevel = 0m,
    decimal MaxLevel = 0m);

/// <summary>
/// Admin Add stock dialog — SKU, category, bottle↔ml calculator. Host saves to Mother.
/// </summary>
public sealed class BarStockCreateDialog : ContentView
{
    private readonly Entry _skuEntry = FieldEntry("e.g. ROSE-750");
    private readonly Entry _nameEntry = FieldEntry("e.g. House Rosé");
    private readonly Entry _customSectionEntry = FieldEntry("Or type a category…");
    private readonly Entry _mlEntry = FieldEntry("750");
    private readonly Entry _stockInEntry = FieldEntry("e.g. 12");
    private readonly Entry _lowEntry = FieldEntry("e.g. 3");
    private readonly Entry _maxEntry = FieldEntry("e.g. 10");
    private readonly Label _calcLabel = new()
    {
        FontSize = 13,
        FontFamily = "OpenSansSemibold",
        TextColor = Color.FromArgb("#0F766E"),
        LineBreakMode = LineBreakMode.WordWrap
    };
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
        Text = "Add stock",
        Style = null,
        BackgroundColor = Color.FromArgb("#0F766E"),
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

    private TaskCompletionSource<BarStockCreateDialogResult>? _tcs;
    private ContentPage? _page;
    private string _selectedSection = BarStockSections.Wine;
    private bool _busy;
    private Func<BarStockCreateDialogResult, Task<(bool Ok, string? Error)>>? _saveAsync;

    public BarStockCreateDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        _mlEntry.Keyboard = Keyboard.Numeric;
        _stockInEntry.Keyboard = Keyboard.Numeric;
        _lowEntry.Keyboard = Keyboard.Numeric;
        _maxEntry.Keyboard = Keyboard.Numeric;
        _mlEntry.Text = "750";
        _stockInEntry.Text = "0";
        _lowEntry.Text = "";
        _maxEntry.Text = "";
        _mlEntry.TextChanged += (_, _) => RefreshCalc();
        _stockInEntry.TextChanged += (_, _) => RefreshCalc();
        _lowEntry.TextChanged += (_, _) => RefreshCalc();
        _maxEntry.TextChanged += (_, _) => RefreshCalc();
        _customSectionEntry.TextChanged += (_, _) =>
        {
            if (_selectedSection != BarStockSections.Other)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_customSectionEntry.Text))
            {
                // Keep Other chip selected while typing a custom name.
                ApplySectionChipStyles();
            }
        };

        foreach (var (key, label) in BarStockSections.PresetChips)
        {
            var chip = BuildChip(key, label);
            _chips[key] = chip;
            _sectionChipRow.Children.Add(chip);
        }

        ApplySectionChipStyles();
        RefreshCalc();

        _saveButton.Clicked += async (_, _) => await ConfirmAsync();
        _cancelButton.Clicked += async (_, _) => await CompleteAsync(new BarStockCreateDialogResult(false));
        _saveButton.BackgroundColor = Color.FromArgb("#2563EB");

        // Wide single-screen layout — no scroll.
        var identityRow = TwoCol(
            Labeled("SKU / inventory number *", _skuEntry),
            Labeled("Name *", _nameEntry));

        _customCategoryBlock = Labeled("Custom category", _customSectionEntry);
        _customCategoryBlock.IsVisible = false;

        var categoryBlock = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                FieldCaption("Category"),
                _sectionChipRow,
                _customCategoryBlock
            }
        };

        var measuresRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        measuresRow.Add(Labeled("1 bottle = ml *", _mlEntry), 0);
        measuresRow.Add(Labeled("Stock IN (bottles) *", _stockInEntry), 1);

        var limitsRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        limitsRow.Add(Labeled("Low limit *", _lowEntry), 0);
        limitsRow.Add(Labeled("Max limit *", _maxEntry), 1);

        var buttonsRow = TwoCol(_saveButton, _cancelButton, leftWeight: 1.5, rightWeight: 1);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14,
            VerticalOptions = LayoutOptions.Center
        };
        var icon = MotherDialogVisuals.IconCircle("＋", size: 44, fontSize: 20, motherBlue: true);
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
                    Text = "Add stock",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A")
                },
                new Label
                {
                    Text = "SKU · category · bottle size · Stock IN · Low / Max",
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
                categoryBlock,
                measuresRow,
                limitsRow,
                new Border
                {
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#BFDBFE"),
                    BackgroundColor = Color.FromArgb("#EFF6FF"),
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(14, 10),
                    Content = _calcLabel
                },
                _statusLabel,
                buttonsRow
            }
        };
        _calcLabel.TextColor = Color.FromArgb("#1D4ED8");

        var panel = MotherDialogVisuals.Panel(720, 860, body, padding: 22);
        panel.VerticalOptions = LayoutOptions.Center;
        panel.HorizontalOptions = LayoutOptions.Center;

        var overlay = MotherDialogVisuals.OverlayGrid(panel);
        overlay.HorizontalOptions = LayoutOptions.Fill;
        overlay.VerticalOptions = LayoutOptions.Fill;
        Content = overlay;
    }

    public Task<BarStockCreateDialogResult> ShowAsync(
        ContentPage page,
        string? defaultSection,
        Func<BarStockCreateDialogResult, Task<(bool Ok, string? Error)>> saveAsync)
    {
        _tcs = new TaskCompletionSource<BarStockCreateDialogResult>();
        _page = page;
        _saveAsync = saveAsync;
        _busy = false;
        _statusLabel.IsVisible = false;
        _skuEntry.Text = string.Empty;
        _nameEntry.Text = string.Empty;
        _customSectionEntry.Text = string.Empty;
        _mlEntry.Text = "750";
        _stockInEntry.Text = "0";
        _lowEntry.Text = string.Empty;
        _maxEntry.Text = string.Empty;
        _selectedSection = string.IsNullOrWhiteSpace(defaultSection) ||
                           string.Equals(defaultSection, BarStockSections.All, StringComparison.OrdinalIgnoreCase)
            ? BarStockSections.Wine
            : BarStockSections.Normalize(defaultSection);
        if (!BarStockSections.IsPreset(_selectedSection) ||
            string.Equals(_selectedSection, BarStockSections.Other, StringComparison.OrdinalIgnoreCase))
        {
            // Non-preset reopen → treat as Other + typed name; Other chip alone → blank custom box.
            if (!BarStockSections.IsPreset(_selectedSection))
            {
                _customSectionEntry.Text = BarStockSections.DisplayName(_selectedSection);
                _selectedSection = BarStockSections.Other;
            }
        }

        ApplySectionChipStyles();
        RefreshCustomCategoryVisibility();
        RefreshCalc();
        SetBusy(false);

        AttachOverlay(page);
        IsVisible = true;
        return _tcs.Task;
    }

    private void AttachOverlay(ContentPage page)
    {
        // Never add into InventoryPage's Auto,* Grid — that only spans TopBar and breaks layout.
        // Wrap the whole page content once, then cover it with a full-size overlay.
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
        // Keep overlay on top.
        host.Children.Remove(this);
        host.Children.Add(this);
    }

    private void RefreshCalc()
    {
        _ = decimal.TryParse((_mlEntry.Text ?? string.Empty).Trim(), out var ml);
        _ = decimal.TryParse((_stockInEntry.Text ?? string.Empty).Trim(), out var bottles);
        _ = decimal.TryParse((_lowEntry.Text ?? string.Empty).Trim(), out var low);
        _ = decimal.TryParse((_maxEntry.Text ?? string.Empty).Trim(), out var max);
        if (ml <= 0)
        {
            _calcLabel.Text = "Set ml per bottle (e.g. 750).";
            return;
        }

        var total = bottles * ml;
        var stockPart = bottles <= 0
            ? $"1 bottle = {ml:0.###} ml · enter Stock IN bottles"
            : $"Stock IN {bottles:0.###} bottles × {ml:0.###} ml = {total:0.###} ml on hand";
        var limitPart = low > 0 && max > 0
            ? $" · Suggest when ≤ {low:0.###}, order up to {max:0.###}"
            : " · set Low + Max (required)";
        _calcLabel.Text = stockPart + limitPart;
    }

    private async Task ConfirmAsync()
    {
        if (_busy)
        {
            return;
        }

        var sku = (_skuEntry.Text ?? string.Empty).Trim();
        var name = (_nameEntry.Text ?? string.Empty).Trim();
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

        if (!decimal.TryParse((_mlEntry.Text ?? string.Empty).Trim(), out var ml) || ml <= 0)
        {
            ShowError("ml per bottle must be greater than zero.");
            return;
        }

        if (!decimal.TryParse((_stockInEntry.Text ?? string.Empty).Trim(), out var stockIn) || stockIn < 0)
        {
            ShowError("Stock IN must be zero or more bottles.");
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

        string section;
        if (string.Equals(_selectedSection, BarStockSections.Other, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_customSectionEntry.Text))
        {
            section = BarStockSections.Normalize(_customSectionEntry.Text);
        }
        else
        {
            section = _selectedSection;
        }

        var draft = new BarStockCreateDialogResult(
            true, sku, name, section, ml, stockIn, low, max);

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
        _saveButton.Text = busy ? "Saving…" : "Add stock";
    }

    private async Task CompleteAsync(BarStockCreateDialogResult result)
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
        var showCustom = string.Equals(_selectedSection, BarStockSections.Other, StringComparison.OrdinalIgnoreCase);
        if (_customCategoryBlock is not null)
        {
            _customCategoryBlock.IsVisible = showCustom;
        }

        if (!showCustom)
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

    private static Label FieldCaption(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569")
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

    private static View Labeled(string label, View input) =>
        new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                FieldCaption(label),
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
}
