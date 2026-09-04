using System.ComponentModel;
using System.Runtime.CompilerServices;
using POS_in_NET.Services;

namespace POS_in_NET.Models;

public sealed class RiderOperation : INotifyPropertyChanged
{
    private bool _isBusy;

    public int OrderDbId { get; init; }
    public long? RiderJobId { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string CloudOrderId { get; init; } = string.Empty;
    public string SourceChannel { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string CustomerAddress { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public decimal? AmountPaid { get; init; }
    public decimal CashCollectionAmount { get; init; }
    public string OrderStatus { get; init; } = string.Empty;
    public string LifecycleState { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? ScheduledTime { get; init; }
    public DateTime? FirstSentAt { get; init; }
    public string OperationStatus { get; init; } = RiderOperationPolicy.AwaitingKitchen;
    public string QuoteId { get; init; } = string.Empty;
    public decimal? QuoteAmount { get; init; }
    public string QuoteCurrency { get; init; } = "GBP";
    public DateTime? QuoteExpiresAt { get; init; }
    public DateTime? EstimatedPickupAt { get; init; }
    public DateTime? EstimatedDeliveryAt { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string DispatchId { get; init; } = string.Empty;
    public string RiderName { get; init; } = string.Empty;
    public string RiderPhone { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;

    public string DisplayOrderNumber => string.IsNullOrWhiteSpace(OrderNumber) ? $"#{OrderId[..Math.Min(8, OrderId.Length)]}" : (OrderNumber.StartsWith('#') ? OrderNumber : $"#{OrderNumber}");
    public string SourceDisplay => SourceChannel.Equals("web", StringComparison.OrdinalIgnoreCase) ? "Web" : "Phone / Local";
    public string CustomerDisplay => string.IsNullOrWhiteSpace(CustomerName) ? CustomerPhone : CustomerName;
    public string TimeDisplay => ScheduledTime.HasValue ? $"Scheduled {ScheduledTime:h:mm tt}" : $"Received {CreatedAt:h:mm tt}";
    public string TotalDisplay => $"£{TotalAmount:0.00}";
    public string CashCollectionDisplay => CashCollectionAmount > 0 ? $"Collect £{CashCollectionAmount:0.00}" : "Collect £0.00";
    public string PaymentDisplay => $"{Services.OnlineOrderPaymentHelper.GetDisplayMethod(PaymentMethod)} · {Services.OnlineOrderPaymentHelper.GetStatusDisplay(PaymentMethod, PaymentStatus)}";
    public string OperationStatusDisplay => RiderOperationPolicy.GetDisplayStatus(OperationStatus, OrderStatus, FirstSentAt);
    public string StatusColor => RiderOperationPolicy.GetStatusColor(OperationStatus, OrderStatus, FirstSentAt);
    public string QuoteDisplay => QuoteAmount.HasValue ? $"{QuoteCurrency} {QuoteAmount:0.00}" : "No quote";
    public bool HasQuote => RiderOperationPolicy.CanConfirmQuote(OperationStatus, QuoteId, QuoteExpiresAt, DateTime.Now);
    public bool CanRequestQuote =>
        (RiderOperationPolicy.CanRequestQuote(OperationStatus) ||
         (OperationStatus == RiderOperationPolicy.QuoteAvailable && QuoteExpiresAt.HasValue && QuoteExpiresAt.Value <= DateTime.Now))
        && !IsBusy;
    public bool CanConfirmQuote => HasQuote && !IsBusy;
    public bool HasRider => !string.IsNullOrWhiteSpace(RiderName);
    public bool HasProblem => RiderOperationPolicy.IsProblem(OperationStatus) || !string.IsNullOrWhiteSpace(LastError);
    public string PrimaryActionText => HasQuote ? "Confirm Rider" : "Request Rider";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRequestQuote));
            OnPropertyChanged(nameof(CanConfirmQuote));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record RiderQuoteResult(bool Success, string Message, RiderOperation? Operation = null);
public sealed record RiderDispatchResult(bool Success, string Message, RiderOperation? Operation = null);
