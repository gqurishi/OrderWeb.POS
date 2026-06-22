using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class StyledPromptDialog : ContentView
    {
        private TaskCompletionSource<string?>? _taskCompletionSource;
        private Grid? _parentGrid;
        private bool _useVirtualKeyboard;
        private bool _virtualKeyboardOpened;

        public StyledPromptDialog()
        {
            InitializeComponent();
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += async (s, e) => await OpenVirtualKeyboardAsync();
            InputBorder.GestureRecognizers.Add(tapGesture);
        }

        public void SetDialog(string title, string message, string placeholder, Keyboard? keyboard = null, string initialValue = "", bool useVirtualKeyboard = false)
        {
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            InputEntry.Placeholder = placeholder;
            InputEntry.Keyboard = keyboard ?? Keyboard.Default;
            InputEntry.Text = initialValue;
            _useVirtualKeyboard = useVirtualKeyboard;
            _virtualKeyboardOpened = false;
            InputEntry.IsReadOnly = useVirtualKeyboard;
        }

        public async Task<string?> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Pages.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<string?>();
            
            // Find the GiftCardPage's content grid
            if (Application.Current?.MainPage is Shell shell)
            {
                var currentPage = shell.CurrentPage;
                
                // Try to find the content grid in the current page
                if (currentPage != null)
                {
                    var pageContent = FindPageContent(currentPage);
                    if (pageContent is Grid mainGrid)
                    {
                        _parentGrid = mainGrid;
                        
                        // Make sure dialog fills entire grid to center properly
                        Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                        Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                        
                        // Add dialog overlay to the grid
                        mainGrid.Children.Add(this);
                        
                        if (_useVirtualKeyboard)
                        {
                            await Task.Delay(120);
                            await OpenVirtualKeyboardAsync();
                        }
                        else
                        {
                            // Focus on the entry field
                            await Task.Delay(100);
                            InputEntry.Focus();
                        }
                    }
                }
            }

            return await _taskCompletionSource.Task;
        }

        private Element? FindPageContent(Element element)
        {
            // For ContentPage, find the Content property
            var contentProperty = element.GetType().GetProperty("Content");
            if (contentProperty != null)
            {
                return contentProperty.GetValue(element) as Element;
            }
            return null;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(InputEntry.Text);
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(null);
            CloseDialog();
        }

        private async Task OpenVirtualKeyboardAsync()
        {
            if (!_useVirtualKeyboard || _virtualKeyboardOpened)
            {
                return;
            }

            _virtualKeyboardOpened = true;
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetInitialText(InputEntry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync();
            if (result != null)
            {
                InputEntry.Text = result;
            }

            _virtualKeyboardOpened = false;
        }

        private void CloseDialog()
        {
            // Remove from parent
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }
    }
}
