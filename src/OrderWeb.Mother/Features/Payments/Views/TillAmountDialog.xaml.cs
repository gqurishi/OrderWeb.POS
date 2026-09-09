using POS_in_NET.Services;
using System.Globalization;

namespace POS_in_NET.Views;

public partial class TillAmountDialog : ContentView
{
    private TaskCompletionSource<decimal?>? _taskCompletionSource;
    private Page? _hostPage;
    private bool _keyboardOpen;

    public TillAmountDialog(string title, string subtitle, string fieldLabel, string confirmText)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        SubtitleLabel.Text = subtitle;
        FieldLabel.Text = fieldLabel;
        ConfirmButton.Text = confirmText;
    }

    public Task<decimal?> ShowAsync(Page hostPage)
    {
        _taskCompletionSource = new TaskCompletionSource<decimal?>();
        _hostPage = hostPage;

        if (hostPage is ContentPage contentPage)
        {
            AddToPage(contentPage);
        }
        else
        {
            _taskCompletionSource.TrySetResult(null);
        }

        return _taskCompletionSource.Task;
    }

    private void AddToPage(ContentPage page)
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 2000;

        if (page.Content is Grid grid)
        {
            if (grid.RowDefinitions.Count > 0)
            {
                Grid.SetRowSpan(this, grid.RowDefinitions.Count);
            }

            if (grid.ColumnDefinitions.Count > 0)
            {
                Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
            }

            grid.Children.Add(this);
            return;
        }

        var wrapper = new Grid();
        var existing = page.Content;
        page.Content = null;
        if (existing != null)
        {
            wrapper.Children.Add(existing);
        }

        wrapper.Children.Add(this);
        page.Content = wrapper;
    }

    private void Close(decimal? result)
    {
        if (Parent is Grid grid)
        {
            grid.Children.Remove(this);
        }

        _taskCompletionSource?.TrySetResult(result);
    }

    private void OnCancelClicked(object sender, EventArgs e) => Close(null);

    private async void OnAmountTapped(object sender, EventArgs e)
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(FieldLabel.Text, "Done");
            keyboard.SetInitialText(AmountEntry.Text ?? string.Empty);
            var value = await keyboard.ShowAsync(_hostPage);
            if (value != null)
            {
                AmountEntry.Text = value;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            && !decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", "Enter a valid amount.");
            return;
        }

        if (amount < 0)
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", "Amount cannot be negative.");
            return;
        }

        Close(amount);
    }
}
