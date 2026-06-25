using POS_in_NET.Models;
using POS_in_NET.Services;
using System.Globalization;

namespace POS_in_NET.Views;

public partial class TillShoppingTakeDialog : ContentView
{
    private TaskCompletionSource<ShoppingTakeRequest?>? _taskCompletionSource;
    private readonly string _sourceArea;
    private readonly string? _orderId;
    private readonly string? _orderNumber;

    public TillShoppingTakeDialog(string sourceArea, string? orderId = null, string? orderNumber = null)
    {
        InitializeComponent();
        _sourceArea = sourceArea;
        _orderId = orderId;
        _orderNumber = orderNumber;
    }

    public Task<ShoppingTakeRequest?> ShowAsync(Page hostPage)
    {
        _taskCompletionSource = new TaskCompletionSource<ShoppingTakeRequest?>();

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

    private void Close(ShoppingTakeRequest? result)
    {
        if (Parent is Grid grid)
        {
            grid.Children.Remove(this);
        }

        _taskCompletionSource?.TrySetResult(result);
    }

    private void OnCancelClicked(object sender, EventArgs e) => Close(null);

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        var item = ItemEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(item))
        {
            await AppAlertService.ShowAlertAsync("Required", "Please enter what you are buying (e.g. Milk).");
            return;
        }

        if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            && !decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", "Enter a valid amount taken from the till.");
            return;
        }

        if (amount <= 0)
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", "Amount must be greater than zero.");
            return;
        }

        Close(new ShoppingTakeRequest
        {
            ItemName = item,
            AmountTaken = amount,
            SourceArea = _sourceArea,
            OrderId = _orderId,
            OrderNumber = _orderNumber
        });
    }
}
