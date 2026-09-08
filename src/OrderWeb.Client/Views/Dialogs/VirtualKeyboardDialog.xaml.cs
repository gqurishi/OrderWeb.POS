using Microsoft.Maui.Devices;

namespace OrderWeb.Client.Views.Dialogs;

/// <summary>Mother-parity on-screen keyboard overlay for Collection/Delivery customer fields.</summary>
public partial class VirtualKeyboardDialog : ContentView
{
    private TaskCompletionSource<string?>? _tcs;
    private string _searchText = string.Empty;
    private ContentPage? _hostPage;
    private Grid? _hostGrid;
    private bool _isClosed;
    private bool _isCompleting;
    private bool _wrappedHostContent;

    public VirtualKeyboardDialog()
    {
        InitializeComponent();
    }

    public void SetInitialText(string text)
    {
        _searchText = text ?? string.Empty;
        UpdateDisplay();
    }

    public void SetPrompt(string title, string actionText = "ENTER")
    {
        KeyboardTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Keyboard" : title.Trim();
        EnterButton.Text = string.IsNullOrWhiteSpace(actionText) ? "ENTER" : actionText.Trim().ToUpperInvariant();
    }

    public async Task<string?> ShowAsync(Page? hostPage = null)
    {
        _tcs = new TaskCompletionSource<string?>();
        _isClosed = false;
        _isCompleting = false;

        var page = hostPage as ContentPage
            ?? Shell.Current?.CurrentPage as ContentPage
            ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;

        if (page is null)
        {
            _tcs.TrySetResult(null);
            return null;
        }

        AddToPage(page);
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

        ZIndex = 10000;
        IsVisible = true;
        page.SizeChanged += OnHostPageSizeChanged;
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
        if (_hostPage is not null)
        {
            _hostPage.SizeChanged -= OnHostPageSizeChanged;
        }

        if (_hostGrid is not null && _hostGrid.Children.Contains(this))
        {
            _hostGrid.Children.Remove(this);
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
        IsVisible = false;
    }

    private void OnHostPageSizeChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            ApplyResponsiveLayout(element.Width, element.Height);
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
            var isActionButton = button == EnterButton || button.Text is "CANCEL" or "CLEAR" or "ENTER" or "DONE" or "DEL";
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

        _searchText = currentText.Remove(cursorPosition, selectionLength)
            .Insert(cursorPosition, value);
        UpdateDisplay();

        Dispatcher.Dispatch(() =>
        {
            KeyboardInputEntry.Focus();
            KeyboardInputEntry.CursorPosition = cursorPosition + value.Length;
            KeyboardInputEntry.SelectionLength = 0;
        });
    }

    private void OnPhysicalTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue ?? string.Empty;
    }

    private void OnPhysicalEnterPressed(object? sender, EventArgs e)
    {
        CompleteDialog(_searchText);
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

    private void CompleteDialog(string? result)
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        try
        {
            CloseDialog();
        }
        finally
        {
            _tcs?.TrySetResult(result);
        }
    }

    private void OnSearchPressed(object? sender, EventArgs e)
    {
        CompleteDialog(_searchText);
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        CompleteDialog(null);
    }
}
