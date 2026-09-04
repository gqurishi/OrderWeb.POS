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
            InputEntry.IsPassword = false;
            _useVirtualKeyboard = useVirtualKeyboard;
            _virtualKeyboardOpened = false;
            InputEntry.IsReadOnly = useVirtualKeyboard;
        }

        public void SetIsPassword(bool isPassword)
        {
            InputEntry.IsPassword = isPassword;
        }

        public void SetCancelText(string text)
        {
            CancelButton.Text = text;
        }

        public void SetOkText(string text)
        {
            OkButton.Text = text;
        }

        public async Task<string?> ShowAsync()
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<string?>();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                var page = Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
                if (page != null)
                {
                    var result = await page.DisplayPromptAsync(TitleLabel.Text, MessageLabel.Text, initialValue: InputEntry.Text);
                    return result;
                }

                return null;
            }

            if (_useVirtualKeyboard)
            {
                await Task.Delay(120);
                await OpenVirtualKeyboardAsync();
            }
            else
            {
                await Task.Delay(100);
                InputEntry.Focus();
            }

            return await _taskCompletionSource.Task;
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
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
