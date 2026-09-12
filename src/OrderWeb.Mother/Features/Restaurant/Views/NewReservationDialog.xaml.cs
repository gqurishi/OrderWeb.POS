using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;

namespace POS_in_NET.Views;

public partial class NewReservationDialog : ContentView
{
    private TaskCompletionSource<CreatePosReservationRequest?>? _taskCompletionSource;
    private Grid? _parentGrid;

    public NewReservationDialog(DateTime defaultDate, TimeSpan? defaultTime = null)
    {
        InitializeComponent();
        Form.Prepare(defaultDate, defaultTime);
        Form.AlertAsync = Services.AppAlertService.ShowAlertAsync;
        Form.Cancelled += OnCancelled;
        Form.Submitted += OnSubmitted;
    }

    public async Task<CreatePosReservationRequest?> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<CreatePosReservationRequest?>();

        if (Application.Current?.MainPage is Shell shell)
        {
            var currentPage = shell.CurrentPage;
            if (currentPage != null)
            {
                var pageContent = FindPageContent(currentPage);
                if (pageContent is Grid mainGrid)
                {
                    _parentGrid = mainGrid;
                    Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                    mainGrid.Children.Add(this);
                }
            }
        }

        return await _taskCompletionSource.Task;
    }

    private static Element? FindPageContent(Element element)
    {
        var contentProperty = element.GetType().GetProperty("Content");
        return contentProperty?.GetValue(element) as Element;
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }

    private void OnSubmitted(object? sender, NewReservationDraft draft)
    {
        _taskCompletionSource?.TrySetResult(new CreatePosReservationRequest
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
        _parentGrid?.Children.Remove(this);
    }
}
