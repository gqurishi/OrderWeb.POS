using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
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

        public Task<string?> ShowAsync(Page? hostPage = null)
        {
            _tcs = new TaskCompletionSource<string?>();
            
            var page = hostPage;

            if (page == null && Application.Current?.MainPage is Page mainPage)
            {
                if (mainPage is Shell shell && shell.CurrentPage is ContentPage contentPage)
                {
                    AddToPage(contentPage);
                }
                else if (mainPage is ContentPage cp)
                {
                    AddToPage(cp);
                }
            }
            else if (page is ContentPage contentPage)
            {
                AddToPage(contentPage);
            }
            
            return _tcs.Task;
        }

        private void AddToPage(ContentPage page)
        {
            _hostPage = page;

            if (page.Content is Grid grid)
            {
                if (grid.RowDefinitions.Count > 0)
                    Grid.SetRowSpan(this, grid.RowDefinitions.Count);
                if (grid.ColumnDefinitions.Count > 0)
                    Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
                
                grid.Children.Add(this);
            }
            else if (page.Content is Layout layout)
            {
                var newGrid = new Grid();
                var existingContent = page.Content;
                page.Content = null;
                newGrid.Children.Add(existingContent);
                newGrid.Children.Add(this);
                page.Content = newGrid;
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
            }
            
            IsVisible = true;
            page.SizeChanged += OnHostPageSizeChanged;
            ApplyResponsiveLayout(page.Width, page.Height);
        }

        private void CloseDialog()
        {
            if (_hostPage != null)
            {
                _hostPage.SizeChanged -= OnHostPageSizeChanged;
                _hostPage = null;
            }

            if (Parent is Grid grid)
            {
                grid.Children.Remove(this);
            }
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
                button.HeightRequest = keyHeight;
                button.FontSize = button.Text is "CLEAR" or "ENTER" or "DEL" ? actionFont : keyFont;
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

        private void OnSearchPressed(object? sender, EventArgs e)
        {
            CloseDialog();
            _tcs?.TrySetResult(_searchText);
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            CloseDialog();
            _tcs?.TrySetResult(null);
        }
    }
}
