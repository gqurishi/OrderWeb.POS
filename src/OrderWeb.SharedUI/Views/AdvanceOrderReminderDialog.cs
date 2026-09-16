using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

/// <summary>Host outcome for reminder dialog actions.</summary>
public enum AdvanceOrderReminderAction
{
    Dismissed,
    OpenOrder,
    PrintKitchen
}

/// <summary>Data shown on the advance reminder popup (host maps from WS / local).</summary>
public sealed record AdvanceOrderReminderPresentation(
    string OrderId,
    string? OrderNumber,
    string OrderType,
    string ScheduledDisplay,
    string CustomerName,
    string? CustomerPhone = null,
    bool KitchenPrinted = false);

/// <summary>
/// Till popup when Mother fires an advance kitchen reminder (T−3h / inside window).
/// No HTTP — host handles Open / Print kitchen callbacks.
/// </summary>
public sealed class AdvanceOrderReminderDialog : ContentView
{
    private readonly Label _bodyLabel = MotherDialogVisuals.Message();
    private readonly Label _statusLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#DC2626"),
        IsVisible = false,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Button _openButton = ActionButton("Open order", "#2563EB", Colors.White);
    private readonly Button _printButton = ActionButton("Print kitchen now", "#059669", Colors.White);
    private readonly Button _dismissButton = ActionButton("Dismiss", "#E2E8F0", Color.FromArgb("#334155"));

    private TaskCompletionSource<AdvanceOrderReminderAction>? _tcs;
    private ContentPage? _page;
    private AdvanceOrderReminderPresentation? _data;
    private bool _busy;
    private Func<AdvanceOrderReminderPresentation, Task<(bool Ok, string? Error)>>? _printAsync;

    public AdvanceOrderReminderDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");

        _openButton.Clicked += async (_, _) => await CompleteAsync(AdvanceOrderReminderAction.OpenOrder);
        _dismissButton.Clicked += async (_, _) => await CompleteAsync(AdvanceOrderReminderAction.Dismissed);
        _printButton.Clicked += async (_, _) => await PrintAsync();

        var actions = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { _openButton, _printButton, _dismissButton }
        };

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                MotherDialogVisuals.IconCircle("⏱", size: 64, fontSize: 28, motherBlue: true),
                MotherDialogVisuals.Title("Advance order"),
                _bodyLabel,
                _statusLabel,
                actions
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(440, 480, body, padding: 26));
    }

    /// <summary>
    /// Shows the reminder overlay. Optional <paramref name="printAsync"/> runs on Print kitchen now
    /// (busy-guarded); Open/Dismiss complete immediately.
    /// </summary>
    public Task<AdvanceOrderReminderAction> ShowAsync(
        ContentPage page,
        AdvanceOrderReminderPresentation data,
        Func<AdvanceOrderReminderPresentation, Task<(bool Ok, string? Error)>>? printAsync = null)
    {
        _tcs = new TaskCompletionSource<AdvanceOrderReminderAction>();
        _page = page;
        _data = data;
        _printAsync = printAsync;
        _busy = false;
        _statusLabel.IsVisible = false;
        SetButtonsEnabled(true);

        var type = string.IsNullOrWhiteSpace(data.OrderType) ? "Order" : data.OrderType;
        var when = string.IsNullOrWhiteSpace(data.ScheduledDisplay) ? "—" : data.ScheduledDisplay;
        var customer = string.IsNullOrWhiteSpace(data.CustomerName) ? "Customer" : data.CustomerName;
        var number = string.IsNullOrWhiteSpace(data.OrderNumber) ? "#" : data.OrderNumber;
        _bodyLabel.Text = $"{type} · {when} · {customer} · {number}";

        if (_printAsync == null)
        {
            _printButton.Text = data.KitchenPrinted ? "Print kitchen again" : "Print kitchen now";
        }

        if (page.Content is not Grid overlayRoot)
        {
            overlayRoot = new Grid();
            if (page.Content != null)
            {
                overlayRoot.Children.Add(page.Content);
            }

            page.Content = overlayRoot;
        }

        if (!overlayRoot.Children.Contains(this))
        {
            overlayRoot.Children.Add(this);
        }

        IsVisible = true;
        return _tcs.Task;
    }

    private async Task PrintAsync()
    {
        if (_busy || _data == null)
        {
            return;
        }

        if (_printAsync == null)
        {
            await CompleteAsync(AdvanceOrderReminderAction.PrintKitchen);
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        _statusLabel.IsVisible = false;
        try
        {
            var (ok, error) = await _printAsync(_data);
            if (ok)
            {
                await CompleteAsync(AdvanceOrderReminderAction.PrintKitchen);
                return;
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(error) ? "Kitchen print failed." : error;
            _statusLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            _statusLabel.IsVisible = true;
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private Task CompleteAsync(AdvanceOrderReminderAction action)
    {
        if (_busy && action != AdvanceOrderReminderAction.PrintKitchen)
        {
            return Task.CompletedTask;
        }

        _busy = true;
        SetButtonsEnabled(false);
        IsVisible = false;

        if (_page?.Content is Grid overlayRoot && overlayRoot.Children.Contains(this))
        {
            overlayRoot.Children.Remove(this);
        }

        _tcs?.TrySetResult(action);
        return Task.CompletedTask;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _openButton.IsEnabled = enabled;
        _printButton.IsEnabled = enabled;
        _dismissButton.IsEnabled = enabled;
    }

    private static Button ActionButton(string text, string background, Color foreground) =>
        new()
        {
            Text = text,
            Style = null,
            BackgroundColor = Color.FromArgb(background),
            TextColor = foreground,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 48,
            FontSize = 16
        };
}
