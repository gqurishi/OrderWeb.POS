using Microsoft.Maui.Controls;
using System;

namespace POS_in_NET.Views
{
    public partial class NumericKeyboardDialog : ContentView
    {
        private string _currentValue = "";
        private int _maxDigits = 3; // Maximum 999 guests
        
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
            _currentValue = initialValue;
            UpdateDisplay();
            this.InputTransparent = false; // Allow input when visible
            DialogOverlay.IsVisible = true;
        }

        /// <summary>
        /// Hide the dialog
        /// </summary>
        public void Hide()
        {
            DialogOverlay.IsVisible = false;
            this.InputTransparent = true; // Pass through input when hidden
            DialogClosed?.Invoke(this, EventArgs.Empty);
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
            DisplayLabel.Text = string.IsNullOrEmpty(_currentValue) ? "0" : _currentValue;
        }

        private void OnNumberClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                string digit = button.Text;
                
                // Don't allow more than max digits
                if (_currentValue.Length >= _maxDigits) return;
                
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
            }
        }

        private void OnDeleteClicked(object sender, EventArgs e)
        {
            if (_currentValue.Length > 0)
            {
                _currentValue = _currentValue.Substring(0, _currentValue.Length - 1);
                UpdateDisplay();
            }
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _currentValue = "";
            UpdateDisplay();
        }

        private void OnConfirmClicked(object sender, EventArgs e)
        {
            if (int.TryParse(_currentValue, out int number) && number > 0)
            {
                NumberConfirmed?.Invoke(this, number);
                Hide();
            }
            else
            {
                // Flash the display to indicate invalid input
                DisplayLabel.TextColor = Color.FromArgb("#DC2626");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(300);
                    DisplayLabel.TextColor = Color.FromArgb("#1E293B");
                });
            }
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            Hide();
        }

        private void OnOverlayTapped(object sender, EventArgs e)
        {
            // Don't close when tapping the dialog itself
            // This is handled by the overlay background
        }
    }
}
