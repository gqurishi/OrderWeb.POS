using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Globalization;

namespace POS_in_NET.Views
{
    public partial class NumericKeyboardDialog : ContentView
    {
        private string _currentValue = "";
        private int _maxDigits = 3; // Maximum 999 guests
        private bool _isUpdatingInput;
        private bool _isCurrencyMode;
        private bool _isDigitMode;
        private TaskCompletionSource<decimal?>? _currencyCompletionSource;
        private TaskCompletionSource<string?>? _digitCompletionSource;
        private Grid? _dynamicParentGrid;
        
        public event EventHandler<int>? NumberConfirmed;
        public event EventHandler? DialogClosed;

        public NumericKeyboardDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show the numeric keyboard dialog
        /// </summary>
        public void Show(string initialValue = "")
        {
            ConfigureIntegerMode();
            ShowCore(initialValue);
        }

        /// <summary>
        /// Shows the same touch-friendly keypad in currency mode. Physical keyboard
        /// input continues to work through the real Entry inside the dialog.
        /// </summary>
        public Task<decimal?> ShowCurrencyAsync(decimal? initialValue = null, string? title = null)
        {
            ConfigureCurrencyMode(title);
            _currencyCompletionSource = new TaskCompletionSource<decimal?>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _dynamicParentGrid))
            {
                _currencyCompletionSource.TrySetResult(null);
                return _currencyCompletionSource.Task;
            }

            ZIndex = 20000;
            var initialText = initialValue is > 0
                ? initialValue.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
            ShowCore(initialText);
            return _currencyCompletionSource.Task;
        }

        /// <summary>
        /// Shows the keypad for long digit strings (gift card numbers, etc.).
        /// </summary>
        public Task<string?> ShowDigitsAsync(
            string? initialValue = null,
            string? title = null,
            int maxDigits = 20,
            Page? hostPage = null)
        {
            ConfigureDigitMode(title, maxDigits);
            _digitCompletionSource = new TaskCompletionSource<string?>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _dynamicParentGrid, hostPage))
            {
                _digitCompletionSource.TrySetResult(null);
                return _digitCompletionSource.Task;
            }

            ZIndex = 20000;
            ShowCore(initialValue?.Trim() ?? string.Empty);
            return _digitCompletionSource.Task;
        }

        private void ShowCore(string initialValue)
        {
            _currentValue = initialValue;
            UpdateDisplay();
            ZIndex = Math.Max(ZIndex, 20000);
            InputTransparent = false;
            DialogOverlay.InputTransparent = false;
            DialogOverlay.IsVisible = true;
            IsVisible = true;
            FocusInput();
        }

        /// <summary>
        /// Hide the dialog
        /// </summary>
        public void Hide()
        {
            // Drop hit-testing before detach so no dismiss path leaves a full-screen blocker.
            DialogOverlay.IsVisible = false;
            DialogOverlay.InputTransparent = true;
            InputTransparent = true;
            NumericInputEntry.Unfocus();

            if (_dynamicParentGrid != null)
            {
                DialogOverlayHelper.DetachOverlay(this, _dynamicParentGrid);
                _dynamicParentGrid = null;
                ZIndex = 0;
            }

            DialogClosed?.Invoke(this, EventArgs.Empty);
        }

        private void ConfigureIntegerMode()
        {
            _isCurrencyMode = false;
            _isDigitMode = false;
            _maxDigits = 3;
            KeyboardTitleLabel.Text = "Enter Number of Guests";
            CurrencyDisplayRow.IsVisible = false;
            NumericInputEntry.IsVisible = true;
            UtilityButton.Text = "C";
            ConfirmButton.Text = "CONFIRM";
            NumericInputEntry.MaxLength = 3;
            NumericInputEntry.HorizontalTextAlignment = TextAlignment.Center;
            NumericInputEntry.IsReadOnly = false;
        }

        private void ConfigureDigitMode(string? title, int maxDigits)
        {
            _isCurrencyMode = false;
            _isDigitMode = true;
            _maxDigits = Math.Clamp(maxDigits, 1, 32);
            KeyboardTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Enter number" : title.Trim();
            CurrencyDisplayRow.IsVisible = false;
            NumericInputEntry.IsVisible = true;
            UtilityButton.Text = "C";
            ConfirmButton.Text = "DONE";
            NumericInputEntry.MaxLength = _maxDigits;
            NumericInputEntry.HorizontalTextAlignment = TextAlignment.Center;
            NumericInputEntry.IsReadOnly = true;
        }

        private void ConfigureCurrencyMode(string? title = null)
        {
            _isCurrencyMode = true;
            _isDigitMode = false;
            _maxDigits = 8;
            KeyboardTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Enter amount" : title.Trim();
            CurrencyDisplayRow.IsVisible = true;
            NumericInputEntry.IsVisible = false;
            UtilityButton.Text = ".";
            ConfirmButton.Text = "DONE";
            NumericInputEntry.MaxLength = 9;
            NumericInputEntry.IsReadOnly = true;
        }

        /// <summary>
        /// Add this dialog to a page
        /// </summary>
        public void AddToPage(Grid parentGrid)
        {
            // Add to the last row spanning all columns
            Grid.SetRowSpan(this, 10);
            Grid.SetColumnSpan(this, 10);
            parentGrid.Children.Add(this);
        }

        private void UpdateDisplay()
        {
            if (_isCurrencyMode)
            {
                CurrencyAmountLabel.Text = string.IsNullOrEmpty(_currentValue) ? "0" : _currentValue;
                return;
            }

            if (!string.Equals(NumericInputEntry.Text, _currentValue, StringComparison.Ordinal))
            {
                _isUpdatingInput = true;
                NumericInputEntry.Text = _currentValue;
                _isUpdatingInput = false;
            }
        }

        private void FocusInput()
        {
            if (_isCurrencyMode || _isDigitMode)
            {
                return;
            }

            Dispatcher.Dispatch(() =>
            {
                if (!DialogOverlay.IsVisible)
                {
                    return;
                }

                NumericInputEntry.Focus();
                NumericInputEntry.CursorPosition = NumericInputEntry.Text?.Length ?? 0;
                NumericInputEntry.SelectionLength = 0;
            });
        }

        private void OnPhysicalTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInput)
            {
                return;
            }

            var sanitized = _isCurrencyMode
                ? SanitizeCurrency(e.NewTextValue)
                : new string((e.NewTextValue ?? string.Empty)
                    .Where(char.IsDigit)
                    .Take(_maxDigits)
                    .ToArray());
            _currentValue = sanitized;

            if (!string.Equals(e.NewTextValue, sanitized, StringComparison.Ordinal))
            {
                UpdateDisplay();
                FocusInput();
            }
        }

        private void OnPhysicalConfirm(object? sender, EventArgs e)
        {
            OnConfirmClicked(sender ?? this, e);
        }

        private void OnNumberClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                string digit = button.Text;
                
                var digitCount = _currentValue.Count(char.IsDigit);
                if (digitCount >= _maxDigits ||
                    (_isCurrencyMode && _currentValue.Contains('.') && _currentValue[( _currentValue.IndexOf('.') + 1)..].Length >= 2))
                {
                    FocusInput();
                    return;
                }
                
                // Don't allow leading zeros (except single zero)
                if (_currentValue == "0")
                {
                    _currentValue = digit;
                }
                else
                {
                    _currentValue += digit;
                }
                
                UpdateDisplay();
                FocusInput();
            }
        }

        private void OnDeleteClicked(object sender, EventArgs e)
        {
            if (_currentValue.Length > 0)
            {
                _currentValue = _currentValue.Substring(0, _currentValue.Length - 1);
                UpdateDisplay();
            }
            FocusInput();
        }

        private void OnUtilityClicked(object sender, EventArgs e)
        {
            if (_isCurrencyMode)
            {
                if (!_currentValue.Contains('.'))
                {
                    _currentValue = string.IsNullOrEmpty(_currentValue) ? "0." : _currentValue + ".";
                }
            }
            else
            {
                _currentValue = "";
            }

            UpdateDisplay();
            FocusInput();
        }

        private void OnConfirmClicked(object sender, EventArgs e)
        {
            if (_isCurrencyMode)
            {
                if (decimal.TryParse(_currentValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount >= 0)
                {
                    _currencyCompletionSource?.TrySetResult(Math.Round(amount, 2));
                    Hide();
                    return;
                }

                ShowInvalidInput();
                return;
            }

            if (_isDigitMode)
            {
                if (string.IsNullOrWhiteSpace(_currentValue))
                {
                    ShowInvalidInput();
                    return;
                }

                _digitCompletionSource?.TrySetResult(_currentValue);
                Hide();
                return;
            }

            if (int.TryParse(_currentValue, out int number) && number > 0)
            {
                NumberConfirmed?.Invoke(this, number);
                Hide();
            }
            else
            {
                ShowInvalidInput();
            }
        }

        private void ShowInvalidInput()
        {
            if (_isCurrencyMode)
            {
                CurrencyAmountLabel.TextColor = Color.FromArgb("#DC2626");
            }
            else
            {
                NumericInputEntry.TextColor = Color.FromArgb("#DC2626");
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(300);
                if (_isCurrencyMode)
                {
                    CurrencyAmountLabel.TextColor = Color.FromArgb("#1E293B");
                }
                else
                {
                    NumericInputEntry.TextColor = Color.FromArgb("#1E293B");
                }

                FocusInput();
            });
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            if (_isCurrencyMode)
            {
                _currencyCompletionSource?.TrySetResult(null);
            }
            else if (_isDigitMode)
            {
                _digitCompletionSource?.TrySetResult(null);
            }

            Hide();
        }

        private static string SanitizeCurrency(string? input)
        {
            var result = new System.Text.StringBuilder();
            var decimalSeen = false;
            var decimalPlaces = 0;
            var wholeDigits = 0;

            foreach (var character in input ?? string.Empty)
            {
                if (char.IsDigit(character))
                {
                    if (decimalSeen)
                    {
                        if (decimalPlaces >= 2)
                        {
                            continue;
                        }

                        decimalPlaces++;
                    }
                    else if (wholeDigits++ >= 6)
                    {
                        continue;
                    }

                    result.Append(character);
                }
                else if ((character == '.' || character == ',') && !decimalSeen)
                {
                    if (result.Length == 0)
                    {
                        result.Append('0');
                    }

                    result.Append('.');
                    decimalSeen = true;
                }
            }

            return result.ToString();
        }

        private void OnOverlayTapped(object sender, EventArgs e)
        {
            // Don't close when tapping the dialog itself
            // This is handled by the overlay background
        }
    }
}
