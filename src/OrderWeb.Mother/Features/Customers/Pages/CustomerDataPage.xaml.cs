using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class CustomerDataPage : ContentPage
{
    private readonly CustomerDataService _customerDataService = new();
    private RecentCustomerFilter _filter = RecentCustomerFilter.All;
    private bool _isLoading;
    private bool _hasPendingLoad;
    private DateTime _lastCachePurgeAt = DateTime.MinValue;
    private static readonly TimeSpan CachePurgeInterval = TimeSpan.FromMinutes(5);

    public CustomerDataPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Recent Customers");
        WireBoard();
    }

    private void WireBoard()
    {
        Board.RefreshRequested += async (_, _) => await LoadAsync(forceCachePurge: true);
        Board.RetrySyncRequested += async (_, _) => await RetrySyncAsync();
        Board.SearchChanged += async (_, _) => await LoadAsync();
        Board.FilterChanged += async (_, e) =>
        {
            _filter = e.Filter;
            await LoadAsync();
        };
        Board.RemoveCacheRequested += async (_, e) => await RemoveCacheAsync(e.Row);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync(forceCachePurge: true);
    }

    private async Task LoadAsync(bool forceCachePurge = false)
    {
        if (_isLoading)
        {
            _hasPendingLoad = true;
            return;
        }

        try
        {
            _isLoading = true;
            Board.SetBusy(true);

            if (forceCachePurge || DateTime.UtcNow - _lastCachePurgeAt > CachePurgeInterval)
            {
                await _customerDataService.PurgeExpiredCacheAsync();
                _lastCachePurgeAt = DateTime.UtcNow;
            }

            var summary = await _customerDataService.GetSyncSummaryAsync();
            var filterCode = RecentCustomerFilterCodes.ToApiCode(_filter);
            var records = await _customerDataService.GetAllAsync(Board.SearchText, filterCode);

            Board.SetSummary(new RecentCustomerSyncSummaryPresentation(
                summary.SyncedCount,
                summary.PendingCount,
                summary.FailedCount,
                summary.LastCloudSyncDisplay,
                $"{records.Count} of {summary.TotalRecent} recent customer(s) shown · queue {summary.QueueCount}"));
            Board.SetRows(records.Select(ToRow).ToList());
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Recent Customers", $"Could not load cache: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            Board.SetBusy(false);
            if (_hasPendingLoad)
            {
                _hasPendingLoad = false;
                _ = LoadAsync();
            }
        }
    }

    private async Task RetrySyncAsync()
    {
        try
        {
            Board.SetBusy(true);
            var result = await _customerDataService.RetrySyncAsync();
            await AppAlertService.ShowAlertAsync("Customer Sync", result.Message ?? "Sync complete.");
            await LoadAsync(forceCachePurge: true);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Sync Failed", ex.Message);
        }
        finally
        {
            Board.SetBusy(false);
        }
    }

    private async Task RemoveCacheAsync(RecentCustomerRowPresentation row)
    {
        if (row.Tag is not CustomerDataRecord record)
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

    private static RecentCustomerRowPresentation ToRow(CustomerDataRecord record) =>
        new(
            record.Name,
            record.ContactDetail,
            record.ShowCollectionBadge,
            record.ShowDeliveryBadge,
            record.ShowSyncedBadge,
            record.ShowPendingSyncBadge,
            record.ShowFailedSyncBadge,
            record);
}
