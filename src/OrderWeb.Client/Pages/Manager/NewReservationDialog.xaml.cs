using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Manager;

public partial class NewReservationDialog : ContentView
{
    private TaskCompletionSource<MotherReservationDraft?>? _taskCompletionSource;
    private Grid? _parentGrid;

    public NewReservationDialog(DateTime defaultDate, TimeSpan? defaultTime = null)
    {
        InitializeComponent();
        Form.Prepare(defaultDate, defaultTime);
        Form.Cancelled += OnCancelled;
        Form.Submitted += OnSubmitted;
    }

    public async Task<MotherReservationDraft?> ShowAsync(Grid host)
    {
        _taskCompletionSource = new TaskCompletionSource<MotherReservationDraft?>();
        _parentGrid = host;
        Grid.SetRowSpan(this, host.RowDefinitions.Count > 0 ? host.RowDefinitions.Count : 1);
        Grid.SetColumnSpan(this, host.ColumnDefinitions.Count > 0 ? host.ColumnDefinitions.Count : 1);
        host.Children.Add(this);
        return await _taskCompletionSource.Task;
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }

    private void OnSubmitted(object? sender, NewReservationDraft draft)
    {
        _taskCompletionSource?.TrySetResult(new MotherReservationDraft
        {
            ReservationDate = draft.ReservationDate,
            ReservationTime = draft.ReservationTime,
            Covers = draft.Covers,
            CustomerName = draft.CustomerName,
            CustomerPhone = draft.CustomerPhone,
            CustomerEmail = draft.CustomerEmail,
            PromoCode = draft.PromoCode,
            TableNumber = draft.TableNumber,
            Notes = draft.Notes,
            Allergies = draft.Allergies,
            Channel = "pos"
        });
        CloseDialog();
    }

    private void CloseDialog()
    {
        InputTransparent = true;
        IsVisible = false;
        _parentGrid?.Children.Remove(this);
    }
}
