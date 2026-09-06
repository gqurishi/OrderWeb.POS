using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OrderWeb.SharedUI.ViewModels;

public enum PaymentPresentationState { Ready, WaitingForCard, Submitting, Approved, Failed, Unknown }
public sealed record PaymentMethodModel(string Id, string Name, string Icon, bool IsAvailable = true);
public sealed record PaymentSubmission(string RequestId, string Method, decimal Amount, decimal Tendered, bool PrintReceipt, bool IsSplit, string CorrelationId);

/// <summary>Presentation state only; authoritative results are supplied by a host payment service.</summary>
public sealed class PaymentViewModel : INotifyPropertyChanged
{
    private decimal _amountDue;
    private decimal _tendered;
    private string _selectedMethod = "cash";
    private bool _printReceipt = true;
    private bool _isSplit;
    private PaymentPresentationState _state;
    private string _message = "Choose a payment method. Mother confirms every payment.";
    private string? _lastRequestId;
    public PaymentViewModel() => SubmitCommand = new Command(Submit);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PaymentSubmission>? SubmissionRequested;
    public event EventHandler<string>? StatusCheckRequested;
    public ICommand SubmitCommand { get; }
    public decimal AmountDue { get => _amountDue; set { if (Set(ref _amountDue, value)) { if (Tendered <= 0) Tendered = value; RaiseMoney(); } } }
    public decimal Tendered { get => _tendered; set { if (Set(ref _tendered, Math.Max(0, value))) RaiseMoney(); } }
    public decimal ChangeDue => SelectedMethod == "cash" ? Math.Max(0, Tendered - AmountDue) : 0;
    public decimal Remaining => Math.Max(0, AmountDue - (IsSplit && SelectedMethod == "cash" ? Tendered : 0));
    public string SelectedMethod { get => _selectedMethod; set { if (Set(ref _selectedMethod, value)) RaiseMoney(); } }
    public bool PrintReceipt { get => _printReceipt; set => Set(ref _printReceipt, value); }
    public bool IsSplit { get => _isSplit; set { if (Set(ref _isSplit, value)) RaiseMoney(); } }
    public PaymentPresentationState State { get => _state; set => Set(ref _state, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public string? LastRequestId => _lastRequestId;
    public void ApplyAuthoritativeResult(bool approved, string message, bool unknown = false)
    {
        State = unknown ? PaymentPresentationState.Unknown : approved ? PaymentPresentationState.Approved : PaymentPresentationState.Failed;
        Message = message;
    }
    private void Submit()
    {
        if (State == PaymentPresentationState.Unknown && !string.IsNullOrWhiteSpace(_lastRequestId))
        {
            State = PaymentPresentationState.Submitting;
            Message = "Checking Mother POS for the authoritative payment result…";
            StatusCheckRequested?.Invoke(this, _lastRequestId);
            return;
        }
        if (State is PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard) return;
        var amount = IsSplit ? Math.Min(Tendered, AmountDue) : AmountDue;
        if (amount <= 0) { ApplyAuthoritativeResult(false, "Enter a valid payment amount."); return; }
        State = SelectedMethod == "card" ? PaymentPresentationState.WaitingForCard : PaymentPresentationState.Submitting;
        Message = SelectedMethod == "card" ? "Waiting for the card terminal and Mother confirmation…" : "Waiting for Mother confirmation…";
        var requestId = Guid.NewGuid().ToString("N");
        _lastRequestId = requestId;
        SubmissionRequested?.Invoke(this, new PaymentSubmission(requestId, SelectedMethod, amount, Tendered, PrintReceipt, IsSplit, Guid.NewGuid().ToString("N")));
    }
    private void RaiseMoney() { OnPropertyChanged(nameof(ChangeDue)); OnPropertyChanged(nameof(Remaining)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
