using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls.OrderPlace;

// Mother-visual-parity Order Place dialogs (MORE / NOTES / VOID / Discount / Transfer / Fire Course).
// Host-neutral: no Mother or Client references, no HTTP. Hosts supply data and validate/send to
// Mother themselves. Attached via OrderPlaceDialogPresenter (Mother DialogOverlayHelper parity).

/// <summary>Mother-parity option for the Order Place MORE tile grid.</summary>
public sealed record OrderPlaceMoreOption(string Text, bool IsEnabled = true, bool IsDestructive = false);

/// <summary>Mother-parity discount dialog result. Host still validates/sends to Mother API.</summary>
public sealed record OrderPlaceDiscountResult(
    bool Applied,
    bool Removed,
    bool IsPercentage,
    decimal Amount,
    string? Reason,
    string? Pin = null);

/// <summary>Host-neutral table option for the transfer dialog.</summary>
public sealed record OrderPlaceTableOption(string Id, string Label);

/// <summary>Shared Mother-look chrome builders reused by the dialogs below.</summary>
internal static class MotherDialogVisuals
{
    public static Grid OverlayGrid(View panel) => new()
    {
        Padding = 16,
        Children = { panel }
    };

    public static Border Panel(double width, double maxWidth, View content, double padding = 30, double maxHeight = 0)
    {
        var border = new Border
        {
            Padding = padding,
            WidthRequest = width,
            MaximumWidthRequest = maxWidth,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = content,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Color.FromArgb("#0F172A"), Opacity = 0.16f, Radius = 20, Offset = new Point(0, 8) }
        };
        if (maxHeight > 0)
        {
            border.MaximumHeightRequest = maxHeight;
        }

        border.Use(Border.BackgroundColorProperty, "OwSurface");
        border.Use(Border.StrokeProperty, "OwBorder");
        return border;
    }

    public static Border IconCircle(string icon, string accentKey = "OwPrimary", double size = 74, double fontSize = 30, bool motherBlue = false)
    {
        var label = new Label
        {
            Text = icon,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var border = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            Content = label,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, motherBlue || size >= 80 ? 10 : 4)
        };
        if (motherBlue)
        {
            // Mother ModernActionSheet/Confirm hardcode #2563EB
            border.BackgroundColor = Color.FromArgb("#2563EB");
        }
        else
        {
            border.Use(Border.BackgroundColorProperty, accentKey);
        }

        return border;
    }

    public static Label Title(string text = "")
    {
        var label = new Label
        {
            Text = text,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        label.Use(Label.TextColorProperty, "OwTextStrong");
        return label;
    }

    public static Label Message(string text = "")
    {
        var label = new Label
        {
            Text = text,
            FontSize = 15,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.Use(Label.TextColorProperty, "OwTextMuted");
        return label;
    }
}

/// <summary>Mother MoreOptionsDialog parity: blue "i" circle, 3-column tile grid, Cancel.</summary>
public sealed class OrderPlaceMoreOptionsDialog : ContentView
{
    private readonly Grid _optionsGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly Label _title = MotherDialogVisuals.Title("More Options");
    private readonly SharedButton _cancel = new() { Text = "Cancel", Variant = ButtonVariant.Secondary };
    private TaskCompletionSource<string?>? _tcs;

    public OrderPlaceMoreOptionsDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        _cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);

        var body = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                MotherDialogVisuals.IconCircle("i"),
                _title,
                new ScrollView { MaximumHeightRequest = 460, Content = _optionsGrid },
                _cancel
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(650, 650, body, maxHeight: 760));
    }

    public Task<string?> ShowAsync(ContentPage page, IReadOnlyList<OrderPlaceMoreOption> options)
    {
        _tcs = new TaskCompletionSource<string?>();
        _optionsGrid.Children.Clear();
        _optionsGrid.RowDefinitions.Clear();

        const int columns = 3;
        var rows = (int)Math.Ceiling(options.Count / (double)columns);
        for (var i = 0; i < rows; i++)
        {
            _optionsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var isRestore = option.Text.Contains("RESTORE", StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Text = option.Text,
                BackgroundColor = !option.IsEnabled
                    ? Color.FromArgb("#F3F4F6")
                    : option.IsDestructive
                        ? Color.FromArgb("#DC2626")
                        : isRestore
                            ? Color.FromArgb("#059669")
                            : Color.FromArgb("#F9FAFB"),
                TextColor = !option.IsEnabled
                    ? Color.FromArgb("#9CA3AF")
                    : option.IsDestructive || isRestore
                        ? Colors.White
                        : Color.FromArgb("#1F2937"),
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 12,
                HeightRequest = 70,
                BorderColor = option.IsDestructive
                    ? Color.FromArgb("#B91C1C")
                    : isRestore
                        ? Color.FromArgb("#047857")
                        : Color.FromArgb("#E5E7EB"),
                BorderWidth = 1.5,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                IsEnabled = option.IsEnabled
            };

            if (option.IsEnabled)
            {
                var text = option.Text;
                button.Clicked += (_, _) => _tcs?.TrySetResult(text);
            }

            Grid.SetRow(button, i / columns);
            Grid.SetColumn(button, i % columns);
            _optionsGrid.Children.Add(button);
        }

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }
}

/// <summary>Mother ModernActionSheetDialog parity: blue "i", title, stacked bordered options, Cancel.</summary>
public sealed class OrderPlaceActionSheetDialog : ContentView
{
    private readonly VerticalStackLayout _options = new() { Spacing = 10 };
    private readonly Label _title = MotherDialogVisuals.Title();
    private readonly Button _cancel = new()
    {
        Text = "Cancel",
        Style = null,
        BackgroundColor = Color.FromArgb("#E2E8F0"),
        TextColor = Color.FromArgb("#334155"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 50,
        FontSize = 16
    };
    private TaskCompletionSource<string?>? _tcs;

    public OrderPlaceActionSheetDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);

        var body = new VerticalStackLayout
        {
            Spacing = 20,
            Children =
            {
                MotherDialogVisuals.IconCircle("i", size: 80, motherBlue: true),
                _title,
                new ScrollView { MaximumHeightRequest = 420, Content = _options },
                _cancel
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(450, 450, body));
    }

    public Task<string?> ShowAsync(ContentPage page, string title, IReadOnlyList<string> options, string cancelText = "Cancel")
    {
        _tcs = new TaskCompletionSource<string?>();
        _title.Text = title;
        _cancel.Text = cancelText;
        _options.Children.Clear();

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                continue;
            }

            var text = option;
            var button = new Button
            {
                Text = text,
                Style = null,
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                TextColor = Color.FromArgb("#1E293B"),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 14,
                HeightRequest = 50,
                BorderColor = Color.FromArgb("#D8E1ED"),
                BorderWidth = 1
            };
            button.Clicked += (_, _) => _tcs?.TrySetResult(text);
            _options.Children.Add(button);
        }

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }
}

/// <summary>Mother StyledPromptDialog parity: icon, title, message, entry + virtual keyboard, Cancel/Continue.</summary>
public sealed class OrderPlacePromptDialog : ContentView
{
    private readonly Label _title = MotherDialogVisuals.Title();
    private readonly Label _message = MotherDialogVisuals.Message();
    private readonly Entry _entry = new()
    {
        FontSize = 16,
        BackgroundColor = Colors.Transparent,
        HeightRequest = 42,
        IsReadOnly = true,
        InputTransparent = true
    };
    private readonly Border _entryBorder;
    private readonly SharedButton _cancel = new() { Text = "Cancel", Variant = ButtonVariant.Secondary };
    private readonly SharedButton _accept = new() { Text = "Continue", Variant = ButtonVariant.Primary };
    private readonly Grid _overlayRoot = new();
    private TaskCompletionSource<string?>? _tcs;
    private ContentPage? _page;
    private bool _numericOnly;
    private bool _keyboardOpen;
    private bool _preferNotesKeyboard;

    public OrderPlacePromptDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        ZIndex = 5000;
        _entry.Use(Entry.TextColorProperty, "OwTextPrimary");
        _entry.Use(Entry.PlaceholderColorProperty, "OwTextPlaceholder");

        _entryBorder = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }
        };
        _entryBorder.Use(Border.BackgroundColorProperty, "OwBackground");
        _entryBorder.Use(Border.StrokeProperty, "OwInputBorder");

        var hitOverlay = new BoxView
        {
            Color = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenVirtualKeyboardAsync();
        hitOverlay.GestureRecognizers.Add(tap);
        _entryBorder.GestureRecognizers.Add(tap);
        _entryBorder.Content = new Grid { Children = { _entry, hitOverlay } };

        _cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);
        _accept.Clicked += (_, _) => _tcs?.TrySetResult(_entry.Text ?? string.Empty);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 14
        };
        buttons.Add(_cancel);
        buttons.Add(_accept, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 18,
            Children = { MotherDialogVisuals.IconCircle("?"), _title, _message, _entryBorder, buttons }
        };

        _overlayRoot.Children.Add(MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(460, 460, body)));
        Content = _overlayRoot;
    }

    public Task<string?> ShowAsync(
        ContentPage page,
        string title,
        string message,
        string accept,
        string cancel,
        string placeholder,
        string initialText = "",
        bool numericOnly = false)
    {
        _tcs = new TaskCompletionSource<string?>();
        _page = page;
        _title.Text = title;
        _message.Text = message;
        _message.IsVisible = !string.IsNullOrWhiteSpace(message);
        _accept.Text = accept;
        _cancel.Text = cancel;
        _entry.Placeholder = placeholder;
        _entry.Text = initialText ?? string.Empty;
        _numericOnly = numericOnly;
        _preferNotesKeyboard =
            title.Contains("note", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("note", StringComparison.OrdinalIgnoreCase);
        _entry.Keyboard = numericOnly ? Keyboard.Numeric : Keyboard.Default;
        _keyboardOpen = false;

        return ShowAttachedWithKeyboardAsync(page);
    }

    private async Task<string?> ShowAttachedWithKeyboardAsync(ContentPage page)
    {
        // Presenter adds this dialog to the page synchronously before awaiting completion.
        var presenterTask = OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs!);
        try
        {
            await Task.Delay(80);
            await OpenVirtualKeyboardAsync();
        }
        catch
        {
            // User can still tap the field.
        }

        return await presenterTask;
    }

    private async Task OpenVirtualKeyboardAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(_title.Text, string.IsNullOrWhiteSpace(_accept.Text) ? "DONE" : _accept.Text);
            var descriptor = $"{_title.Text} {_message.Text} {_entry.Placeholder}".ToLowerInvariant();
            if (_numericOnly && descriptor.Contains("phone"))
            {
                keyboard.SetTextMode(VirtualKeyboardTextMode.Phone);
                keyboard.SetMaximumLength(16);
            }
            else if (_numericOnly)
            {
                var isPin = descriptor.Contains("pin");
                var isPositiveNumber = descriptor.Contains("points") || descriptor.Contains("table") || descriptor.Contains("merge");
                keyboard.SetNumericMode(
                    isPin ? VirtualKeyboardNumericMode.LongDigits : VirtualKeyboardNumericMode.WholeNumber,
                    minimum: isPositiveNumber ? 1m : 0m,
                    maximum: isPin ? null : 999999999m);
                keyboard.SetMaximumLength(isPin ? 4 : 9);
                keyboard.SetRequired(true);
            }
            else if (_preferNotesKeyboard)
            {
                keyboard.SetTextMode(VirtualKeyboardTextMode.Notes);
            }

            keyboard.SetInitialText(_entry.Text ?? string.Empty);
            keyboard.SetPlaceholder(_entry.Placeholder);
            // Attach onto this dialog overlay so Mother/Client page chrome cannot cover it.
            var result = await keyboard.ShowOverAsync(_overlayRoot, _page);
            if (result is not null)
            {
                _entry.Text = result;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }
}

/// <summary>Mother ModernConfirmDialog parity: blue "!", title, message, Keep Open / Discard|Confirm.</summary>
public sealed class OrderPlaceConfirmDialog : ContentView
{
    private readonly Label _title = MotherDialogVisuals.Title();
    private readonly Label _message = MotherDialogVisuals.Message();
    private readonly Button _no = new()
    {
        Text = "No",
        Style = null,
        BackgroundColor = Color.FromArgb("#E2E8F0"),
        TextColor = Color.FromArgb("#334155"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 50,
        FontSize = 16
    };
    private readonly Button _yes = new()
    {
        Text = "Yes",
        Style = null,
        BackgroundColor = Color.FromArgb("#2563EB"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 50,
        FontSize = 16
    };
    private TaskCompletionSource<bool>? _tcs;

    public OrderPlaceConfirmDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _no.Clicked += (_, _) => _tcs?.TrySetResult(false);
        _yes.Clicked += (_, _) => _tcs?.TrySetResult(true);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 15
        };
        buttons.Add(_no);
        buttons.Add(_yes, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 20,
            Children = { MotherDialogVisuals.IconCircle("!", size: 80, motherBlue: true), _title, _message, buttons }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(450, 450, body));
    }

    public Task<bool> ShowAsync(ContentPage page, string title, string message, string accept = "Yes", string cancel = "No")
    {
        _tcs = new TaskCompletionSource<bool>();
        _title.Text = title;
        _message.Text = message;
        _yes.Text = accept;
        _no.Text = cancel;

        // Same destructive rules as Mother ModernConfirmDialog.SetConfirm
        // (Discard Unsent Items stays primary blue; Void Order - Danger is red).
        var loweredTitle = title.ToLowerInvariant();
        var loweredYes = accept.ToLowerInvariant();
        var destructive =
            loweredTitle.Contains("danger") ||
            loweredTitle.Contains("delete") ||
            loweredTitle.Contains("void") ||
            loweredTitle.Contains("cancel") ||
            loweredYes.Contains("delete") ||
            loweredYes.Contains("void") ||
            loweredYes.Contains("cancel");
        _yes.BackgroundColor = destructive ? Color.FromArgb("#DC2626") : Color.FromArgb("#2563EB");
        _yes.TextColor = Colors.White;

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }
}

/// <summary>Mother DiscountDialog parity: Fixed/Percent toggle, amount, reason chips, Remove/Apply.</summary>
public sealed class OrderPlaceDiscountDialog : ContentView
{
    private readonly Label _subtotalLabel = new() { FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#333333") };
    private readonly SharedButton _fixedButton = new() { Text = "Fixed (£)" };
    private readonly SharedButton _percentButton = new() { Text = "Percent (%)" };
    private readonly Entry _amountEntry = new()
    {
        Placeholder = "0",
        Keyboard = Keyboard.Numeric,
        FontSize = 18,
        HeightRequest = 44,
        BackgroundColor = Colors.Transparent,
        IsReadOnly = true,
        InputTransparent = true
    };
    private readonly Label _symbolLabel = new() { Text = "£", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4A90D9"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    private readonly FlexLayout _reasonLayout = new()
    {
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
        JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
    };
    private readonly Border _customReasonBorder;
    private readonly Entry _customReasonEntry = new()
    {
        Placeholder = "Enter reason...",
        FontSize = 14,
        HeightRequest = 40,
        BackgroundColor = Colors.Transparent,
        IsReadOnly = true,
        InputTransparent = true
    };
    private readonly Label _discountAmountLabel = new() { Text = "£0.00", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#CC8800") };
    private readonly Label _errorLabel = new() { FontSize = 13, TextColor = Color.FromArgb("#DC2626"), IsVisible = false, HorizontalOptions = LayoutOptions.Center };

    private static readonly string[] ReasonOptions = { "Staff", "Family", "Good Will", "Voucher", "Custom" };
    private readonly Dictionary<string, Button> _reasonButtons = new(StringComparer.OrdinalIgnoreCase);
    private Button? _selectedReasonButton;
    private string? _selectedReason;
    private bool _isFixed = true;
    private decimal _subtotal;
    private decimal _amountValue;
    private ContentPage? _page;
    private bool _keyboardOpen;
    private readonly Grid _overlayRoot = new();

    private TaskCompletionSource<OrderPlaceDiscountResult?>? _tcs;

    public OrderPlaceDiscountDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        ZIndex = 5000;

        _fixedButton.Clicked += (_, _) =>
        {
            SetFixed(true);
            _ = OpenAmountKeyboardAsync();
        };
        _percentButton.Clicked += (_, _) =>
        {
            SetFixed(false);
            _ = OpenAmountKeyboardAsync();
        };
        _amountEntry.TextChanged += (_, e) =>
        {
            _amountValue = decimal.TryParse(e.NewTextValue, out var value) ? value : 0m;
            UpdateDiscountPreview();
        };

        foreach (var reason in ReasonOptions)
        {
            var button = new Button
            {
                Text = reason,
                BackgroundColor = Color.FromArgb("#F0F0F0"),
                TextColor = Color.FromArgb("#555555"),
                FontSize = 13,
                HeightRequest = 38,
                WidthRequest = 100,
                CornerRadius = 6,
                Margin = new Thickness(0, 0, 8, 8)
            };
            var captured = reason;
            button.Clicked += (_, _) => SelectReason(captured, button);
            _reasonButtons[reason] = button;
            _reasonLayout.Children.Add(button);
        }

        _customReasonBorder = new Border
        {
            IsVisible = false,
            Padding = new Thickness(10, 0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = _customReasonEntry
        };
        _customReasonBorder.Use(Border.BackgroundColorProperty, "OwSurface");
        _customReasonBorder.Use(Border.StrokeProperty, "OwBorderStrong");
        var customTap = new TapGestureRecognizer();
        customTap.Tapped += async (_, _) => await OpenFieldKeyboardAsync(
            "Custom reason",
            _customReasonEntry.Text ?? string.Empty,
            numericOnly: false,
            text => _customReasonEntry.Text = text);
        _customReasonBorder.GestureRecognizers.Add(customTap);

        var subtotalFrame = new Border
        {
            Padding = 12,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Subtotal:", FontSize = 16, TextColor = Color.FromArgb("#666666"), VerticalOptions = LayoutOptions.Center },
                    _subtotalLabel
                }
            }
        };
        subtotalFrame.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        subtotalFrame.Use(Border.StrokeProperty, "OwBorder");

        var typeGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        typeGrid.Add(_fixedButton);
        typeGrid.Add(_percentButton, 1);

        var amountBorder = new Border { StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 } };
        amountBorder.Use(Border.BackgroundColorProperty, "OwSurface");
        amountBorder.Use(Border.StrokeProperty, "OwBorderStrong");
        var amountHit = new BoxView
        {
            Color = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var amountTap = new TapGestureRecognizer();
        amountTap.Tapped += async (_, _) => await OpenAmountKeyboardAsync();
        amountHit.GestureRecognizers.Add(amountTap);
        amountBorder.GestureRecognizers.Add(amountTap);
        amountBorder.Content = new Grid { Children = { _amountEntry, amountHit } };
        var symbolBorder = new Border { WidthRequest = 50, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = _symbolLabel };
        symbolBorder.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        symbolBorder.Use(Border.StrokeProperty, "OwBorderStrong");
        var amountGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(50) }, ColumnSpacing = 10 };
        amountGrid.Add(amountBorder);
        amountGrid.Add(symbolBorder, 1);

        var resultFrame = new Border
        {
            Padding = 12,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new Label { Text = "Discount Amount:", FontSize = 15, TextColor = Color.FromArgb("#996600"), VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        resultFrame.Use(Border.BackgroundColorProperty, "OwWarningSoft");
        resultFrame.Use(Border.StrokeProperty, "OwWarningBorder");
        ((Grid)resultFrame.Content).Add(_discountAmountLabel, 1);

        var scrollBody = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                subtotalFrame,
                new Label { Text = "Type", FontSize = 14, TextColor = Color.FromArgb("#666666"), FontAttributes = FontAttributes.Bold },
                typeGrid,
                new Label { Text = "Amount", FontSize = 14, TextColor = Color.FromArgb("#666666"), FontAttributes = FontAttributes.Bold },
                amountGrid,
                new Label { Text = "Reason", FontSize = 14, TextColor = Color.FromArgb("#666666"), FontAttributes = FontAttributes.Bold },
                _reasonLayout,
                _customReasonBorder,
                resultFrame,
                _errorLabel
            }
        };

        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary, FontSize = 13 };
        cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);
        var remove = new SharedButton { Text = "Remove", Variant = ButtonVariant.Danger, FontSize = 13 };
        remove.Clicked += (_, _) => _tcs?.TrySetResult(new OrderPlaceDiscountResult(false, true, false, 0m, null));
        var apply = new SharedButton { Text = "Apply", Variant = ButtonVariant.Success, FontSize = 14 };
        apply.Clicked += (_, _) => TryApply();

        var actionsGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
        actionsGrid.Add(cancel);
        actionsGrid.Add(remove, 1);
        actionsGrid.Add(apply, 2);

        var header = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                MotherDialogVisuals.IconCircle("%", size: 58, fontSize: 24),
                MotherDialogVisuals.Title("Apply Discount"),
                MotherDialogVisuals.Message("Choose discount type and amount for this order.")
            }
        };

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, RowSpacing = 12 };
        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = scrollBody }, 0, 1);
        root.Add(actionsGrid, 0, 2);

        _overlayRoot.Children.Add(MotherDialogVisuals.OverlayGrid(new Border
        {
            Padding = 20,
            MaximumWidthRequest = 540,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = root,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        }.Also(b =>
        {
            b.Use(Border.BackgroundColorProperty, "OwSurface");
            b.Use(Border.StrokeProperty, "OwBorder");
        })));
        Content = _overlayRoot;
    }

    public Task<OrderPlaceDiscountResult?> ShowAsync(ContentPage page, decimal subtotal)
    {
        _tcs = new TaskCompletionSource<OrderPlaceDiscountResult?>();
        _page = page;
        _subtotal = subtotal;
        _subtotalLabel.Text = $"£{subtotal:F2}";
        _amountEntry.Text = string.Empty;
        _amountValue = 0m;
        _customReasonEntry.Text = string.Empty;
        _customReasonBorder.IsVisible = false;
        _errorLabel.IsVisible = false;
        _selectedReason = null;
        _selectedReasonButton = null;
        _keyboardOpen = false;
        foreach (var button in _reasonButtons.Values)
        {
            button.BackgroundColor = Color.FromArgb("#F0F0F0");
            button.TextColor = Color.FromArgb("#555555");
        }

        SetFixed(true);
        return ShowAttachedWithAmountKeyboardAsync(page);
    }

    private async Task<OrderPlaceDiscountResult?> ShowAttachedWithAmountKeyboardAsync(ContentPage page)
    {
        var presenterTask = OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs!);
        try
        {
            await Task.Delay(80);
            await OpenAmountKeyboardAsync();
        }
        catch
        {
            // User can still tap Amount.
        }

        return await presenterTask;
    }

    private Task OpenAmountKeyboardAsync() =>
        OpenFieldKeyboardAsync(
            _isFixed ? "Discount amount (£)" : "Discount percent (%)",
            _amountEntry.Text ?? string.Empty,
            numericOnly: true,
            text =>
            {
                _amountEntry.Text = text;
                _amountValue = decimal.TryParse(text, out var value) ? value : 0m;
                UpdateDiscountPreview();
            });

    private async Task OpenFieldKeyboardAsync(string title, string initial, bool numericOnly, Action<string> apply)
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(title, "DONE");
            if (numericOnly)
            {
                keyboard.SetNumericMode(
                    _isFixed ? VirtualKeyboardNumericMode.Currency : VirtualKeyboardNumericMode.Decimal,
                    minimum: 0m,
                    maximum: _isFixed ? _subtotal : 100m);
            }
            else
            {
                keyboard.SetNumericOnly(false);
                keyboard.SetTextMode(VirtualKeyboardTextMode.Notes);
            }

            keyboard.SetInitialText(initial);
            var result = await keyboard.ShowOverAsync(_overlayRoot, _page);
            if (result is not null)
            {
                apply(result.Trim());
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private void SetFixed(bool isFixed)
    {
        _isFixed = isFixed;
        _fixedButton.Variant = isFixed ? ButtonVariant.Primary : ButtonVariant.Secondary;
        _percentButton.Variant = isFixed ? ButtonVariant.Secondary : ButtonVariant.Primary;
        _symbolLabel.Text = isFixed ? "£" : "%";
        UpdateDiscountPreview();
    }

    private void SelectReason(string reason, Button button)
    {
        if (_selectedReasonButton is not null)
        {
            _selectedReasonButton.BackgroundColor = Color.FromArgb("#F0F0F0");
            _selectedReasonButton.TextColor = Color.FromArgb("#555555");
        }

        _selectedReasonButton = button;
        button.BackgroundColor = Color.FromArgb("#4A90D9");
        button.TextColor = Colors.White;
        _selectedReason = reason;
        _customReasonBorder.IsVisible = string.Equals(reason, "Custom", StringComparison.OrdinalIgnoreCase);
        if (_customReasonBorder.IsVisible)
        {
            _ = OpenFieldKeyboardAsync(
                "Custom reason",
                _customReasonEntry.Text ?? string.Empty,
                numericOnly: false,
                text => _customReasonEntry.Text = text);
        }
    }

    private void UpdateDiscountPreview()
    {
        var actual = _isFixed ? _amountValue : Math.Round(_subtotal * (_amountValue / 100m), 2, MidpointRounding.AwayFromZero);
        if (actual > _subtotal)
        {
            actual = _subtotal;
        }

        _discountAmountLabel.Text = $"£{actual:F2}";
    }

    private void TryApply()
    {
        _errorLabel.IsVisible = false;
        if (_amountValue <= 0)
        {
            ShowError("Please enter a discount amount.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedReason))
        {
            ShowError("Please select a discount reason.");
            return;
        }

        var reason = _selectedReason;
        if (string.Equals(_selectedReason, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = _customReasonEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(custom))
            {
                ShowError("Please enter a custom reason.");
                return;
            }

            reason = custom;
        }

        var amount = _isFixed ? _amountValue : 0m;
        var percent = _isFixed ? 0m : _amountValue;
        _tcs?.TrySetResult(new OrderPlaceDiscountResult(true, false, !_isFixed, _isFixed ? amount : percent, reason));
    }

    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.IsVisible = true;
    }
}

/// <summary>Mother TableTransferDialog parity: current table + grid of available empty tables.</summary>
public sealed class OrderPlaceTableTransferDialog : ContentView
{
    private readonly Label _currentTableLabel = new() { FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#CC8800") };
    private readonly FlexLayout _tablesContainer = new()
    {
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
        JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
    };
    private readonly Label _noTablesLabel = new() { Text = "No empty tables available", FontSize = 14, TextColor = Color.FromArgb("#CC4444"), HorizontalOptions = LayoutOptions.Center, IsVisible = false };
    private readonly Border _selectedFrame;
    private readonly Label _selectedLabel = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1B5E20") };
    private readonly SharedButton _transfer = new() { Text = "Transfer", Variant = ButtonVariant.Success, IsEnabled = false };

    private readonly Dictionary<string, Button> _tableButtons = new(StringComparer.OrdinalIgnoreCase);
    private OrderPlaceTableOption? _selected;
    private TaskCompletionSource<OrderPlaceTableOption?>? _tcs;

    public OrderPlaceTableTransferDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");

        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);
        _transfer.Clicked += (_, _) => _tcs?.TrySetResult(_selected);

        var currentFrame = new Border
        {
            Padding = 12,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Transfer from:", FontSize = 15, TextColor = Color.FromArgb("#996600"), VerticalOptions = LayoutOptions.Center },
                    _currentTableLabel
                }
            }
        };
        currentFrame.Use(Border.BackgroundColorProperty, "OwWarningSoft");
        currentFrame.Use(Border.StrokeProperty, "OwWarningBorder");

        var tablesFrame = new Border
        {
            Padding = 10,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new ScrollView { HeightRequest = 180, Content = _tablesContainer }
        };
        tablesFrame.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        tablesFrame.Use(Border.StrokeProperty, "OwBorder");

        _selectedFrame = new Border
        {
            IsVisible = false,
            Padding = 10,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Transfer to:", FontSize = 14, TextColor = Color.FromArgb("#2E7D32"), VerticalOptions = LayoutOptions.Center },
                    _selectedLabel
                }
            }
        };
        _selectedFrame.Use(Border.BackgroundColorProperty, "OwSuccessSoft");
        _selectedFrame.Use(Border.StrokeProperty, "OwSuccessSoftBorder");

        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        buttons.Add(cancel);
        buttons.Add(_transfer, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                MotherDialogVisuals.IconCircle("T"),
                MotherDialogVisuals.Title("Transfer Table"),
                MotherDialogVisuals.Message("Move the current order to another table."),
                currentFrame,
                new Label { Text = "Select an empty table:", FontSize = 13, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                tablesFrame,
                _noTablesLabel,
                _selectedFrame,
                buttons
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(500, 500, body));
    }

    public Task<OrderPlaceTableOption?> ShowAsync(ContentPage page, string currentTableLabel, IReadOnlyList<OrderPlaceTableOption> availableTables)
    {
        _tcs = new TaskCompletionSource<OrderPlaceTableOption?>();
        _currentTableLabel.Text = currentTableLabel;
        _selected = null;
        _selectedFrame.IsVisible = false;
        _transfer.IsEnabled = false;
        _transfer.Variant = ButtonVariant.Utility;
        _tableButtons.Clear();
        _tablesContainer.Children.Clear();

        _noTablesLabel.IsVisible = availableTables.Count == 0;

        foreach (var table in availableTables)
        {
            var button = new Button
            {
                Text = table.Label,
                BackgroundColor = Color.FromArgb("#E8F5E8"),
                TextColor = Color.FromArgb("#2E7D32"),
                FontSize = 14,
                HeightRequest = 50,
                WidthRequest = 120,
                CornerRadius = 8,
                Margin = new Thickness(5)
            };
            button.Clicked += (_, _) => SelectTable(table, button);
            _tableButtons[table.Id] = button;
            _tablesContainer.Children.Add(button);
        }

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private void SelectTable(OrderPlaceTableOption table, Button button)
    {
        foreach (var other in _tableButtons.Values)
        {
            other.BackgroundColor = Color.FromArgb("#E8F5E8");
            other.TextColor = Color.FromArgb("#2E7D32");
        }

        button.BackgroundColor = Color.FromArgb("#4CAF50");
        button.TextColor = Colors.White;

        _selected = table;
        _selectedLabel.Text = table.Label;
        _selectedFrame.IsVisible = true;
        _transfer.IsEnabled = true;
        _transfer.Variant = ButtonVariant.Success;
    }
}

/// <summary>Mother FireCourseDialog parity: Starters / Mains / Desserts / Drinks + Fire All.</summary>
public sealed class OrderPlaceFireCourseDialog : ContentView
{
    private readonly Grid _coursesGrid = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private TaskCompletionSource<string?>? _tcs;

    public OrderPlaceFireCourseDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);

        var body = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                MotherDialogVisuals.IconCircle("F", fontSize: 28),
                MotherDialogVisuals.Title("Fire Course"),
                MotherDialogVisuals.Message("Select which course to send to kitchen"),
                _coursesGrid,
                cancel
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(450, 450, body));
    }

    /// <param name="includeDrinks">Kept for callers; Drinks always shown (Mother parity).</param>
    public Task<string?> ShowAsync(ContentPage page, bool includeDrinks = true)
    {
        _ = includeDrinks;
        _tcs = new TaskCompletionSource<string?>();
        BuildTiles();
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private void BuildTiles()
    {
        _coursesGrid.Children.Clear();
        _coursesGrid.RowDefinitions.Clear();
        _coursesGrid.ColumnDefinitions.Clear();
        _coursesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _coursesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _coursesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _coursesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _coursesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddTile("Starters", "#E0F2FE", "#0EA5E9", "#0369A1", 0, 0);
        AddTile("Mains", "#DBEAFE", "#3B82F6", "#1E40AF", 0, 1);
        AddTile("Desserts", "#E0E7FF", "#6366F1", "#4338CA", 1, 0);
        AddTile("Drinks", "#CFFAFE", "#06B6D4", "#0E7490", 1, 1);
        AddFireAllTile(2);
    }

    private void AddTile(string text, string bg, string stroke, string textColor, int row, int col)
    {
        var label = new Label { Text = text, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(textColor), HorizontalOptions = LayoutOptions.Center };
        var border = new Border
        {
            BackgroundColor = Color.FromArgb(bg),
            Stroke = Color.FromArgb(stroke),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HeightRequest = 90,
            Content = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { label } }
        };
        var tap = new TapGestureRecognizer();
        var value = text;
        tap.Tapped += (_, _) => _tcs?.TrySetResult(value);
        border.GestureRecognizers.Add(tap);
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        _coursesGrid.Children.Add(border);
    }

    private void AddFireAllTile(int row)
    {
        var label = new Label { Text = "Fire All Courses", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#3B82F6"),
            Stroke = Color.FromArgb("#2563EB"),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HeightRequest = 70,
            Content = new HorizontalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, Children = { label } }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _tcs?.TrySetResult("All");
        border.GestureRecognizers.Add(tap);
        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, 2);
        _coursesGrid.Children.Add(border);
    }
}

internal static class FluentExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
