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

        public void SetCancelText(string text)
        {
            CancelButton.Text = text;
        }

        public void HighlightGridOption(string optionText, string backgroundColor, string textColor, string borderColor)
        {
            foreach (var child in GridOptionsContainer.Children)
            {
                if (child is Button button && string.Equals(button.Text, optionText, StringComparison.OrdinalIgnoreCase))
                {
                    button.BackgroundColor = ParseColor(backgroundColor, button.BackgroundColor);
                    button.TextColor = ParseColor(textColor, button.TextColor);
                    button.BorderColor = ParseColor(borderColor, button.BorderColor);
                }
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
            if (lowered is "list")
            {
                return "i";
            }

            if (lowered is "warning")
            {
                return "!";
            }

            if (lowered is "ok" or "success")
            {
                return "OK";
            }

            if (lowered is "x" or "error")
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
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<string?>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                _taskCompletionSource.TrySetResult(null);
                return null;
            }

            return await _taskCompletionSource.Task;
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(null);
            CloseDialog();
        }

        private void CloseDialog()
        {
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
