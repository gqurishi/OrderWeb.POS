using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class ModernActionSheetDialog : ContentView
    {
        private TaskCompletionSource<string?>? _taskCompletionSource;
        private Grid? _parentGrid;

        public ModernActionSheetDialog()
        {
            InitializeComponent();
        }

        public void SetActionSheet(string title, List<string> options, string icon = "i", string iconBgColor = "#2563EB")
        {
            DialogBorder.WidthRequest = 450;
            TitleLabel.Text = title;
            IconBorder.BackgroundColor = ParseColor(iconBgColor, Color.FromArgb("#2563EB"));
            IconLabel.Text = NormalizeIconText(icon);
            OptionsContainer.Children.Clear();
            OptionsContainer.IsVisible = true;
            GridOptionsContainer.Children.Clear();
            GridOptionsContainer.RowDefinitions.Clear();
            GridOptionsContainer.IsVisible = false;

            foreach (var option in options)
            {
                var button = new Button
                {
                    Text = option,
                    BackgroundColor = Color.FromArgb("#F8FAFC"),
                    TextColor = Color.FromArgb("#1E293B"),
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 14,
                    HeightRequest = 50,
                    BorderColor = Color.FromArgb("#D8E1ED"),
                    BorderWidth = 1
                };
                
                button.Clicked += (s, e) =>
                {
                    _taskCompletionSource?.TrySetResult(option);
                    CloseDialog();
                };

                OptionsContainer.Children.Add(button);
            }
        }

        public void SetActionSheetGrid(string title, List<string> options, string icon = "i", string iconBgColor = "#2563EB")
        {
            DialogBorder.WidthRequest = 520;
            TitleLabel.Text = title;
            IconBorder.BackgroundColor = ParseColor(iconBgColor, Color.FromArgb("#2563EB"));
            IconLabel.Text = NormalizeIconText(icon);
            OptionsContainer.Children.Clear();
            OptionsContainer.IsVisible = false;
            GridOptionsContainer.Children.Clear();
            GridOptionsContainer.RowDefinitions.Clear();
            GridOptionsContainer.IsVisible = true;

            var rows = (int)Math.Ceiling(options.Count / 2d);
            for (var row = 0; row < rows; row++)
            {
                GridOptionsContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                var button = new Button
                {
                    Text = option,
                    BackgroundColor = Color.FromArgb("#F8FAFC"),
                    TextColor = Color.FromArgb("#1E293B"),
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 16,
                    HeightRequest = 72,
                    BorderColor = Color.FromArgb("#D8E1ED"),
                    BorderWidth = 1
                };

                button.Clicked += (s, e) =>
                {
                    _taskCompletionSource?.TrySetResult(option);
                    CloseDialog();
                };

                GridOptionsContainer.Add(button, index % 2, index / 2);
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
            if (lowered is "🧾" or "📋" or "list")
            {
                return "i";
            }

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

            return "i";
        }

        public async Task<string?> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Pages.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<string?>();
            
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

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(null);
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
