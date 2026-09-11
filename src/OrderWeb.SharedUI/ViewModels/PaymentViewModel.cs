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
    private bool _allowSplit = true;
    private bool _showLoyaltyMethod = true;
    private bool _showInlineSplitToggle;
    private bool _showInlineSplitMethod;
    private PaymentPresentationState _state;
    private string _message = "Choose a payment method. Mother confirms every payment.";
    private string? _lastRequestId;

    public PaymentViewModel() => SubmitCommand = new Command(Submit);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PaymentSubmission>? SubmissionRequested;
    public event EventHandler<string>? StatusCheckRequested;
    public ICommand SubmitCommand { get; }

    /// <summary>Client shows Loyalty; Mother Order Place may hide it (points via order menu).</summary>
    public bool ShowLoyaltyMethod
    {
        get => _showLoyaltyMethod;
        set
        {
            if (!Set(ref _showLoyaltyMethod, value))
            {
                return;
            }

            if (!_showLoyaltyMethod && SelectedMethod == "loyalty")
            {
                SelectedMethod = "cash";
            }

            OnPropertyChanged(nameof(ShowLoyaltyMethod));
        }
    }

    /// <summary>Prefer PaymentWizard for split; leave false on tender after setup plan.</summary>
    public bool ShowInlineSplitToggle
    {
        get => _showInlineSplitToggle;
        set => Set(ref _showInlineSplitToggle, value);
    }

    /// <summary>Legacy Split method tile on tender screen (default off).</summary>
    public bool ShowInlineSplitMethod
    {
        get => _showInlineSplitMethod;
        set => Set(ref _showInlineSplitMethod, value);
    }

    public decimal AmountDue
    {
        get => _amountDue;
        set
        {
            if (Set(ref _amountDue, value))
            {
                if (Tendered <= 0)
                {
                    Tendered = value;
                }

                RaiseMoney();
            }
        }
    }

    public decimal Tendered
    {
        get => _tendered;
        set
        {
            if (Set(ref _tendered, Math.Max(0, value)))
            {
                RaiseMoney();
            }
        }
    }

    public decimal ChangeDue => SelectedMethod == "cash" ? Math.Max(0, Tendered - AmountDue) : 0;
    public decimal Remaining => Math.Max(0, AmountDue - (EffectiveSplit && SelectedMethod == "cash" ? Tendered : 0));

    public string SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (Set(ref _selectedMethod, value))
            {
                RaiseMoney();
            }
        }
    }

    public bool PrintReceipt { get => _printReceipt; set => Set(ref _printReceipt, value); }

    /// <summary>
    /// Table orders may split/partial-pay. Collection/Delivery must pay the full bill (Mother parity).
    /// </summary>
    public bool AllowSplit
    {
        get => _allowSplit;
        set
        {
            if (!Set(ref _allowSplit, value))
            {
                return;
            }

            if (!_allowSplit)
            {
                IsSplit = false;
            }

            OnPropertyChanged(nameof(EffectiveSplit));
            RaiseMoney();
        }
    }

    public bool IsSplit
    {
        get => _isSplit;
        set
        {
            var next = AllowSplit && value;
            if (Set(ref _isSplit, next))
            {
                OnPropertyChanged(nameof(EffectiveSplit));
                RaiseMoney();
            }
        }
    }

    public bool EffectiveSplit => AllowSplit && IsSplit;

    public PaymentPresentationState State { get => _state; set => Set(ref _state, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public string? LastRequestId => _lastRequestId;

    public void ApplyAuthoritativeResult(bool approved, string message, bool unknown = false)
    {
        State = unknown
            ? PaymentPresentationState.Unknown
            : approved
                ? PaymentPresentationState.Approved
                : PaymentPresentationState.Failed;
        Message = message;
    }

    public void SetTenderedExact() => Tendered = AmountDue;

    public void SetTenderedQuick(decimal amount) => Tendered = Math.Max(0m, amount);

    private void Submit()
    {
        if (State == PaymentPresentationState.Unknown && !string.IsNullOrWhiteSpace(_lastRequestId))
        {
            State = PaymentPresentationState.Submitting;
            Message = "Checking Mother POS for the authoritative payment result…";
            StatusCheckRequested?.Invoke(this, _lastRequestId);
            return;
        }

        if (State is PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard)
        {
            return;
        }

        // COL/DEL (AllowSplit=false): always charge the full amount due.
        var amount = EffectiveSplit ? Math.Min(Tendered, AmountDue) : AmountDue;
        if (amount <= 0)
        {
            ApplyAuthoritativeResult(false, "Enter a valid payment amount.");
            return;
        }

        if (!AllowSplit && SelectedMethod == "cash" && Tendered + 0.009m < AmountDue)
        {
            ApplyAuthoritativeResult(false, "Collection and Delivery require payment of the full bill.");
            return;
        }

        State = SelectedMethod == "card"
            ? PaymentPresentationState.WaitingForCard
            : PaymentPresentationState.Submitting;
        Message = SelectedMethod switch
        {
            "card" => "Waiting for the card terminal and Mother confirmation…",
            "gift_card" => "Waiting for gift card details, then Mother / OrderWeb confirmation…",
            "loyalty" => "Waiting for loyalty points details, then Mother / OrderWeb confirmation…",
            _ => "Waiting for Mother confirmation…"
        };
        var requestId = Guid.NewGuid().ToString("N");
        _lastRequestId = requestId;
        SubmissionRequested?.Invoke(
            this,
            new PaymentSubmission(
                requestId,
                SelectedMethod,
                amount,
                Tendered,
                PrintReceipt,
                EffectiveSplit,
                Guid.NewGuid().ToString("N")));
    }

    private void RaiseMoney()
    {
        OnPropertyChanged(nameof(ChangeDue));
        OnPropertyChanged(nameof(Remaining));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
