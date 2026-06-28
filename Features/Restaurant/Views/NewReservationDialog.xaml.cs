using POS_in_NET.Models;

namespace POS_in_NET.Views;

public partial class NewReservationDialog : ContentView
{
    private TaskCompletionSource<CreatePosReservationRequest?>? _taskCompletionSource;
    private Grid? _parentGrid;
    private string _source = "walk_in";

    public NewReservationDialog(DateTime defaultDate, TimeSpan? defaultTime = null)
    {
        InitializeComponent();
        DatePickerControl.Date = defaultDate.Date;
        TimePickerControl.Time = defaultTime ?? new TimeSpan(19, 0, 0);
        SelectSource("walk_in");
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

    private void OnWalkInClicked(object sender, EventArgs e) => SelectSource("walk_in");

    private void OnPhoneClicked(object sender, EventArgs e) => SelectSource("phone");

    private void SelectSource(string source)
    {
        _source = source;
        var selected = Color.FromArgb("#2563EB");
        var selectedText = Colors.White;
        var normal = Color.FromArgb("#E2E8F0");
        var normalText = Color.FromArgb("#334155");

        WalkInButton.BackgroundColor = source == "walk_in" ? selected : normal;
        WalkInButton.TextColor = source == "walk_in" ? selectedText : normalText;
        PhoneButton.BackgroundColor = source == "phone" ? selected : normal;
        PhoneButton.TextColor = source == "phone" ? selectedText : normalText;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await Services.AppAlertService.ShowAlertAsync("Required", "Please enter a customer name.");
            return;
        }

        if (!int.TryParse(CoversEntry.Text?.Trim(), out var covers) || covers <= 0)
        {
            await Services.AppAlertService.ShowAlertAsync("Required", "Covers must be at least 1.");
            return;
        }

        var request = new CreatePosReservationRequest
        {
            ReservationDate = DatePickerControl.Date,
            ReservationTime = TimePickerControl.Time,
            Covers = covers,
            CustomerName = NameEntry.Text.Trim(),
            CustomerPhone = PhoneEntry.Text?.Trim() ?? string.Empty,
            TableNumber = TableEntry.Text?.Trim() ?? string.Empty,
            Notes = NotesEditor.Text?.Trim() ?? string.Empty,
            Channel = _source
        };

        _taskCompletionSource?.TrySetResult(request);
        CloseDialog();
    }

    private void CloseDialog()
    {
        if (_parentGrid != null)
        {
            _parentGrid.Children.Remove(this);
        }
    }
}
