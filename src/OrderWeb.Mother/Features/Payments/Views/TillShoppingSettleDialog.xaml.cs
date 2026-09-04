using POS_in_NET.Models;
using POS_in_NET.Services;
using System.Globalization;

namespace POS_in_NET.Views;

public partial class TillShoppingSettleDialog : ContentView
{
    private readonly TillExpense _trip;
    private TaskCompletionSource<ShoppingSettleRequest?>? _taskCompletionSource;

    public TillShoppingSettleDialog(TillExpense trip)
    {
        InitializeComponent();
        _trip = trip;
        TripSummaryLabel.Text = $"{trip.Description} · £{trip.AmountTaken:F2} taken · by {trip.RecordedByName}";
        UpdateChangeLabel();
    }

    public Task<ShoppingSettleRequest?> ShowAsync(Page hostPage)
    {
        _taskCompletionSource = new TaskCompletionSource<ShoppingSettleRequest?>();

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

    private void OnSpentChanged(object? sender, TextChangedEventArgs e) => UpdateChangeLabel();

    private void UpdateChangeLabel()
    {
        if (!TryParseAmount(SpentEntry.Text, out var spent))
        {
            ChangeLabel.Text = "Change to return: —";
            return;
        }

        var change = Math.Max(0, _trip.AmountTaken - spent);
        ChangeLabel.Text = $"Change to return: £{change:F2}";
    }

    private void Close(ShoppingSettleRequest? result)
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
        if (!TryParseAmount(SpentEntry.Text, out var spent))
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", "Enter how much was actually spent.");
            return;
        }

        if (spent < 0 || spent > _trip.AmountTaken)
        {
            await AppAlertService.ShowAlertAsync("Invalid amount", $"Spent must be between £0 and £{_trip.AmountTaken:F2}.");
            return;
        }

        Close(new ShoppingSettleRequest
        {
            TillExpenseId = _trip.Id,
            AmountSpent = spent
        });
    }

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
    }
}
