using OrderWeb.Client.Models;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Payments;

public partial class PaymentPage : ContentPage
{
    private readonly decimal _totalDue;
    private readonly string? _orderId;
    private readonly ClientCacheService _cache = new();
    private readonly MotherPrintClient _printClient;
    private string _selectedMethod = "Cash";

    public PaymentPage() : this(42.80m)
    {
    }

    public PaymentPage(decimal totalDue, string? orderId = null)
    {
        _totalDue = totalDue;
        _orderId = orderId;
        _printClient = new MotherPrintClient(_cache);
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Menu stays available from the POS shell.", "OK");
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        TotalLabel.Text = Money(_totalDue);
        TenderedEntry.Text = _totalDue.ToString("F2");
        SelectMethod("Cash");
        UpdateChange();
        PrintStatus.ShowIdle("Waiting for Mother print response.");
    }

    private void OnMethodClicked(object sender, EventArgs e)
    {
        if (sender is Button button && !string.IsNullOrWhiteSpace(button.Text))
        {
            SelectMethod(button.Text);
        }
    }

    private void SelectMethod(string method)
    {
        _selectedMethod = method;
        MethodLabel.Text = $"Method: {method}";
        foreach (var button in new[] { CashButton, CardButton, GiftCardButton, SplitButton })
        {
            button.BackgroundColor = button.Text == method ? Color.FromArgb("#6366F1") : Color.FromArgb("#F1F5F9");
            button.TextColor = button.Text == method ? Colors.White : Color.FromArgb("#334155");
            button.FontFamily = "OpenSansSemibold";
            button.FontSize = 17;
            button.CornerRadius = 8;
            button.HeightRequest = 60;
        }

        TenderedEntry.Text = _totalDue.ToString("F2");
        UpdateChange();
    }

    private void OnTenderedChanged(object sender, TextChangedEventArgs e) => UpdateChange();

    private void UpdateChange()
    {
        var tendered = decimal.TryParse(TenderedEntry.Text, out var parsed) ? parsed : 0m;
        var change = Math.Max(0m, tendered - _totalDue);
        ChangeLabel.Text = Money(change);
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        if (PrintReceiptCheckBox.IsChecked)
        {
            if (string.IsNullOrWhiteSpace(_orderId))
            {
                PrintStatus.SetStatus("failed", "Order id required for Mother receipt print.");
                await DisplayAlert("Payment", $"{_selectedMethod} payment noted, but receipt print needs an order id.", "OK");
                return;
            }

            var session = await _cache.GetCurrentLoginSessionAsync();
            // Mother is authoritative — show only Mother's print response.
            var request = await _printClient.RequestPrintAsync("bill", _orderId, session);
            await _cache.SavePrintRequestAsync(request);
            PrintStatus.Bind(MotherPrintClient.ToResultDto(request));
            await DisplayAlert("Payment", $"{_selectedMethod} payment queued by Mother.\nPrint: {request.Status}", "OK");
            return;
        }

        PrintStatus.ShowIdle("Payment confirmed without receipt print.");
        await DisplayAlert("Payment", $"{_selectedMethod} payment queued by Mother.", "OK");
    }

    private static string Money(decimal value) => $"£{value:F2}";
}
