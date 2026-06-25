using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class CustomerDataPage : ContentPage
{
    private readonly CustomerDataService _customerDataService = new();
    private string _filter = "all";

    public CustomerDataPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Recent Customers");
        SearchEntry.TextChanged += (_, _) => _ = LoadAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _customerDataService.PurgeExpiredCacheAsync();

            var summary = await _customerDataService.GetSyncSummaryAsync();
            SyncedCountLabel.Text = summary.SyncedCount.ToString();
            PendingCountLabel.Text = summary.PendingCount.ToString();
            FailedCountLabel.Text = summary.FailedCount.ToString();
            LastSyncLabel.Text = summary.LastCloudSyncDisplay;

            var records = await _customerDataService.GetAllAsync(SearchEntry.Text, _filter);
            CustomersCollection.ItemsSource = records;
            SummaryLabel.Text = $"{records.Count} of {summary.TotalRecent} recent customer(s) shown · queue {summary.QueueCount}";
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Recent Customers", $"Could not load cache: {ex.Message}");
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadAsync();
    }

    private async void OnRetrySyncClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await _customerDataService.RetrySyncAsync();
            await AppAlertService.ShowAlertAsync("Customer Sync", result.Message ?? "Sync complete.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Sync Failed", ex.Message);
        }
    }

    private async void OnFilterAllClicked(object sender, EventArgs e)
    {
        _filter = "all";
        SetFilterButtonStyles(FilterAllButton, FilterCollectionButton, FilterDeliveryButton);
        await LoadAsync();
    }

    private async void OnFilterCollectionClicked(object sender, EventArgs e)
    {
        _filter = "collection";
        SetFilterButtonStyles(FilterCollectionButton, FilterAllButton, FilterDeliveryButton);
        await LoadAsync();
    }

    private async void OnFilterDeliveryClicked(object sender, EventArgs e)
    {
        _filter = "delivery";
        SetFilterButtonStyles(FilterDeliveryButton, FilterAllButton, FilterCollectionButton);
        await LoadAsync();
    }

    private static void SetFilterButtonStyles(Button active, Button inactiveA, Button inactiveB)
    {
        active.BackgroundColor = Color.FromArgb("#3B82F6");
        active.TextColor = Colors.White;
        inactiveA.BackgroundColor = Color.FromArgb("#E2E8F0");
        inactiveA.TextColor = Color.FromArgb("#334155");
        inactiveB.BackgroundColor = Color.FromArgb("#E2E8F0");
        inactiveB.TextColor = Color.FromArgb("#334155");
    }

    private async void OnDeleteCustomerClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CustomerDataRecord record })
        {
            return;
        }

        var confirm = await DisplayAlert(
            "Remove local cache",
            $"Remove {record.Name} ({record.PhoneNumber}) from this terminal's 7-day cache?\n\nOrderWeb cloud records are not deleted.",
            "Remove",
            "Cancel");

        if (!confirm)
        {
            return;
        }

        try
        {
            if (await _customerDataService.DeleteLocalCacheAsync(record.Id))
            {
                await LoadAsync();
            }
            else
            {
                await AppAlertService.ShowAlertAsync("Remove Failed", "Could not remove this cache row.");
            }
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Remove Failed", ex.Message);
        }
    }
}
