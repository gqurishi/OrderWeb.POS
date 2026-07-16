using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class VirtualKeyboardDialog : ContentView
    {
        private TaskCompletionSource<string?>? _tcs;
        private string _searchText = string.Empty;
        private ContentPage? _hostPage;
        private Grid? _hostGrid;
        private bool _isClosed;
        private bool _isCompleting;

        public VirtualKeyboardDialog()
        {
            InitializeComponent();
            StartCursorBlink();
        }

        public void SetInitialText(string text)
        {
            _searchText = text ?? string.Empty;
            UpdateDisplay();
        }

        public void SetPrompt(string title, string actionText = "ENTER")
        {
            KeyboardTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Keyboard" : title.Trim();
            EnterButton.Text = string.IsNullOrWhiteSpace(actionText) ? "ENTER" : actionText.Trim().ToUpperInvariant();
        }

        public async Task<string?> ShowAsync(Page? hostPage = null)
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _tcs = new TaskCompletionSource<string?>();
            _isClosed = false;
            _isCompleting = false;

            var page = hostPage as ContentPage
                ?? Shell.Current?.CurrentPage as ContentPage
                ?? Application.Current?.MainPage as ContentPage;

            if (page != null)
            {
                AddToPage(page);
            }
            else
            {
                _tcs.TrySetResult(null);
                return null;
            }

            return await _tcs.Task;
        }

        private void AddToPage(ContentPage page)
        {
            _hostPage = page;

            if (DialogOverlayHelper.TryAttachOverlay(this, out var hostGrid))
            {
                _hostGrid = hostGrid;
                ZIndex = 10000;
                IsVisible = true;
                page.SizeChanged += OnHostPageSizeChanged;
                ApplyResponsiveLayout(page.Width, page.Height);
                return;
            }

            if (page.Content is Layout layout)
            {
                var newGrid = new Grid();
                var existingContent = page.Content;
                page.Content = null;
                newGrid.Children.Add(existingContent);
                newGrid.Children.Add(this);
                page.Content = newGrid;
                _hostGrid = newGrid;
            }
            else
            {
                var newGrid = new Grid();
                var existingContent = page.Content;
                page.Content = null;

                if (existingContent != null)
                {
                    newGrid.Children.Add(existingContent);
                }

                newGrid.Children.Add(this);
                page.Content = newGrid;
                _hostGrid = newGrid;
            }

            IsVisible = true;
            page.SizeChanged += OnHostPageSizeChanged;
            ApplyResponsiveLayout(page.Width, page.Height);
        }

        private void CloseDialog()
        {
            if (_isClosed)
            {
                return;
            }

            _isClosed = true;
            if (_hostPage != null)
            {
                _hostPage.SizeChanged -= OnHostPageSizeChanged;
                _hostPage = null;
            }

            DialogOverlayHelper.DetachOverlay(this, _hostGrid);
            if (Parent is Layout parentLayout && parentLayout.Children.Contains(this))
            {
                parentLayout.Children.Remove(this);
            }

            _hostGrid = null;
            IsVisible = false;
        }

        private void OnHostPageSizeChanged(object? sender, EventArgs e)
        {
            if (sender is VisualElement element)
            {
                ApplyResponsiveLayout(element.Width, element.Height);
            }
        }

        private void ApplyResponsiveLayout(double width, double height)
        {
            if (width <= 0)
            {
                var display = DeviceDisplay.Current.MainDisplayInfo;
                width = display.Width / display.Density;
            }

            var isSmall = width < 720;
            var isMedium = width >= 720 && width < 1100;

            var horizontalPadding = isSmall ? 8 : 12;
            KeyboardCard.Margin = new Thickness(horizontalPadding, 0, horizontalPadding, isSmall ? 8 : 10);
            KeyboardCard.MaximumWidthRequest = isSmall ? width - (horizontalPadding * 2) : Math.Min(1100, width - (horizontalPadding * 2));

            var keyHeight = isSmall ? 40 : isMedium ? 44 : 48;
            var keyFont = isSmall ? 14 : isMedium ? 16 : 18;
            var actionFont = isSmall ? 11 : isMedium ? 12 : 13;

            foreach (var button in GetButtons(KeyboardRowsContainer))
            {
                var isActionButton = button == EnterButton || button.Text is "CANCEL" or "CLEAR" or "ENTER" or "DONE" or "DEL";
                button.HeightRequest = keyHeight;
                button.FontSize = isActionButton ? actionFont : keyFont;
            }

            SearchTextLabel.FontSize = isSmall ? 17 : isMedium ? 18 : 21;
            CursorLabel.FontSize = SearchTextLabel.FontSize;
        }

        private static IEnumerable<Button> GetButtons(IView root)
        {
            if (root is Button button)
            {
                yield return button;
                yield break;
            }

            if (root is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    if (child is IView childView)
                    {
                        foreach (var nested in GetButtons(childView))
                        {
                            yield return nested;
                        }
                    }
                }
            }
        }

        private void UpdateDisplay()
        {
            SearchTextLabel.Text = _searchText;
        }

        private void StartCursorBlink()
        {
            // Simple cursor blink animation
            Device.StartTimer(TimeSpan.FromMilliseconds(500), () =>
            {
                if (IsVisible)
                {
                    CursorLabel.IsVisible = !CursorLabel.IsVisible;
                    return true;
                }
                return false;
            });
        }

        private void OnKeyPressed(object? sender, EventArgs e)
        {
            if (sender is Button button)
            {
                _searchText += button.Text;
                UpdateDisplay();
            }
        }

        private void OnBackspacePressed(object? sender, EventArgs e)
        {
            if (_searchText.Length > 0)
            {
                _searchText = _searchText.Substring(0, _searchText.Length - 1);
                UpdateDisplay();
            }
        }

        private void OnSpacePressed(object? sender, EventArgs e)
        {
            _searchText += " ";
            UpdateDisplay();
        }

        private void OnClearPressed(object? sender, EventArgs e)
        {
            _searchText = string.Empty;
            UpdateDisplay();
        }

        private void CompleteDialog(string? result)
        {
            if (_isCompleting)
            {
                return;
            }

            _isCompleting = true;
            try
            {
                CloseDialog();
            }
            finally
            {
                _tcs?.TrySetResult(result);
            }
        }

        private void OnSearchPressed(object? sender, EventArgs e)
        {
            CompleteDialog(_searchText);
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            CompleteDialog(null);
        }
    }
}
