using OrderWeb.SharedUI.Views;
using POS_in_NET.Pages;

namespace POS_in_NET.Services;

/// <summary>
/// Shows SharedUI advance reminder on the active Mother till page (MainThread).
/// One kitchen print (scheduler); many till popups OK across terminals — this is Mother local UI.
/// </summary>
public sealed class AdvanceOrderReminderPresenter
{
    private static readonly string[] HiddenRoutes = ["login", "terminalsetup", "initialadminsetup"];

    private readonly AdvanceOrderService _advance;
    private readonly Queue<AdvanceOrderReminderPresentation> _queue = new();
    private bool _started;
    private bool _showing;

    public AdvanceOrderReminderPresenter(AdvanceOrderService advance)
    {
        _advance = advance;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        AdvanceOrderService.ReminderRaised += OnReminderRaised;
    }

    private void OnReminderRaised(object? sender, AdvanceOrderReminderEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (AuthenticationService.Instance.CurrentUser == null || IsHiddenRoute())
            {
                return;
            }

            _queue.Enqueue(e.Reminder);
            _ = DrainAsync();
        });
    }

    private async Task DrainAsync()
    {
        if (_showing)
        {
            return;
        }

        _showing = true;
        try
        {
            while (_queue.Count > 0)
            {
                if (AuthenticationService.Instance.CurrentUser == null || IsHiddenRoute())
                {
                    _queue.Clear();
                    break;
                }

                var page = Shell.Current?.CurrentPage as ContentPage;
                if (page == null)
                {
                    break;
                }

                var data = _queue.Dequeue();
                var dialog = new AdvanceOrderReminderDialog();
                var action = await dialog.ShowAsync(
                    page,
                    data,
                    async reminder =>
                    {
                        var result = await _advance.PrintKitchenManualAsync(reminder.OrderId);
                        return result.Success
                            ? (true, (string?)null)
                            : (false, result.Message ?? "Print failed");
                    });

                if (action == AdvanceOrderReminderAction.OpenOrder)
                {
                    try
                    {
                        await page.Navigation.PushAsync(
                            new OrderPlacementPageSimple(existingOrderId: data.OrderId),
                            false);
                    }
                    catch (Exception ex)
                    {
                        await AppAlertService.ShowAlertAsync("Advance order", ex.Message);
                    }
                }
            }
        }
        finally
        {
            _showing = false;
            if (_queue.Count > 0)
            {
                _ = DrainAsync();
            }
        }
    }

    private static bool IsHiddenRoute()
    {
        var location = Shell.Current?.CurrentState?.Location?.OriginalString ?? "";
        return HiddenRoutes.Any(route => location.Contains(route, StringComparison.OrdinalIgnoreCase));
    }
}
