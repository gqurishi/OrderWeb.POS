using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Controls
{
    public enum ToastPlacement
    {
        TopRight,
        BottomCenter
    }

    public partial class ToastNotification : ContentView
    {
        private bool _isShowing = false;

        public static readonly BindableProperty PlacementProperty =
            BindableProperty.Create(
                nameof(Placement),
                typeof(ToastPlacement),
                typeof(ToastNotification),
                ToastPlacement.TopRight);

        public ToastPlacement Placement
        {
            get => (ToastPlacement)GetValue(PlacementProperty);
            set => SetValue(PlacementProperty, value);
        }

        public ToastNotification()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show a toast on the current page without blocking the till.
        /// Falls back silently when the page has no toast host.
        /// </summary>
        public static Task ShowGlobalAsync(string title, string message, NotificationType type, int durationMs = 2200)
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    var page = Shell.Current?.CurrentPage;
                    if (page != null)
                    {
                        var toast = FindToast(page);
                        if (toast != null)
                        {
                            await toast.ShowAsync(title, message, type, durationMs);
                            return;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[Toast] {title}: {message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Toast] ShowGlobalAsync failed: {ex.Message}");
                }
            });
        }

        private static ToastNotification? FindToast(Element root, int depth = 0)
        {
            if (depth > 8)
            {
                return null;
            }

            if (root is ToastNotification toast)
            {
                return toast;
            }

            if (root is IVisualTreeElement tree)
            {
                foreach (var child in tree.GetVisualChildren())
                {
                    if (child is Element element)
                    {
                        var found = FindToast(element, depth + 1);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Show toast notification with automatic styling and dismiss
        /// </summary>
        public async Task ShowAsync(string title, string message, NotificationType type, int durationMs = 2000)
        {
            // Don't show if already showing
            if (_isShowing) return;
            
            _isShowing = true;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Set content
                TitleLabel.Text = title;
                MessageLabel.Text = message;

                // Set colors and icon based on type
                switch (type)
                {
                    case NotificationType.Success:
                        ToastBorder.BackgroundColor = Colors.White;
                        ToastBorder.Stroke = Color.FromArgb("#BBF7D0");
                        IconContainer.BackgroundColor = Color.FromArgb("#ECFDF5");
                        IconLabel.TextColor = Color.FromArgb("#047857");
                        IconLabel.Text = "OK";
                        break;

                    case NotificationType.Error:
                        ToastBorder.BackgroundColor = Colors.White;
                        ToastBorder.Stroke = Color.FromArgb("#FECACA");
                        IconContainer.BackgroundColor = Color.FromArgb("#FEF2F2");
                        IconLabel.TextColor = Color.FromArgb("#DC2626");
                        IconLabel.Text = "X";
                        break;

                    case NotificationType.Warning:
                        ToastBorder.BackgroundColor = Colors.White;
                        ToastBorder.Stroke = Color.FromArgb("#DDD6FE");
                        IconContainer.BackgroundColor = Color.FromArgb("#F5F3FF");
                        IconLabel.TextColor = Color.FromArgb("#6D28D9");
                        IconLabel.Text = "!";
                        break;

                    case NotificationType.Info:
                        ToastBorder.BackgroundColor = Colors.White;
                        ToastBorder.Stroke = Color.FromArgb("#BFDBFE");
                        IconContainer.BackgroundColor = Color.FromArgb("#EFF6FF");
                        IconLabel.TextColor = Color.FromArgb("#1D4ED8");
                        IconLabel.Text = "i";
                        break;
                }

                // Position + animate in
                if (Placement == ToastPlacement.BottomCenter)
                {
                    ToastBorder.HorizontalOptions = LayoutOptions.Center;
                    ToastBorder.VerticalOptions = LayoutOptions.End;
                    ToastBorder.Margin = new Thickness(16, 0, 16, 24);
                    ToastBorder.Opacity = 0;
                    ToastBorder.TranslationX = 0;
                    ToastBorder.TranslationY = 120;

                    await Task.WhenAll(
                        ToastBorder.FadeTo(1, 300, Easing.CubicOut),
                        ToastBorder.TranslateTo(0, 0, 300, Easing.CubicOut)
                    );
                }
                else
                {
                    // Animate in (slide from right + fade in)
                    ToastBorder.HorizontalOptions = LayoutOptions.End;
                    ToastBorder.VerticalOptions = LayoutOptions.Start;
                    ToastBorder.Margin = new Thickness(0, 20, 20, 0);
                    ToastBorder.Opacity = 0;
                    ToastBorder.TranslationY = 0;
                    ToastBorder.TranslationX = 400;

                    await Task.WhenAll(
                        ToastBorder.FadeTo(1, 350, Easing.CubicOut),
                        ToastBorder.TranslateTo(0, 0, 350, Easing.CubicOut)
                    );
                }

                // Wait for duration
                await Task.Delay(durationMs);

                // Animate out
                if (Placement == ToastPlacement.BottomCenter)
                {
                    await Task.WhenAll(
                        ToastBorder.FadeTo(0, 250, Easing.CubicIn),
                        ToastBorder.TranslateTo(0, 80, 250, Easing.CubicIn)
                    );
                }
                else
                {
                    // Slide to right + fade out
                    await Task.WhenAll(
                        ToastBorder.FadeTo(0, 300, Easing.CubicIn),
                        ToastBorder.TranslateTo(100, 0, 300, Easing.CubicIn)
                    );
                }

                _isShowing = false;
            });
        }

        /// <summary>
        /// Close button handler
        /// </summary>
        private async void OnCloseTapped(object? sender, EventArgs e)
        {
            if (!_isShowing) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Placement == ToastPlacement.BottomCenter)
                {
                    await Task.WhenAll(
                        ToastBorder.FadeTo(0, 200, Easing.CubicIn),
                        ToastBorder.TranslateTo(0, 80, 200, Easing.CubicIn)
                    );
                }
                else
                {
                    // Quick slide out to the right
                    await Task.WhenAll(
                        ToastBorder.FadeTo(0, 200, Easing.CubicIn),
                        ToastBorder.TranslateTo(100, 0, 200, Easing.CubicIn)
                    );
                }

                _isShowing = false;
            });
        }
    }
}
