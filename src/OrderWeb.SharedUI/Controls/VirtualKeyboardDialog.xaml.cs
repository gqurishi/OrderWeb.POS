using Microsoft.Maui.Devices;
using System.Globalization;

namespace OrderWeb.SharedUI.Controls;

public enum VirtualKeyboardTextMode
{
    Text,
    Name,
    Address,
    Notes,
    Email,
    Phone
}

public enum VirtualKeyboardNumericMode
{
    WholeNumber,
    Decimal,
    Currency,
    Quantity,
    LongDigits,
    /// <summary>Digits and multiple dots (Mother IP / host address).</summary>
    IpAddress
}

/// <summary>
/// Mother/Client-parity on-screen keyboard overlay for Collection/Delivery (and other) customer fields.
/// Host-neutral: does not reference Mother or Client services. Callers pass the hosting <see cref="Page"/>
/// to <see cref="ShowAsync"/>.
/// </summary>
public partial class VirtualKeyboardDialog : ContentView
{
    private static VirtualKeyboardDialog? _activeDialog;
    private TaskCompletionSource<string?>? _tcs;
    private string _searchText = string.Empty;
    private ContentPage? _hostPage;
    private Grid? _hostGrid;
    private Grid? _overlayHost;
    private bool _isClosed;
    private bool _isCompleting;
    private bool _wrappedHostContent;
    private bool _isNumericOnly;
    private VirtualKeyboardNumericMode _numericMode = VirtualKeyboardNumericMode.WholeNumber;
    private bool _isPhoneMode;
    private bool _isRequired;
    private decimal? _minimum;
    private decimal? _maximum;
    private int _maximumLength;

    public VirtualKeyboardDialog()
    {
        InitializeComponent();
        SemanticProperties.SetDescription(KeyboardInputEntry, "Keyboard input");
        // Never open another SharedUI keyboard from the in-dialog display field.
        SharedTouchKeyboard.SetEnabled(KeyboardInputEntry, false);
        KeyboardInputEntry.HandlerChanged += (_, _) => SharedTouchKeyboard.SetEnabled(KeyboardInputEntry, false);

        HeaderCancelButton.Clicked += OnCancelClicked;
        NumericCancelButton.Clicked += OnCancelClicked;
        var headerCloseTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        headerCloseTap.Tapped += OnCancelClicked;
        HeaderCloseHit.GestureRecognizers.Add(headerCloseTap);

        foreach (var button in GetButtons(KeyboardRowsContainer))
        {
            SemanticProperties.SetDescription(button, button.Text switch
            {
                "DEL" or "⌫" => "Delete previous character",
                "CLEAR" => "Clear input",
                "CANCEL" or "Cancel" or "Close" or "✕" => "Cancel input",
                "DONE" or "Done" or "ENTER" => "Confirm input",
                _ => $"Key {button.Text}"
            });
        }
    }

    public void Cancel() => CompleteDialog(null);

    public void SetInitialText(string text)
    {
        _searchText = ApplyInputRules(text ?? string.Empty);
        UpdateDisplay();
    }

    public void SetPlaceholder(string? placeholder) =>
        KeyboardInputEntry.Placeholder = placeholder ?? string.Empty;

    public void SetMaximumLength(int maximumLength)
    {
        _maximumLength = Math.Max(0, maximumLength);
        KeyboardInputEntry.MaxLength = _maximumLength == 0 ? int.MaxValue : _maximumLength;
        _searchText = ApplyInputRules(_searchText);
        UpdateDisplay();
    }

    public void SetRequired(bool required) => _isRequired = required;

    public void SetTextMode(VirtualKeyboardTextMode mode)
    {
        _isPhoneMode = mode == VirtualKeyboardTextMode.Phone;
        SetNumericOnly(mode == VirtualKeyboardTextMode.Phone);
        CurrencyPrefixLabel.IsVisible = false;
        DecimalKeyButton.IsVisible = _isPhoneMode;
        DecimalKeyButton.Text = _isPhoneMode ? "+" : ".";
        KeyboardInputEntry.Keyboard = mode switch
        {
            VirtualKeyboardTextMode.Email => Keyboard.Email,
            VirtualKeyboardTextMode.Phone => Keyboard.Telephone,
            _ => Keyboard.Default
        };
    }

    public void SetNumericMode(
        VirtualKeyboardNumericMode mode,
        decimal? minimum = null,
        decimal? maximum = null)
    {
        _isPhoneMode = false;
        _numericMode = mode;
        _minimum = minimum;
        _maximum = maximum;
        SetNumericOnly(true);
        DecimalKeyButton.IsVisible = mode is VirtualKeyboardNumericMode.Decimal
            or VirtualKeyboardNumericMode.Currency
            or VirtualKeyboardNumericMode.IpAddress;
        DecimalKeyButton.Text = ".";
        CurrencyPrefixLabel.IsVisible = mode == VirtualKeyboardNumericMode.Currency;
        KeyboardInputEntry.Keyboard = mode is VirtualKeyboardNumericMode.Decimal
            or VirtualKeyboardNumericMode.Currency
            or VirtualKeyboardNumericMode.IpAddress
            ? Keyboard.Numeric
            : Keyboard.Telephone;
        _searchText = ApplyInputRules(_searchText);
        UpdateDisplay();
    }

    public void SetPrompt(string title, string actionText = "ENTER")
    {
        KeyboardTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Keyboard" : title.Trim();
        var normalizedAction = string.IsNullOrWhiteSpace(actionText) ? "ENTER" : actionText.Trim().ToUpperInvariant();
        EnterButton.Text = normalizedAction;
        NumericEnterButton.Text = _isNumericOnly ? ToTitleCaseAction(normalizedAction) : normalizedAction;
    }

    /// <summary>
    /// When true, shows the centered amount keypad (pill digits + green Done).
    /// When false, shows the full QWERTY keyboard (default).
    /// </summary>
    public void SetNumericOnly(bool numericOnly)
    {
        _isNumericOnly = numericOnly;
        NumberRow.IsVisible = !numericOnly;
        NumericPadContainer.IsVisible = numericOnly;
        AlphaRowsContainer.IsVisible = !numericOnly;
        FullActionsRow.IsVisible = !numericOnly;
        NumericActionsRow.IsVisible = numericOnly;
        NumericEnterButton.IsVisible = numericOnly;
        NumericCancelButton.IsVisible = numericOnly;

        HeaderCancelButton.IsVisible = !numericOnly;
        HeaderCancelButton.IsEnabled = !numericOnly;
        HeaderCancelButton.InputTransparent = numericOnly;

        HeaderCloseHit.IsVisible = numericOnly;
        HeaderCloseHit.InputTransparent = !numericOnly;

        KeyboardInputEntry.Keyboard = numericOnly ? Keyboard.Numeric : Keyboard.Default;
        ApplyChromeForMode();
    }

    private void ApplyChromeForMode()
    {
        if (_isNumericOnly)
        {
            // One full-screen row: scrim behind, centered card on top (ZIndex).
            OverlayGrid.RowDefinitions.Clear();
            OverlayGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(DismissScrim, 0);
            Grid.SetRowSpan(DismissScrim, 1);
            Grid.SetRow(KeyboardCard, 0);
            Grid.SetRowSpan(KeyboardCard, 1);
            DismissScrim.ZIndex = 0;
            KeyboardCard.ZIndex = 10;
            KeyboardCard.VerticalOptions = LayoutOptions.Center;
            KeyboardCard.HorizontalOptions = LayoutOptions.Center;
            KeyboardCard.WidthRequest = 360;
            KeyboardCard.MaximumWidthRequest = 380;
            KeyboardCard.Margin = new Thickness(16);
            KeyboardHeader.Padding = new Thickness(18, 14);
            KeyboardTitleLabel.FontSize = 18;
            KeyboardInputBorder.Margin = new Thickness(18, 16, 18, 10);
            KeyboardInputBorder.HeightRequest = 56;
            KeyboardInputBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 };
            KeyboardInputEntry.FontSize = 28;
            KeyboardInputEntry.FontAttributes = FontAttributes.Bold;
            KeyboardInputEntry.HeightRequest = 54;
            CurrencyPrefixLabel.FontSize = 28;
            KeyboardRowsContainer.Padding = new Thickness(14, 4, 14, 16);
            KeyboardRowsContainer.Spacing = 8;
            NumericEnterButton.Text = string.IsNullOrWhiteSpace(NumericEnterButton.Text)
                ? "Done"
                : ToTitleCaseAction(NumericEnterButton.Text);
            ApplyNumericPadButtonSizes();
        }
        else
        {
            OverlayGrid.RowDefinitions.Clear();
            OverlayGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            OverlayGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(DismissScrim, 0);
            Grid.SetRowSpan(DismissScrim, 1);
            Grid.SetRow(KeyboardCard, 1);
            Grid.SetRowSpan(KeyboardCard, 1);
            DismissScrim.ZIndex = 0;
            KeyboardCard.ZIndex = 10;
            KeyboardCard.VerticalOptions = LayoutOptions.End;
            KeyboardCard.HorizontalOptions = LayoutOptions.Fill;
            KeyboardCard.WidthRequest = -1;
            KeyboardCard.MaximumWidthRequest = double.PositiveInfinity;
            KeyboardCard.Margin = new Thickness(12, 0, 12, 10);
            KeyboardInputEntry.FontAttributes = FontAttributes.None;
            KeyboardInputEntry.FontSize = 18;
            CurrencyPrefixLabel.FontSize = 26;
            if (_hostPage is not null)
            {
                ApplyResponsiveLayout(_hostPage.Width, _hostPage.Height);
            }
        }
    }

    private void ApplyNumericPadButtonSizes()
    {
        foreach (var button in GetButtons(NumericPadContainer))
        {
            if (ReferenceEquals(button, NumericBackspaceButton))
            {
                button.BackgroundColor = Color.FromArgb("#FEE2E2");
                button.TextColor = Color.FromArgb("#EF4444");
                button.Text = "⌫";
            }
            else
            {
                button.BackgroundColor = Color.FromArgb("#F1F5F9");
                button.TextColor = Color.FromArgb("#0F172A");
            }

            button.HeightRequest = 58;
            button.CornerRadius = 29;
            button.BorderWidth = 0;
            button.FontSize = string.Equals(button.Text, ".", StringComparison.Ordinal) ? 28 : 26;
            button.FontAttributes = FontAttributes.Bold;
        }

        NumericEnterButton.BackgroundColor = Color.FromArgb("#22C55E");
        NumericEnterButton.TextColor = Colors.White;
        NumericEnterButton.HeightRequest = 54;
        NumericEnterButton.CornerRadius = 16;
        NumericEnterButton.FontSize = 18;
        NumericEnterButton.FontAttributes = FontAttributes.Bold;

        NumericCancelButton.BackgroundColor = Color.FromArgb("#E2E8F0");
        NumericCancelButton.TextColor = Color.FromArgb("#334155");
        NumericCancelButton.HeightRequest = 54;
        NumericCancelButton.CornerRadius = 16;
        NumericCancelButton.FontSize = 18;
        NumericCancelButton.FontAttributes = FontAttributes.Bold;
    }

    private static string ToTitleCaseAction(string action)
    {
        var trimmed = action.Trim();
        if (trimmed.Equals("DONE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ENTER", StringComparison.OrdinalIgnoreCase))
        {
            return "Done";
        }

        return trimmed.Length <= 1
            ? trimmed.ToUpperInvariant()
            : char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }

    public async Task<string?> ShowAsync(Page? hostPage = null)
    {
        if (_activeDialog is { _isClosed: false })
        {
            return null;
        }

        _activeDialog = this;
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _isClosed = false;
        _isCompleting = false;

        var page = hostPage as ContentPage
            ?? Shell.Current?.CurrentPage as ContentPage
            ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;

        if (page is null)
        {
            _activeDialog = null;
            _tcs.TrySetResult(null);
            return null;
        }

        AddToPage(page);
        return await _tcs.Task;
    }

    /// <summary>
    /// Show the keyboard on top of an existing dialog overlay grid (Order Notes / Discount / Merge).
    /// Prefer this when a SharedUI modal is already covering the page — attaching to the page root can sit behind Mother/Client overlays.
    /// </summary>
    public async Task<string?> ShowOverAsync(Grid overlayHost, ContentPage? sizePage = null)
    {
        ArgumentNullException.ThrowIfNull(overlayHost);

        if (_activeDialog is { _isClosed: false } active && !ReferenceEquals(active, this))
        {
            active.Cancel();
        }

        _activeDialog = this;

        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _isClosed = false;
        _isCompleting = false;
        _wrappedHostContent = false;
        _hostPage = sizePage
            ?? overlayHost.Window?.Page as ContentPage
            ?? Shell.Current?.CurrentPage as ContentPage
            ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;
        _hostGrid = overlayHost;
        _overlayHost = overlayHost;
        _overlayHost.ParentChanged += OnOverlayHostParentChanged;

        if (overlayHost.RowDefinitions.Count > 0)
        {
            Grid.SetRowSpan(this, Math.Max(1, overlayHost.RowDefinitions.Count));
        }

        if (overlayHost.ColumnDefinitions.Count > 0)
        {
            Grid.SetColumnSpan(this, Math.Max(1, overlayHost.ColumnDefinitions.Count));
        }

        Grid.SetRow(this, 0);
        Grid.SetColumn(this, 0);
        overlayHost.Children.Add(this);

        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        // Above cash-drawer and other SharedUI overlays (those use 20000).
        ZIndex = 30000;
        InputTransparent = false;
        Opacity = 1;
        IsVisible = true;

        if (_hostPage is not null)
        {
            _hostPage.SizeChanged += OnHostPageSizeChanged;
            _hostPage.Disappearing += OnHostPageDisappearing;
            ApplyResponsiveLayout(_hostPage.Width, _hostPage.Height);
        }
        else
        {
            ApplyResponsiveLayout(overlayHost.Width, overlayHost.Height);
        }

        FocusInput(moveCursorToEnd: true);
        return await _tcs.Task;
    }

    private void AddToPage(ContentPage page)
    {
        _hostPage = page;
        _wrappedHostContent = false;

        if (page.Content is Grid existingGrid)
        {
            Grid.SetRowSpan(this, Math.Max(1, existingGrid.RowDefinitions.Count));
            Grid.SetColumnSpan(this, Math.Max(1, existingGrid.ColumnDefinitions.Count));
            Grid.SetRow(this, 0);
            Grid.SetColumn(this, 0);
            existingGrid.Children.Add(this);
            _hostGrid = existingGrid;
        }
        else
        {
            var newGrid = new Grid();
            var existingContent = page.Content;
            page.Content = null;
            if (existingContent is not null)
            {
                newGrid.Children.Add(existingContent);
            }

            newGrid.Children.Add(this);
            page.Content = newGrid;
            _hostGrid = newGrid;
            _wrappedHostContent = true;
        }

        ZIndex = 30000;
        InputTransparent = false;
        Opacity = 1;
        IsVisible = true;
        page.SizeChanged += OnHostPageSizeChanged;
        page.Disappearing += OnHostPageDisappearing;
        ApplyResponsiveLayout(page.Width, page.Height);
        FocusInput(moveCursorToEnd: true);
    }

    private void CloseDialog()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        InputTransparent = true;
        IsVisible = false;
        if (_hostPage is not null)
        {
            _hostPage.SizeChanged -= OnHostPageSizeChanged;
            _hostPage.Disappearing -= OnHostPageDisappearing;
        }

        if (_hostGrid is not null && _hostGrid.Children.Contains(this))
        {
            _hostGrid.Children.Remove(this);
        }
        else if (Parent is Microsoft.Maui.Controls.Layout layout && layout.Children.Contains(this))
        {
            layout.Children.Remove(this);
        }

        if (_overlayHost is not null)
        {
            _overlayHost.ParentChanged -= OnOverlayHostParentChanged;
            _overlayHost = null;
        }

        if (_wrappedHostContent && _hostPage is not null && _hostGrid is not null)
        {
            var original = _hostGrid.Children.OfType<View>().FirstOrDefault(child => child != this);
            if (original is not null)
            {
                _hostGrid.Children.Remove(original);
                _hostPage.Content = original;
            }
        }

        _hostPage = null;
        _hostGrid = null;
        if (ReferenceEquals(_activeDialog, this))
        {
            _activeDialog = null;
        }
    }

    private void OnHostPageSizeChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            ApplyResponsiveLayout(element.Width, element.Height);
        }
    }

    private void OnHostPageDisappearing(object? sender, EventArgs e) => CompleteDialog(null);

    private void OnOverlayHostParentChanged(object? sender, EventArgs e)
    {
        if (sender is Element { Parent: null })
        {
            CompleteDialog(null);
        }
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null && !_isClosed && _tcs is not null)
        {
            CompleteDialog(null);
        }
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        if (width <= 0)
        {
            var display = DeviceDisplay.Current.MainDisplayInfo;
            width = display.Width / display.Density;
        }

        if (height <= 0)
        {
            var display = DeviceDisplay.Current.MainDisplayInfo;
            height = display.Height / display.Density;
        }

        if (_isNumericOnly)
        {
            ApplyChromeForMode();
            return;
        }

        var isSmall = width < 720;
        var isMedium = width >= 720 && width < 1100;
        var isShort = height <= 800;
        var isVeryShort = height <= 650;

        const double horizontalPadding = 12;
        const double verticalPadding = 10;
        KeyboardCard.Margin = new Thickness(horizontalPadding, 0, horizontalPadding, verticalPadding);
        KeyboardCard.MaximumWidthRequest = isSmall ? width - (horizontalPadding * 2) : Math.Min(1100, width - (horizontalPadding * 2));
        KeyboardCard.MaximumHeightRequest = Math.Max(360, height - (verticalPadding * 2));

        var keyHeight = isVeryShort || isShort || isSmall ? 44 : isMedium ? 46 : 48;
        var keyFont = isVeryShort ? 13 : isShort ? 15 : isSmall ? 14 : isMedium ? 16 : 18;
        var actionFont = isVeryShort ? 10 : isShort || isSmall ? 11 : isMedium ? 12 : 13;
        var cornerRadius = isShort ? 10 : 14;

        foreach (var button in GetButtons(KeyboardRowsContainer))
        {
            if (ReferenceEquals(button, NumericEnterButton))
            {
                continue;
            }

            var isActionButton = button == EnterButton ||
                button.Text is "CANCEL" or "CLEAR" or "ENTER" or "DONE" or "DEL";
            button.HeightRequest = keyHeight;
            button.FontSize = isActionButton ? actionFont : keyFont;
            button.CornerRadius = cornerRadius;
        }

        KeyboardHeader.Padding = isVeryShort
            ? new Thickness(12, 6)
            : isShort ? new Thickness(14, 8) : new Thickness(18, 12);
        KeyboardRowsContainer.Padding = isVeryShort
            ? new Thickness(8, 2, 8, 6)
            : isShort ? new Thickness(10, 3, 10, 8) : new Thickness(14, 4, 14, 14);
        KeyboardRowsContainer.Spacing = isVeryShort ? 3 : isShort ? 4 : 6;
        SetKeyboardRowSpacing(isVeryShort ? 3 : isShort ? 4 : 5);

        KeyboardInputBorder.Margin = isVeryShort
            ? new Thickness(10, 5, 10, 4)
            : isShort ? new Thickness(12, 8, 12, 5) : new Thickness(20, 14, 20, 8);
        KeyboardInputBorder.HeightRequest = isVeryShort ? 44 : isShort ? 48 : 52;
        KeyboardInputEntry.HeightRequest = KeyboardInputBorder.HeightRequest - 2;
        KeyboardInputEntry.FontSize = isVeryShort ? 16 : isShort || isSmall ? 17 : isMedium ? 18 : 21;
    }

    private void SetKeyboardRowSpacing(double spacing)
    {
        foreach (var child in KeyboardRowsContainer.Children)
        {
            if (child is Grid row)
            {
                row.ColumnSpacing = spacing;
            }
        }

        foreach (var child in AlphaRowsContainer.Children)
        {
            if (child is Grid row)
            {
                row.ColumnSpacing = spacing;
            }
        }
    }

    private static IEnumerable<Button> GetButtons(IView root)
    {
        if (root is Button button)
        {
            yield return button;
            yield break;
        }

        if (root is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is IView childView)
                {
                    foreach (var nested in GetButtons(childView))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    private void UpdateDisplay()
    {
        if (!string.Equals(KeyboardInputEntry.Text, _searchText, StringComparison.Ordinal))
        {
            KeyboardInputEntry.Text = _searchText;
        }
    }

    private void FocusInput(bool moveCursorToEnd = false)
    {
        Dispatcher.Dispatch(() =>
        {
            if (!IsVisible || _isClosed)
            {
                return;
            }

            KeyboardInputEntry.Focus();
            if (moveCursorToEnd)
            {
                KeyboardInputEntry.CursorPosition = KeyboardInputEntry.Text?.Length ?? 0;
                KeyboardInputEntry.SelectionLength = 0;
            }
        });
    }

    private void ReplaceSelection(string value)
    {
        var currentText = KeyboardInputEntry.Text ?? _searchText;
        var cursorPosition = Math.Clamp(KeyboardInputEntry.CursorPosition, 0, currentText.Length);
        var selectionLength = Math.Clamp(
            KeyboardInputEntry.SelectionLength,
            0,
            currentText.Length - cursorPosition);

        var candidate = currentText.Remove(cursorPosition, selectionLength)
            .Insert(cursorPosition, value);
        var filtered = ApplyInputRules(candidate);
        if (string.Equals(filtered, currentText, StringComparison.Ordinal) &&
            !string.Equals(candidate, currentText, StringComparison.Ordinal))
        {
            return;
        }

        _searchText = filtered;
        UpdateDisplay();

        Dispatcher.Dispatch(() =>
        {
            KeyboardInputEntry.Focus();
            KeyboardInputEntry.CursorPosition = Math.Min(cursorPosition + value.Length, _searchText.Length);
            KeyboardInputEntry.SelectionLength = 0;
        });
    }

    private void OnPhysicalTextChanged(object? sender, TextChangedEventArgs e)
    {
        var filtered = ApplyInputRules(e.NewTextValue ?? string.Empty);
        _searchText = filtered;
        if (!string.Equals(filtered, e.NewTextValue, StringComparison.Ordinal))
        {
            UpdateDisplay();
        }

        HideValidation();
    }

    private void OnPhysicalEnterPressed(object? sender, EventArgs e)
    {
        OnSearchPressed(sender, e);
    }

    private void OnKeyPressed(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            ReplaceSelection(button.Text);
        }
    }

    private void OnBackspacePressed(object? sender, EventArgs e)
    {
        var currentText = KeyboardInputEntry.Text ?? _searchText;
        var cursorPosition = Math.Clamp(KeyboardInputEntry.CursorPosition, 0, currentText.Length);
        var selectionLength = Math.Clamp(
            KeyboardInputEntry.SelectionLength,
            0,
            currentText.Length - cursorPosition);

        if (selectionLength > 0)
        {
            _searchText = currentText.Remove(cursorPosition, selectionLength);
            UpdateDisplay();
            FocusInputAt(cursorPosition);
        }
        else if (cursorPosition > 0)
        {
            _searchText = currentText.Remove(cursorPosition - 1, 1);
            UpdateDisplay();
            FocusInputAt(cursorPosition - 1);
        }
    }

    private void OnSpacePressed(object? sender, EventArgs e)
    {
        ReplaceSelection(" ");
    }

    private void OnClearPressed(object? sender, EventArgs e)
    {
        _searchText = string.Empty;
        UpdateDisplay();
        FocusInput(moveCursorToEnd: true);
    }

    private void FocusInputAt(int cursorPosition)
    {
        Dispatcher.Dispatch(() =>
        {
            KeyboardInputEntry.Focus();
            KeyboardInputEntry.CursorPosition = Math.Clamp(
                cursorPosition,
                0,
                KeyboardInputEntry.Text?.Length ?? 0);
            KeyboardInputEntry.SelectionLength = 0;
        });
    }

    private const int ClickThroughGuardMs = 250;

    private void CompleteDialog(string? result)
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        SharedTouchKeyboard.SuppressAllBriefly(1200);
        _ = FinishDismissAsync(result);
    }

    private async Task FinishDismissAsync(string? result)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Keep a nearly invisible overlay for one frame of the gesture so
                // Cancel/Close cannot click-through into Mother IP / Pairing / Terminal.
                InputTransparent = false;
                Opacity = 0.02;
                IsVisible = true;
                await Task.Delay(ClickThroughGuardMs);
                CloseDialog();
                Opacity = 1;
            });
        }
        finally
        {
            _tcs?.TrySetResult(result);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CompleteDialog(null);

    private void OnCancelClicked(object? sender, TappedEventArgs e) => CompleteDialog(null);

    private void OnSearchPressed(object? sender, EventArgs e)
    {
        if (TryValidate(out var error))
        {
            CompleteDialog(_searchText);
            return;
        }

        ValidationLabel.Text = error;
        ValidationLabel.IsVisible = true;
        FocusInput(moveCursorToEnd: true);
    }

    private string ApplyInputRules(string value)
    {
        var filtered = value ?? string.Empty;
        if (_isNumericOnly)
        {
            if (_isPhoneMode)
            {
                var digits = new string(filtered.Where(char.IsDigit).ToArray());
                filtered = filtered.TrimStart().StartsWith('+') ? "+" + digits : digits;
                if (_maximumLength > 0 && filtered.Length > _maximumLength)
                {
                    filtered = filtered[.._maximumLength];
                }
                return filtered;
            }

            if (_numericMode == VirtualKeyboardNumericMode.IpAddress)
            {
                filtered = new string(filtered.Where(character => char.IsDigit(character) || character is '.' or ':').ToArray());
                if (_maximumLength > 0 && filtered.Length > _maximumLength)
                {
                    filtered = filtered[.._maximumLength];
                }
                return filtered;
            }

            var allowDecimal = _numericMode is VirtualKeyboardNumericMode.Decimal or VirtualKeyboardNumericMode.Currency;
            var decimalSeen = false;
            filtered = new string(filtered.Where(character =>
            {
                if (char.IsDigit(character)) return true;
                if (allowDecimal && character == '.' && !decimalSeen)
                {
                    decimalSeen = true;
                    return true;
                }
                return false;
            }).ToArray());

            if (allowDecimal)
            {
                var dot = filtered.IndexOf('.');
                if (dot >= 0 && filtered.Length - dot - 1 > 2)
                {
                    filtered = filtered[..(dot + 3)];
                }
            }
        }

        if (_maximumLength > 0 && filtered.Length > _maximumLength)
        {
            filtered = filtered[.._maximumLength];
        }

        return filtered;
    }

    private bool TryValidate(out string error)
    {
        error = string.Empty;
        if (_isRequired && string.IsNullOrWhiteSpace(_searchText))
        {
            error = "A value is required.";
            return false;
        }

        // IP / host addresses are not decimal amounts.
        if (_numericMode == VirtualKeyboardNumericMode.IpAddress)
        {
            return true;
        }

        if (!_isNumericOnly || string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        if (!decimal.TryParse(_searchText, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            error = "Enter a valid number.";
            return false;
        }

        if (_minimum.HasValue && number < _minimum.Value)
        {
            error = $"Enter a value of at least {_minimum.Value:0.##}.";
            return false;
        }

        if (_maximum.HasValue && number > _maximum.Value)
        {
            error = $"Enter a value no greater than {_maximum.Value:0.##}.";
            return false;
        }

        return true;
    }

    private void HideValidation()
    {
        ValidationLabel.IsVisible = false;
        ValidationLabel.Text = string.Empty;
    }
}
