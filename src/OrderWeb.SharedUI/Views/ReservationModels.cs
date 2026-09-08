using System.Globalization;

namespace OrderWeb.SharedUI.Views;

/// <summary>Calendar cell used by the shared reservation board.</summary>
public sealed class ReservationCalendarDay
{
    public ReservationCalendarDay(DateTime date, bool isCurrentMonth, bool isSelected, int reservationCount)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsSelected = isSelected;
        ReservationCount = reservationCount;
    }

    public DateTime Date { get; }
    public bool IsCurrentMonth { get; }
    public bool IsSelected { get; }
    public int ReservationCount { get; }
    public bool HasBookings => ReservationCount > 0;

    public string DayText => Date.Day.ToString(CultureInfo.InvariantCulture);

    public Color BackgroundColor => IsSelected
        ? Color.FromArgb("#2563EB")
        : HasBookings
            ? Color.FromArgb("#EFF6FF")
            : Colors.Transparent;

    public Color BorderColor => IsSelected
        ? Color.FromArgb("#2563EB")
        : HasBookings
            ? Color.FromArgb("#BFDBFE")
            : Color.FromArgb("#E2E8F0");

    public Color TextColor => IsSelected
        ? Colors.White
        : HasBookings
            ? Color.FromArgb("#1E3A8A")
            : IsCurrentMonth ? Color.FromArgb("#0F172A") : Color.FromArgb("#CBD5E1");

    public Color DotColor => IsSelected
        ? Colors.White
        : Color.FromArgb("#2563EB");
}

/// <summary>Host-neutral reservation row for Mother and Client list cards.</summary>
public sealed class ReservationRow
{
    public ReservationRow(
        string cloudId,
        string? localId,
        DateTime date,
        TimeOnly time,
        string name,
        string phone,
        int guests,
        string table,
        string status,
        string note,
        string reference,
        string source,
        string email,
        string promoCode,
        string allergies,
        string specialRequests)
    {
        CloudId = cloudId;
        LocalId = localId;
        Date = date.Date;
        Time = time;
        Name = name;
        Phone = phone;
        Guests = guests;
        Table = table;
        Status = string.IsNullOrWhiteSpace(status) ? "Booked" : status;
        Note = note;
        Reference = reference;
        Source = source;
        Email = email;
        PromoCode = promoCode;
        Allergies = allergies;
        SpecialRequests = specialRequests;
    }

    public string CloudId { get; }
    public string? LocalId { get; }
    public DateTime Date { get; }
    public TimeOnly Time { get; }
    public string TimeText => Time.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Name { get; }
    public string Phone { get; }
    public int Guests { get; }
    public string Table { get; }
    public string Status { get; }
    public string Note { get; }
    public string Reference { get; }
    public string Source { get; }
    public string Email { get; }
    public string PromoCode { get; }
    public string Allergies { get; }
    public string SpecialRequests { get; }

    public string DateTimeText => $"{Date:dddd, dd MMMM yyyy} at {TimeText}";
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "No phone saved" : Phone;
    public string EmailDisplay => string.IsNullOrWhiteSpace(Email) ? "No email saved" : Email;
    public string GuestsText => Guests == 1 ? "1 guest" : $"{Guests} guests";
    public string TableDisplay => Table == "-" ? "Table not assigned" : $"Table {Table}";
    public string StatusDisplay => $"Status: {Status}";
    public string ReferenceDisplay => string.IsNullOrWhiteSpace(Reference) ? "No reference" : Reference;
    public string PromoCodeDisplay => string.IsNullOrWhiteSpace(PromoCode) ? "No promocode" : PromoCode;
    public string AllergiesDisplay => string.IsNullOrWhiteSpace(Allergies) ? "No allergies or dietary notes" : Allergies;
    public string SpecialRequestsDisplay => HasSpecialRequests ? SpecialRequests.Trim() : "No special requests";
    public string SourceDisplay => Source.Trim().ToLowerInvariant() switch
    {
        "online" => "Online",
        "walk_in" or "walkin" or "walk-in" => "Walk-in",
        "phone" => "Phone",
        "pos" => "POS",
        _ => string.IsNullOrWhiteSpace(Source) ? "Booking" : Source
    };

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public bool HasPromoCode => !string.IsNullOrWhiteSpace(PromoCode);
    public bool HasAllergies => !string.IsNullOrWhiteSpace(Allergies);
    public bool HasSpecialRequests => !string.IsNullOrWhiteSpace(SpecialRequests)
                                      && !IsUploadDiagnostic(SpecialRequests);
    public bool HasAnySpecialInfo => HasAllergies || HasSpecialRequests;
    public string PromoBadgeText => HasPromoCode ? $"Promo {PromoCode}" : string.Empty;

    public Color StatusBadgeBackground => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#ECFDF5"),
        "no_show" or "noshow" => Color.FromArgb("#FFF1F2"),
        "cancelled" or "canceled" => Color.FromArgb("#F8FAFC"),
        _ => Color.FromArgb("#EFF6FF")
    };
    public Color StatusBadgeBorder => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#A7F3D0"),
        "no_show" or "noshow" => Color.FromArgb("#FECDD3"),
        "cancelled" or "canceled" => Color.FromArgb("#E2E8F0"),
        _ => Color.FromArgb("#BFDBFE")
    };
    public Color StatusBadgeTextColor => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#047857"),
        "no_show" or "noshow" => Color.FromArgb("#BE123C"),
        "cancelled" or "canceled" => Color.FromArgb("#475569"),
        _ => Color.FromArgb("#1D4ED8")
    };

    public bool IsArrived => NormalizedStatus is "arrived" or "show" or "shown" or "seated";
    public bool IsNoShow => string.Equals(Status, "No Show", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(Status, "No-show", StringComparison.OrdinalIgnoreCase)
                            || NormalizedStatus is "no_show" or "noshow";
    public bool IsCancelled => string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Status, "Canceled", StringComparison.OrdinalIgnoreCase)
                               || NormalizedStatus is "cancelled" or "canceled";
    public bool IsFinalAttendance => IsArrived || IsNoShow || IsCancelled;
    public bool IsShowButtonVisible => !IsNoShow && !IsCancelled;
    public bool IsNoShowButtonVisible => !IsArrived && !IsCancelled;
    public bool IsCancelButtonVisible => !IsArrived && !IsNoShow;
    public bool IsShowButtonEnabled => !IsFinalAttendance;
    public bool IsNoShowButtonEnabled => !IsFinalAttendance;
    public bool IsCancelButtonEnabled => !IsFinalAttendance;
    public string ShowButtonText => IsArrived ? "Shown" : "Show";
    public string NoShowButtonText => "No Show";
    public string CancelButtonText => IsCancelled ? "Cancelled" : "Cancel";
    public Color ShowButtonBackground => IsArrived ? Color.FromArgb("#065F46") : Color.FromArgb("#ECFDF5");
    public Color ShowButtonBorderColor => IsArrived ? Color.FromArgb("#065F46") : Color.FromArgb("#A7F3D0");
    public Color ShowButtonTextColor => IsArrived ? Colors.White : Color.FromArgb("#047857");
    public Color NoShowButtonBackground => IsNoShow ? Color.FromArgb("#9F1239") : Color.FromArgb("#FFF1F2");
    public Color NoShowButtonBorderColor => IsNoShow ? Color.FromArgb("#FECDD3") : Color.FromArgb("#FECDD3");
    public Color NoShowButtonTextColor => IsNoShow ? Colors.White : Color.FromArgb("#BE123C");
    public Color CancelButtonBackground => IsCancelled ? Color.FromArgb("#334155") : Color.FromArgb("#F8FAFC");
    public Color CancelButtonBorderColor => IsCancelled ? Color.FromArgb("#334155") : Color.FromArgb("#E2E8F0");
    public Color CancelButtonTextColor => IsCancelled ? Colors.White : Color.FromArgb("#475569");

    private string NormalizedStatus => Status.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();

    private static bool IsUploadDiagnostic(string value)
    {
        var text = value.Trim();
        return text.StartsWith("Input string was not in a correct format", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Cloud upload failed", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Invalid cloud response", StringComparison.OrdinalIgnoreCase);
    }
}
