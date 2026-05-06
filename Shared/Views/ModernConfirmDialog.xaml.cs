using Microsoft.Maui.Controls;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class ModernConfirmDialog : ContentView
    {
        private TaskCompletionSource<bool>? _taskCompletionSource;
        private Grid? _parentGrid;

        public ModernConfirmDialog()
        {
            InitializeComponent();
        }

        public void SetConfirm(string title, string message, string yesText = "Yes", string noText = "No", string icon = "!", string iconBgColor = "#2563EB")
        {
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            YesButton.Text = yesText;
            NoButton.Text = noText;
            
            IconBorder.BackgroundColor = ParseColor(iconBgColor, Color.FromArgb("#2563EB"));
            IconLabel.Text = NormalizeIconText(icon);

            var loweredTitle = (title ?? string.Empty).ToLowerInvariant();
            var loweredYesText = (yesText ?? string.Empty).ToLowerInvariant();
            var isDestructive = loweredTitle.Contains("danger") || loweredTitle.Contains("delete") || loweredTitle.Contains("void") || loweredYesText.Contains("delete") || loweredYesText.Contains("void");
            if (isDestructive)
            {
                YesButton.BackgroundColor = Color.FromArgb("#DC2626");
                YesButton.TextColor = Colors.White;
            }
            else
            {
                YesButton.BackgroundColor = Color.FromArgb("#2563EB");
                YesButton.TextColor = Colors.White;
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
                return "!";
            }

            var lowered = input.ToLowerInvariant();
            if (lowered is "⚠️" or "⚠" or "warning")
            {
                return "!";
            }

            if (lowered is "✅" or "ok" or "success")
            {
                return "OK";
            }

            if (lowered is "❌" or "x" or "error")
            {
                return "X";
            }

            if (input.Length <= 3)
            {
                return input.ToUpper(CultureInfo.InvariantCulture);
            }

            return "!";
        }

        public async Task<bool> ShowAsync()
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

            return await _taskCompletionSource.Task;
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

        private void OnYesClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(true);
            CloseDialog();
        }

        private void OnNoClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(false);
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
