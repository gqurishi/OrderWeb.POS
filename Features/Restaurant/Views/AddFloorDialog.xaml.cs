namespace POS_in_NET.Views;

public partial class AddFloorDialog : ContentView
{
    private TaskCompletionSource<(bool success, string? floorName)>? _taskCompletionSource;

    public AddFloorDialog()
    {
        InitializeComponent();
    }

    public Task<(bool success, string? floorName)> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<(bool, string?)>();
        IsVisible = true;
        
        // Auto-focus on floor name entry
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
        {
            FloorNameEntry.Focus();
        });
        
        return _taskCompletionSource.Task;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        IsVisible = false;
        _taskCompletionSource?.SetResult((false, null));
    }

    private void OnCreateClicked(object sender, EventArgs e)
    {
        var floorName = FloorNameEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(floorName))
        {
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Required", "Please enter a floor name");
            return;
        }

        IsVisible = false;
        _taskCompletionSource?.SetResult((true, floorName));
    }

    private async void DisplayAlert(string title, string message, string cancel)
    {
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, cancel);
        }
    }
}
