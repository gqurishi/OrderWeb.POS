namespace OrderWeb.Client.Pages.Payments;

public partial class PaymentPage : ContentPage
{
    private readonly decimal _totalDue;
    private string _selectedMethod = "Cash";

    public PaymentPage() : this(42.80m)
    {
    }

    public PaymentPage(decimal totalDue)
    {
        _totalDue = totalDue;
        InitializeComponent();
        TopBar.MenuClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Menu stays available from the POS shell.", "OK");
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        TotalLabel.Text = Money(_totalDue);
        TenderedEntry.Text = _totalDue.ToString("F2");
        SelectMethod("Cash");
        UpdateChange();
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

        TenderedEntry.Text = method == "Cash" ? _totalDue.ToString("F2") : _totalDue.ToString("F2");
        UpdateChange();
    }

    private void OnTenderedChanged(object sender, TextChangedEventArgs e)
    {
        UpdateChange();
    }

    private void UpdateChange()
    {
        var tendered = decimal.TryParse(TenderedEntry.Text, out var parsed) ? parsed : 0m;
        var change = Math.Max(0m, tendered - _totalDue);
        ChangeLabel.Text = Money(change);
    }

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        PrintStatus.SetStatus(PrintReceiptCheckBox.IsChecked ? "queued" : "sent", PrintReceiptCheckBox.IsChecked ? "Queued by Mother" : "Sent to kitchen");
        await Task.Delay(250);
        PrintStatus.SetStatus(PrintReceiptCheckBox.IsChecked ? "printed" : "sent", PrintReceiptCheckBox.IsChecked ? "Printed" : "Sent to kitchen");
        await DisplayAlert("Payment", $"{_selectedMethod} payment queued by Mother.", "OK");
    }

    private static string Money(decimal value) => $"£{value:F2}";
}
