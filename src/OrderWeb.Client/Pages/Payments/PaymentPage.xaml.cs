using OrderWeb.Client.Services;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Payments;

public partial class PaymentPage : ContentPage
{
    private readonly decimal _totalDue;
    private readonly string? _orderId;
    private readonly long? _expectedOrderRevision;
    private readonly ClientPaymentService _paymentService = new();
    private PaymentViewModel? _sharedPayment;
    private string _selectedMethod = "Cash";
    private bool _isSubmitting;

    public PaymentPage() : this(42.80m)
    {
    }

    public PaymentPage(decimal totalDue, string? orderId = null, long? expectedOrderRevision = null)
    {
        _totalDue = totalDue;
        _orderId = orderId;
        _expectedOrderRevision = expectedOrderRevision;
        InitializeComponent();
        // The Client host supplies the operation; the payment presentation is
        // the same SharedUI surface used by the Mother host.
        _sharedPayment = new PaymentViewModel { AmountDue = _totalDue };
        _sharedPayment.StatusCheckRequested += OnSharedPaymentStatusCheckRequested;
        var sharedPayment = new PaymentView { ViewModel = _sharedPayment };
        sharedPayment.SubmissionRequested += OnSharedPaymentRequested;
        Content = sharedPayment;
        TopBar.MenuClicked += async (_, _) => await DisplayAlert("Restaurant POS", "Menu stays available from the POS shell.", "OK");
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        TotalLabel.Text = Money(_totalDue);
        TenderedEntry.Text = _totalDue.ToString("F2");
        SelectMethod("Cash");
        UpdateChange();
    }

    private async void OnSharedPaymentRequested(object? sender, PaymentSubmission submission)
    {
        if (_sharedPayment is null) return;
        if (string.IsNullOrWhiteSpace(_orderId))
        {
            _sharedPayment.ApplyAuthoritativeResult(false, "This Client order is not yet a Mother order. No payment has been taken.");
            return;
        }

        var result = await _paymentService.TakePaymentAsync(
            _orderId,
            submission.Method,
            submission.Amount,
            submission.RequestId,
            expectedOrderRevision: _expectedOrderRevision,
            correlationId: submission.CorrelationId);
        _sharedPayment.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
    }

    private async void OnSharedPaymentStatusCheckRequested(object? sender, string requestId)
    {
        if (_sharedPayment is null) return;
        var result = await _paymentService.GetPaymentStatusAsync(requestId);
        _sharedPayment.ApplyAuthoritativeResult(result.Approved, result.Message, result.IsUnknown);
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
        if (_isSubmitting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_orderId))
        {
            await DisplayAlert("Payment unavailable", "This Client order is not yet a Mother order. No payment has been taken.", "OK");
            return;
        }

        _isSubmitting = true;
        ConfirmButton.IsEnabled = false;
        PrintStatus.SetStatus("queued", "Waiting for Mother payment result...");
        try
        {
            var amount = _selectedMethod == "Cash" && decimal.TryParse(TenderedEntry.Text, out var tendered)
                ? Math.Min(tendered, _totalDue)
                : _totalDue;
            var result = await _paymentService.TakePaymentAsync(_orderId, _selectedMethod, amount);
            if (!result.Approved)
            {
                PrintStatus.SetStatus("failed", result.Message);
                await DisplayAlert("Payment not approved", result.Message, "OK");
                return;
            }

            PrintStatus.SetStatus(PrintReceiptCheckBox.IsChecked ? "queued" : "sent",
                PrintReceiptCheckBox.IsChecked ? "Payment approved. Receipt must be requested from Mother." : "Payment approved by Mother.");
            await DisplayAlert("Payment approved", result.Message, "OK");
        }
        finally
        {
            _isSubmitting = false;
            ConfirmButton.IsEnabled = true;
        }
    }

    private static string Money(decimal value) => $"£{value:F2}";
}
