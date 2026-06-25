using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class ModernConfirmDialog : ContentView
    {
        private TaskCompletionSource<bool>? _taskCompletionSource;
        private Grid? _parentGrid;
        private string _title = "Confirm";
        private string _message = string.Empty;
        private string _yesText = "Yes";
        private string _noText = "No";

        public ModernConfirmDialog()
        {
            InitializeComponent();
        }

        public void SetConfirm(string title, string message, string yesText = "Yes", string noText = "No", string icon = "!", string iconBgColor = "#2563EB")
        {
            _title = title?.Trim() ?? "Confirm";
            _message = message?.Trim() ?? string.Empty;
            _yesText = yesText;
            _noText = noText;
            TitleLabel.Text = _title;
            MessageLabel.Text = _message;
            YesButton.Text = _yesText;
            NoButton.Text = _noText;
            
            var normalizedIcon = NormalizeIconText(icon);
            var useLogo = normalizedIcon == "__LOGO__";
            IconBorder.BackgroundColor = useLogo
                ? Colors.White
                : ParseColor(iconBgColor, Color.FromArgb("#2563EB"));
            IconBorder.Stroke = new SolidColorBrush(useLogo
                ? Color.FromArgb("#E2E8F0")
                : Colors.White);
            IconImage.IsVisible = useLogo;
            IconLabel.IsVisible = !useLogo;
            IconLabel.Text = useLogo ? string.Empty : normalizedIcon;

            var loweredTitle = _title.ToLowerInvariant();
            var loweredYesText = _yesText.ToLowerInvariant();
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
            if (lowered is "warning")
            {
                return "!";
            }

            if (lowered is "logo" or "company" or "orderweb")
            {
                return "__LOGO__";
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

            return "!";
        }

        public async Task<bool> ShowAsync()
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<bool>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                var page = Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
                if (page != null)
                {
                    var result = await page.DisplayAlert(_title, _message, _yesText, _noText);
                    return result;
                }

                return false;
            }

            return await _taskCompletionSource.Task;
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
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
