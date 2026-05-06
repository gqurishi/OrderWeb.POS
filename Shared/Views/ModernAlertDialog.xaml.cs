using Microsoft.Maui.Controls;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class ModernAlertDialog : ContentView
    {
        private TaskCompletionSource<bool>? _taskCompletionSource;
        private Grid? _parentGrid;

        public ModernAlertDialog()
        {
            InitializeComponent();
        }

        public void SetAlert(string title, string message, string icon = "i", string iconBgColor = "#3B82F6", string buttonColor = "#2563EB", string buttonTextColor = "White")
        {
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            IconBorder.BackgroundColor = ParseColor(iconBgColor, Color.FromArgb("#3B82F6"));
            
            IconLabel.Text = NormalizeIconText(icon);
            
            var parsedButtonColor = ParseColor(buttonColor, Color.FromArgb("#2563EB"));
            OkButton.BackgroundColor = parsedButtonColor;
            OkButton.TextColor = ParseColor(buttonTextColor, Colors.White);

            if (IsLightColor(parsedButtonColor))
            {
                OkButton.BackgroundColor = Color.FromArgb("#2563EB");
                OkButton.TextColor = Colors.White;
            }
        }

        private static Color ParseColor(string? value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (Color.TryParse(value, out var parsed) && parsed != null)
            {
                return parsed;
            }

            return fallback;
        }

        private static string NormalizeIconText(string? icon)
        {
            var input = (icon ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(input))
            {
                return "i";
            }

            var lowered = input.ToLowerInvariant();

            if (lowered is "✅" or "ok" or "success" or "done" or "check")
            {
                return "OK";
            }

            if (lowered is "❌" or "x" or "error" or "fail" or "failed")
            {
                return "X";
            }

            if (lowered is "⚠️" or "⚠" or "!" or "warning")
            {
                return "!";
            }

            if (lowered is "ℹ️" or "ℹ" or "i" or "info")
            {
                return "i";
            }

            if (input.Length <= 3)
            {
                return input.ToUpper(CultureInfo.InvariantCulture);
            }

            return "i";
        }

        private static bool IsLightColor(Color color)
        {
            var luminance = (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
            return luminance > 0.78;
        }

        public async Task ShowAsync()
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
            
            if (Application.Current?.MainPage != null)
            {
                var pageContent = GetPageContent(Application.Current.MainPage);
                if (pageContent is Grid mainGrid)
                {
                    _parentGrid = mainGrid;
                    
                    Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                    Grid.SetRow(this, 0);
                    Grid.SetColumn(this, 0);
                    
                    mainGrid.Children.Add(this);
                }
            }

            await _taskCompletionSource.Task;
        }

        private View? GetPageContent(Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage currentPage)
            {
                return currentPage.Content;
            }
            else if (page is ContentPage contentPage)
            {
                return contentPage.Content;
            }
            return null;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(true);
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }
    }
}
