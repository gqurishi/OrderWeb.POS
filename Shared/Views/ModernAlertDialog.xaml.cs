using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class ModernAlertDialog : ContentView
    {
        private TaskCompletionSource<bool>? _taskCompletionSource;
        private Grid? _parentGrid;
        private string _title = "Info";
        private string _message = string.Empty;

        public ModernAlertDialog()
        {
            InitializeComponent();
        }

        public void SetAlert(string title, string message, string icon = "i", string iconBgColor = "#3B82F6", string buttonColor = "#2563EB", string buttonTextColor = "White")
        {
            _title = title?.Trim() ?? "Info";
            _message = message?.Trim() ?? string.Empty;
            TitleLabel.Text = _title;
            MessageLabel.Text = _message;
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

            if (lowered is "ok" or "success" or "done" or "check")
            {
                return "OK";
            }

            if (lowered is "x" or "error" or "fail" or "failed")
            {
                return "X";
            }

            if (lowered is "!" or "warning")
            {
                return "!";
            }

            if (lowered is "i" or "info")
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
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<bool>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                var page = Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
                if (page != null)
                {
                    await page.DisplayAlert(_title, _message, "OK");
                }

                _taskCompletionSource.TrySetResult(true);
                return;
            }

            await _taskCompletionSource.Task;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(true);
            CloseDialog();
        }

        private void CloseDialog()
        {
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
