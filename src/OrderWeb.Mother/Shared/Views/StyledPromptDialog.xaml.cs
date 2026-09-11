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
            _useVirtualKeyboard = useVirtualKeyboard || InputEntry.Keyboard == Keyboard.Numeric;
            _virtualKeyboardOpened = false;
            InputEntry.IsReadOnly = _useVirtualKeyboard;
            InputEntry.InputTransparent = _useVirtualKeyboard;
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
                var keyboard = CreateSharedKeyboard();
                return await keyboard.ShowAsync(Shell.Current?.CurrentPage);
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
            var keyboard = CreateSharedKeyboard();
            var result = _parentGrid is not null
                ? await keyboard.ShowOverAsync(_parentGrid)
                : await keyboard.ShowAsync();
            if (result != null)
            {
                InputEntry.Text = result;
            }

            _virtualKeyboardOpened = false;
        }

        private OrderWeb.SharedUI.Controls.VirtualKeyboardDialog CreateSharedKeyboard()
        {
            var keyboard = new OrderWeb.SharedUI.Controls.VirtualKeyboardDialog();
            keyboard.SetPrompt(TitleLabel.Text, OkButton.Text);
            keyboard.SetPlaceholder(InputEntry.Placeholder);
            if (InputEntry.Keyboard == Keyboard.Numeric)
            {
                var descriptor = $"{TitleLabel.Text} {MessageLabel.Text} {InputEntry.Placeholder}";
                keyboard.SetNumericMode(
                    descriptor.Contains("PIN", StringComparison.OrdinalIgnoreCase)
                        ? OrderWeb.SharedUI.Controls.VirtualKeyboardNumericMode.LongDigits
                        : OrderWeb.SharedUI.Controls.VirtualKeyboardNumericMode.WholeNumber);
                keyboard.SetRequired(true);
            }
            else
            {
                keyboard.SetTextMode(InputEntry.Keyboard == Keyboard.Email
                    ? OrderWeb.SharedUI.Controls.VirtualKeyboardTextMode.Email
                    : InputEntry.Keyboard == Keyboard.Telephone
                        ? OrderWeb.SharedUI.Controls.VirtualKeyboardTextMode.Phone
                        : OrderWeb.SharedUI.Controls.VirtualKeyboardTextMode.Name);
                keyboard.SetRequired(true);
            }
            keyboard.SetMaximumLength(InputEntry.MaxLength == int.MaxValue ? 0 : InputEntry.MaxLength);
            keyboard.SetInitialText(InputEntry.Text ?? string.Empty);
            return keyboard;
        }

        private void CloseDialog()
        {
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
