using System.Globalization;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Canonical SharedUI numeric dialog for integer, currency, quantity, and long-digit input.
/// It retains the legacy Mother-facing API while delegating all UI and validation to the
/// shared keyboard implementation.
/// </summary>
public sealed class NumericKeyboardDialog : ContentView
{
    private VirtualKeyboardDialog? _activeKeyboard;
    private bool _isShowing;

    public NumericKeyboardDialog()
    {
        IsVisible = false;
        InputTransparent = true;
    }

    public event EventHandler<int>? NumberConfirmed;
    public event EventHandler? DialogClosed;

    public async void Show(string initialValue = "")
    {
        if (_isShowing) return;
        _isShowing = true;
        InputTransparent = false;

        try
        {
            var keyboard = CreateKeyboard("Enter Number of Guests", "Confirm", initialValue, 3);
            keyboard.SetNumericMode(VirtualKeyboardNumericMode.Quantity, minimum: 1, maximum: 999);
            var value = await keyboard.ShowAsync(FindHostPage());
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                NumberConfirmed?.Invoke(this, number);
            }
        }
        finally
        {
            Finish();
        }
    }

    public async Task<decimal?> ShowCurrencyAsync(
        decimal? initialValue = null,
        string? title = null,
        Page? hostPage = null,
        decimal minimum = 0m,
        decimal maximum = 999999.99m,
        Grid? overlayHost = null)
    {
        if (_isShowing) return null;
        _isShowing = true;
        InputTransparent = false;

        try
        {
            var initialText = initialValue.HasValue
                ? initialValue.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
            var keyboard = CreateKeyboard(title ?? "Enter amount", "Done", initialText, 9);
            keyboard.SetNumericMode(VirtualKeyboardNumericMode.Currency, minimum, maximum);
            var value = overlayHost is not null
                ? await keyboard.ShowOverAsync(overlayHost, hostPage as ContentPage ?? FindHostPage())
                : await keyboard.ShowAsync(hostPage ?? FindHostPage());
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? Math.Round(amount, 2)
                : null;
        }
        finally
        {
            Finish();
        }
    }

    public async Task<string?> ShowDigitsAsync(
        string? initialValue = null,
        string? title = null,
        int maxDigits = 20,
        Page? hostPage = null,
        Grid? overlayHost = null)
    {
        if (_isShowing) return null;
        _isShowing = true;
        InputTransparent = false;

        try
        {
            var maximumLength = Math.Clamp(maxDigits, 1, 32);
            var keyboard = CreateKeyboard(title ?? "Enter number", "Done", initialValue, maximumLength);
            keyboard.SetNumericMode(VirtualKeyboardNumericMode.LongDigits);
            var value = overlayHost is not null
                ? await keyboard.ShowOverAsync(overlayHost, hostPage as ContentPage ?? FindHostPage())
                : await keyboard.ShowAsync(hostPage ?? FindHostPage());
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            Finish();
        }
    }

    public void Hide()
    {
        _activeKeyboard?.Cancel();
        if (!_isShowing) Finish();
    }

    public void AddToPage(Grid parentGrid)
    {
        if (Parent is null) parentGrid.Children.Add(this);
    }

    private VirtualKeyboardDialog CreateKeyboard(
        string title,
        string action,
        string? initialValue,
        int maximumLength)
    {
        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(title, action);
        keyboard.SetPlaceholder("Tap the keys below");
        keyboard.SetMaximumLength(maximumLength);
        keyboard.SetRequired(true);
        keyboard.SetInitialText(initialValue ?? string.Empty);
        _activeKeyboard = keyboard;
        return keyboard;
    }

    private void Finish()
    {
        var wasShowing = _isShowing;
        _isShowing = false;
        _activeKeyboard = null;
        InputTransparent = true;
        if (wasShowing) DialogClosed?.Invoke(this, EventArgs.Empty);
    }

    private ContentPage? FindHostPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is ContentPage page) return page;
            current = current.Parent;
        }

        return Shell.Current?.CurrentPage as ContentPage
            ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;
    }
}
