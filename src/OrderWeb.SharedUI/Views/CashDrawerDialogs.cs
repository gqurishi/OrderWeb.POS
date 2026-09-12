using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

/// <summary>Mother cash-drawer chrome. Hosts own drawer open, till expenses, and permissions.</summary>
public static class CashDrawerReasons
{
    public const string NoSale = "No Sale";
    public const string Shopping = "Shopping";
    public const string Delivery = "Delivery";
    public const string Refund = "Refund";
    public const string CashCount = "Cash Count";
    public const string Other = "Other";

    public static readonly string[] All =
    [
        NoSale, Shopping, Delivery, Refund, CashCount, Other
    ];
}

public enum CashDrawerUiKind
{
    NoSale,
    Refund,
    ShoppingTake,
    ShoppingSettle,
    Delivery,
    CashCount,
    Other
}

public sealed record CashDrawerUiResult(CashDrawerUiKind Kind, decimal? Amount, string? Details, int? ExpenseId = null);

/// <summary>One pending shopping trip. Hosts load this; SharedUI only draws the picker and settle form.</summary>
public sealed record CashDrawerPendingTrip(int Id, string PickerLabel, string Summary, decimal AmountTaken, string Description);

/// <summary>Same reason → amount sequence as Mother CashDrawerFlowService. No API calls.</summary>
public static class CashDrawerDialogFlow
{
    public static async Task<CashDrawerUiResult?> CollectAsync(ContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var reason = await ShowChoicesAsync(
            page,
            "Cash Drawer Reason",
            CashDrawerReasons.All,
            grid: true,
            icon: "£",
            iconColor: "#0F766E");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        switch (reason)
        {
            case CashDrawerReasons.NoSale:
                if (!await ConfirmOpenAsync(page, CashDrawerReasons.NoSale))
                {
                    return null;
                }

                return new CashDrawerUiResult(CashDrawerUiKind.NoSale, null, null);

            case CashDrawerReasons.Refund:
                if (!await ConfirmOpenAsync(page, CashDrawerReasons.Refund))
                {
                    return null;
                }

                return new CashDrawerUiResult(CashDrawerUiKind.Refund, null, null);

            case CashDrawerReasons.Shopping:
                var shopping = await ShowChoicesAsync(
                    page,
                    "Shopping",
                    ["Take from till", "Settle pending trip"],
                    grid: false,
                    icon: "£",
                    iconColor: "#0F766E");
                if (shopping is null)
                {
                    return null;
                }

                if (shopping == "Settle pending trip")
                {
                    return new CashDrawerUiResult(CashDrawerUiKind.ShoppingSettle, null, null);
                }

                var take = await new CashDrawerShoppingTakeDialog().ShowAsync(page);
                return take is null
                    ? null
                    : new CashDrawerUiResult(CashDrawerUiKind.ShoppingTake, take.Value.Amount, take.Value.Item);

            case CashDrawerReasons.Delivery:
                var payout = await new CashDrawerAmountDialog(
                    "Delivery payout",
                    "Record cash paid out for delivery before opening the drawer.",
                    "Amount out (£)",
                    "Pay & Open",
                    requirePositive: true).ShowAsync(page);
                return payout is null
                    ? null
                    : new CashDrawerUiResult(CashDrawerUiKind.Delivery, payout, null);

            case CashDrawerReasons.CashCount:
                var counted = await new CashDrawerAmountDialog(
                    "Cash count",
                    "Enter the cash counted in the drawer.",
                    "Counted cash (£)",
                    "Record & Open",
                    requirePositive: false).ShowAsync(page);
                return counted is null
                    ? null
                    : new CashDrawerUiResult(CashDrawerUiKind.CashCount, counted, null);

            default:
                var otherReason = await new CashDrawerPromptDialog(
                    "Other till expense",
                    "Enter the reason for opening the cash drawer:",
                    "Reason").ShowAsync(page);
                if (string.IsNullOrWhiteSpace(otherReason))
                {
                    return null;
                }

                var otherAmount = await new CashDrawerAmountDialog(
                    "Amount out (optional)",
                    "Leave as 0 if no cash is leaving the till.",
                    "Amount out (£)",
                    "Continue",
                    requirePositive: false).ShowAsync(page);
                return otherAmount is null
                    ? null
                    : new CashDrawerUiResult(CashDrawerUiKind.Other, otherAmount, otherReason.Trim());
        }
    }

    public static Task ShowNoticeAsync(ContentPage page, string title, string message, string icon, string iconColor) =>
        new CashDrawerNoticeDialog(title, message, icon, iconColor).ShowAsync(page);

    /// <summary>Mother settle path: trip list, then spent form. Empty list shows Mother's info dialog.</summary>
    public static async Task<CashDrawerUiResult?> CollectSettleAsync(ContentPage page, IReadOnlyList<CashDrawerPendingTrip> pending)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (pending.Count == 0)
        {
            await ShowNoticeAsync(page, "No pending trips", "There are no shopping trips waiting to be settled.", "i", "#3B82F6");
            return null;
        }

        var picked = await ShowChoicesAsync(
            page,
            "Settle shopping trip",
            pending.Select(trip => trip.PickerLabel).ToList(),
            grid: false,
            icon: "£",
            iconColor: "#0F766E");
        if (string.IsNullOrWhiteSpace(picked))
        {
            return null;
        }

        var trip = pending.FirstOrDefault(item => string.Equals(item.PickerLabel, picked, StringComparison.Ordinal));
        if (trip is null)
        {
            return null;
        }

        var spent = await new CashDrawerShoppingSettleDialog(trip.Summary, trip.AmountTaken).ShowAsync(page);
        return spent is null
            ? null
            : new CashDrawerUiResult(CashDrawerUiKind.ShoppingSettle, spent, trip.Description, trip.Id);
    }

    private static async Task<bool> ConfirmOpenAsync(ContentPage page, string reason) =>
        await new CashDrawerConfirmDialog(
            "Open Cash Drawer",
            $"Open the cash drawer for: {reason}?",
            "Open",
            "No").ShowAsync(page);

    private static Task<string?> ShowChoicesAsync(
        ContentPage page,
        string title,
        IReadOnlyList<string> options,
        bool grid,
        string icon,
        string iconColor)
    {
        var sheet = new CashDrawerActionSheetDialog();
        if (grid)
        {
            sheet.SetGrid(title, options, icon, iconColor);
        }
        else
        {
            sheet.SetList(title, options, icon, iconColor);
        }

        return sheet.ShowAsync(page);
    }
}

public sealed class CashDrawerActionSheetDialog : ContentView
{
    private readonly Border _dialog;
    private readonly Border _iconBorder;
    private readonly Label _iconLabel;
    private readonly Label _title;
    private readonly VerticalStackLayout _list;
    private readonly Grid _grid;
    private TaskCompletionSource<string?>? _tcs;

    public CashDrawerActionSheetDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        _iconLabel = CashDrawerChrome.IconLabel("£");
        _iconBorder = CashDrawerChrome.Icon("#0F766E", _iconLabel);
        _title = CashDrawerChrome.Title("Cash Drawer Reason");
        _list = new VerticalStackLayout { Spacing = 10, Margin = new Thickness(0, 10), IsVisible = false };
        _grid = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            ColumnSpacing = 12,
            RowSpacing = 12,
            Margin = new Thickness(0, 10),
            IsVisible = false
        };

        var cancel = CashDrawerChrome.Button("Cancel", "#E2E8F0", "#334155", 50, 14);
        cancel.Clicked += (_, _) => Complete(null);

        _dialog = CashDrawerChrome.Card(520, new VerticalStackLayout
        {
            Spacing = 20,
            Children = { _iconBorder, _title, _list, _grid, cancel }
        });

        Content = new Grid
        {
            Padding = 40,
            Children = { _dialog }
        };
    }

    public void SetList(string title, IReadOnlyList<string> options, string icon = "£", string iconColor = "#0F766E")
    {
        _dialog.WidthRequest = 450;
        ApplyHeader(title, icon, iconColor);
        _list.IsVisible = true;
        _list.Children.Clear();
        _grid.IsVisible = false;
        _grid.Children.Clear();
        foreach (var option in options)
        {
            _list.Children.Add(OptionButton(option, 50, 14));
        }
    }

    public void SetGrid(string title, IReadOnlyList<string> options, string icon = "£", string iconColor = "#0F766E")
    {
        _dialog.WidthRequest = 520;
        ApplyHeader(title, icon, iconColor);
        _list.IsVisible = false;
        _list.Children.Clear();
        _grid.IsVisible = true;
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        var rows = (int)Math.Ceiling(options.Count / 2d);
        for (var row = 0; row < rows; row++)
        {
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < options.Count; i++)
        {
            _grid.Add(OptionButton(options[i], 72, 16), i % 2, i / 2);
        }
    }

    public Task<string?> ShowAsync(ContentPage page)
    {
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private void ApplyHeader(string title, string icon, string iconColor)
    {
        _title.Text = title;
        _iconLabel.Text = string.IsNullOrWhiteSpace(icon) ? "£" : icon.Trim();
        _iconBorder.BackgroundColor = Color.FromArgb(iconColor);
    }

    private Button OptionButton(string option, double height, int corner)
    {
        var button = CashDrawerChrome.Button(option, "#F8FAFC", "#1E293B", height, corner, "#D8E1ED");
        button.Clicked += (_, _) => Complete(option);
        return button;
    }

    private void Complete(string? value) => _tcs?.TrySetResult(value);
}

public sealed class CashDrawerAmountDialog : ContentView
{
    private readonly Entry _amount;
    private readonly Grid _overlay;
    private readonly bool _requirePositive;
    private ContentPage? _page;
    private TaskCompletionSource<decimal?>? _tcs;
    private bool _keyboardOpen;

    public CashDrawerAmountDialog(string title, string subtitle, string fieldLabel, string confirmText, bool requirePositive)
    {
        _requirePositive = requirePositive;
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        _amount = CashDrawerChrome.FieldEntry("0.00");

        var cancel = CashDrawerChrome.Button("Cancel", "#E2E8F0", "#334155", 48, 14);
        var confirm = CashDrawerChrome.Button(confirmText, "#0F766E", "#FFFFFF", 48, 14);
        cancel.Clicked += (_, _) => Complete(null);
        confirm.Clicked += async (_, _) => await ConfirmAsync();

        _overlay = CashDrawerChrome.DialogOverlay(CashDrawerChrome.Card(460, new VerticalStackLayout
                {
                    Spacing = 18,
                    Children =
                    {
                        CashDrawerChrome.Icon("#0F766E", CashDrawerChrome.IconLabel("£"), 74, new Thickness(0, 0, 0, 4)),
                        CashDrawerChrome.Title(title),
                        CashDrawerChrome.Subtitle(subtitle),
                        CashDrawerChrome.FieldLabel(fieldLabel),
                        CashDrawerChrome.TappableField(_amount, () => EditAmountAsync(fieldLabel)),
                        CashDrawerChrome.ButtonRow(cancel, confirm)
                    }
                }));
        Content = _overlay;
    }

    public Task<decimal?> ShowAsync(ContentPage page)
    {
        _page = page;
        _tcs = new TaskCompletionSource<decimal?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private async Task EditAmountAsync(string title)
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var page = FindHostPage();
            var value = await CashDrawerChrome.AskAmountAsync(page, _overlay, title, CashDrawerChrome.ParseAmount(_amount.Text), 0m);
            if (value.HasValue)
            {
                _amount.Text = value.Value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async Task ConfirmAsync()
    {
        if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            && !decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
        {
            await ShowInvalidAsync("Enter a valid amount.");
            return;
        }

        if (amount < 0 || (_requirePositive && amount <= 0))
        {
            await ShowInvalidAsync(_requirePositive ? "Enter an amount greater than zero." : "Amount cannot be negative.");
            return;
        }

        Complete(amount);
    }

    private async Task ShowInvalidAsync(string message)
    {
        var page = FindHostPage();
        if (page is null)
        {
            return;
        }

        await CashDrawerDialogFlow.ShowNoticeAsync(page, "Invalid amount", message, "!", "#EF4444");
    }

    private void Complete(decimal? value) => _tcs?.TrySetResult(value);

    private ContentPage? FindHostPage() =>
        _page ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage
        ?? Shell.Current?.CurrentPage as ContentPage;
}

public sealed class CashDrawerShoppingTakeDialog : ContentView
{
    private readonly Entry _item;
    private readonly Entry _amount;
    private readonly Grid _overlay;
    private ContentPage? _page;
    private TaskCompletionSource<(string Item, decimal Amount)?>? _tcs;
    private bool _keyboardOpen;

    public CashDrawerShoppingTakeDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        _item = CashDrawerChrome.FieldEntry("e.g. Milk");
        _amount = CashDrawerChrome.FieldEntry("0.00");

        var cancel = CashDrawerChrome.Button("Cancel", "#E2E8F0", "#334155", 48, 14);
        var confirm = CashDrawerChrome.Button("Take & Open", "#0F766E", "#FFFFFF", 48, 14);
        cancel.Clicked += (_, _) => Complete(null);
        confirm.Clicked += async (_, _) => await ConfirmAsync();

        _overlay = CashDrawerChrome.DialogOverlay(CashDrawerChrome.Card(460, new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                CashDrawerChrome.Icon("#0F766E", CashDrawerChrome.IconLabel("£"), 74, new Thickness(0, 0, 0, 4)),
                CashDrawerChrome.Title("Shopping — Take from till"),
                CashDrawerChrome.Subtitle("Record cash taken before opening the drawer."),
                CashDrawerChrome.FieldLabel("Item / reason"),
                CashDrawerChrome.TappableField(_item, EditItemAsync),
                CashDrawerChrome.FieldLabel("Amount taken (£)"),
                CashDrawerChrome.TappableField(_amount, EditAmountAsync),
                CashDrawerChrome.ButtonRow(cancel, confirm)
            }
        }));
        Content = _overlay;
    }

    public Task<(string Item, decimal Amount)?> ShowAsync(ContentPage page)
    {
        _page = page;
        _tcs = new TaskCompletionSource<(string Item, decimal Amount)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private async Task EditItemAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var value = await CashDrawerChrome.AskTextAsync(_page, _overlay, "Item / reason", _item.Placeholder, _item.Text, required: true);
            if (value is not null)
            {
                _item.Text = value;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async Task EditAmountAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var value = await CashDrawerChrome.AskAmountAsync(_page, _overlay, "Amount taken (£)", CashDrawerChrome.ParseAmount(_amount.Text), 0.01m);
            if (value.HasValue)
            {
                _amount.Text = value.Value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async Task ConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(_item.Text))
        {
            await ShowInvalidAsync("Enter the item or reason.");
            return;
        }

        if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            await ShowInvalidAsync("Enter an amount greater than zero.");
            return;
        }

        Complete((_item.Text.Trim(), amount));
    }

    private async Task ShowInvalidAsync(string message)
    {
        if (_page is not null)
        {
            await CashDrawerDialogFlow.ShowNoticeAsync(_page, "Shopping", message, "!", "#EF4444");
        }
    }

    private void Complete((string Item, decimal Amount)? value) => _tcs?.TrySetResult(value);
}

public sealed class CashDrawerShoppingSettleDialog : ContentView
{
    private readonly Entry _spent;
    private readonly Label _change;
    private readonly Grid _overlay;
    private readonly decimal _taken;
    private ContentPage? _page;
    private TaskCompletionSource<decimal?>? _tcs;
    private bool _keyboardOpen;

    public CashDrawerShoppingSettleDialog(string summary, decimal amountTaken)
    {
        _taken = amountTaken;
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        _spent = CashDrawerChrome.FieldEntry("0.00");
        _spent.TextChanged += (_, _) => UpdateChange();

        _change = new Label
        {
            Text = "Change to return: £0.00",
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb("#0F766E"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var cancel = CashDrawerChrome.Button("Cancel", "#E2E8F0", "#334155", 48, 14);
        var confirm = CashDrawerChrome.Button("Settle & Open", "#0F766E", "#FFFFFF", 48, 14);
        cancel.Clicked += (_, _) => Complete(null);
        confirm.Clicked += async (_, _) => await ConfirmAsync();

        _overlay = CashDrawerChrome.DialogOverlay(CashDrawerChrome.Card(460, new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                CashDrawerChrome.Icon("#0F766E", CashDrawerChrome.IconLabel("£"), 74, new Thickness(0, 0, 0, 4)),
                CashDrawerChrome.Title("Settle shopping trip"),
                CashDrawerChrome.Subtitle(summary),
                CashDrawerChrome.FieldLabel("Actual spent (£)"),
                CashDrawerChrome.TappableField(_spent, EditSpentAsync),
                        new Border
                        {
                            BackgroundColor = Color.FromArgb("#F0FDFA"),
                            Stroke = Color.FromArgb("#CCFBF1"),
                            StrokeThickness = 1,
                            Padding = new Thickness(14, 12),
                            StrokeShape = new RoundRectangle { CornerRadius = 14 },
                            Content = _change
                        },
                CashDrawerChrome.ButtonRow(cancel, confirm)
            }
        }));
        Content = _overlay;
    }

    public Task<decimal?> ShowAsync(ContentPage page)
    {
        _page = page;
        _tcs = new TaskCompletionSource<decimal?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private void UpdateChange()
    {
        if (!decimal.TryParse(_spent.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var spent)
            && !decimal.TryParse(_spent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out spent))
        {
            _change.Text = "Change to return: —";
            return;
        }

        var change = Math.Max(0, _taken - spent);
        _change.Text = $"Change to return: £{change:F2}";
    }

    private async Task EditSpentAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var value = await CashDrawerChrome.AskAmountAsync(_page, _overlay, "Actual spent", CashDrawerChrome.ParseAmount(_spent.Text), 0m, _taken);
            if (value.HasValue)
            {
                _spent.Text = value.Value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async Task ConfirmAsync()
    {
        if (!decimal.TryParse(_spent.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var spent)
            && !decimal.TryParse(_spent.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out spent))
        {
            await ShowInvalidAsync("Enter how much was actually spent.");
            return;
        }

        if (spent < 0 || spent > _taken)
        {
            await ShowInvalidAsync($"Spent must be between £0 and £{_taken:F2}.");
            return;
        }

        Complete(spent);
    }

    private async Task ShowInvalidAsync(string message)
    {
        if (_page is not null)
        {
            await CashDrawerDialogFlow.ShowNoticeAsync(_page, "Invalid amount", message, "!", "#EF4444");
        }
    }

    private void Complete(decimal? value) => _tcs?.TrySetResult(value);
}

public sealed class CashDrawerPromptDialog : ContentView
{
    private readonly Entry _entry;
    private readonly Grid _overlay;
    private ContentPage? _page;
    private TaskCompletionSource<string?>? _tcs;
    private bool _keyboardOpen;

    public CashDrawerPromptDialog(string title, string message, string placeholder)
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        _entry = CashDrawerChrome.FieldEntry(placeholder);

        var cancel = CashDrawerChrome.Button("Cancel", "#E2E8F0", "#334155", 48, 14);
        var ok = CashDrawerChrome.Button("Continue", "#2563EB", "#FFFFFF", 48, 14);
        cancel.Clicked += (_, _) => Complete(null);
        ok.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_entry.Text))
            {
                if (_page is not null)
                {
                    await CashDrawerDialogFlow.ShowNoticeAsync(_page, title, "Enter a reason before continuing.", "!", "#EF4444");
                }

                return;
            }

            Complete(_entry.Text.Trim());
        };

        _overlay = CashDrawerChrome.DialogOverlay(CashDrawerChrome.Card(460, new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                CashDrawerChrome.Icon("#2563EB", CashDrawerChrome.IconLabel("?"), 74, new Thickness(0, 0, 0, 4)),
                CashDrawerChrome.Title(title),
                CashDrawerChrome.Subtitle(message),
                CashDrawerChrome.TappableField(_entry, () => EditAsync(title)),
                CashDrawerChrome.ButtonRow(cancel, ok)
            }
        }));
        Content = _overlay;
    }

    public Task<string?> ShowAsync(ContentPage page)
    {
        _page = page;
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private async Task EditAsync(string title)
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            var value = await CashDrawerChrome.AskTextAsync(_page, _overlay, title, _entry.Placeholder, _entry.Text, required: true);
            if (value is not null)
            {
                _entry.Text = value;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private void Complete(string? value) => _tcs?.TrySetResult(value);
}

public sealed class CashDrawerConfirmDialog : ContentView
{
    private TaskCompletionSource<bool>? _tcs;

    public CashDrawerConfirmDialog(string title, string message, string accept, string cancel)
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        var no = CashDrawerChrome.Button(cancel, "#E2E8F0", "#334155", 50, 14);
        var yes = CashDrawerChrome.Button(accept, "#2563EB", "#FFFFFF", 50, 14);
        no.Clicked += (_, _) => Complete(false);
        yes.Clicked += (_, _) => Complete(true);

        Content = new Grid
        {
            Padding = 40,
            Children =
            {
                CashDrawerChrome.Card(450, new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        CashDrawerChrome.Icon("#2563EB", CashDrawerChrome.IconLabel("£")),
                        CashDrawerChrome.Title(title),
                        CashDrawerChrome.Subtitle(message),
                        CashDrawerChrome.ButtonRow(no, yes)
                    }
                })
            }
        };
    }

    public Task<bool> ShowAsync(ContentPage page)
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private void Complete(bool value) => _tcs?.TrySetResult(value);
}

public sealed class CashDrawerNoticeDialog : ContentView
{
    private TaskCompletionSource<bool>? _tcs;

    public CashDrawerNoticeDialog(string title, string message, string icon, string iconColor)
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 8000;

        var ok = CashDrawerChrome.Button("OK", iconColor, "#FFFFFF", 50, 14);
        ok.Clicked += (_, _) => _tcs?.TrySetResult(true);

        Content = new Grid
        {
            Padding = 40,
            Children =
            {
                CashDrawerChrome.Card(450, new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        CashDrawerChrome.Icon(iconColor, CashDrawerChrome.IconLabel(icon)),
                        CashDrawerChrome.Title(title),
                        CashDrawerChrome.Subtitle(message),
                        ok
                    }
                })
            }
        };
    }

    public Task ShowAsync(ContentPage page)
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }
}

internal static class CashDrawerChrome
{
    public static Border Card(double width, View content) => new()
    {
        BackgroundColor = Colors.White,
        Stroke = Color.FromArgb("#DCE3EE"),
        StrokeThickness = 1,
        Padding = 30,
        WidthRequest = width,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        StrokeShape = new RoundRectangle { CornerRadius = 22 },
        Shadow = new Shadow
        {
            Brush = Color.FromArgb("#0F172A"),
            Opacity = 0.16f,
            Radius = 20,
            Offset = new Point(0, 8)
        },
        Content = content
    };

    public static Border Icon(string color, Label icon, double size = 80, Thickness? margin = null) => new()
    {
        WidthRequest = size,
        HeightRequest = size,
        BackgroundColor = Color.FromArgb(color),
        Stroke = Colors.White,
        StrokeThickness = 2,
        HorizontalOptions = LayoutOptions.Center,
        Margin = margin ?? new Thickness(0, 0, 0, 10),
        StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
        Content = icon
    };

    public static Label IconLabel(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 30,
        FontAttributes = FontAttributes.None,
        TextColor = Colors.White,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };

    public static Label Title(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 22,
        FontAttributes = FontAttributes.None,
        TextColor = Color.FromArgb("#1F2937"),
        HorizontalTextAlignment = TextAlignment.Center
    };

    public static Label Subtitle(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansRegular",
        FontSize = 15,
        TextColor = Color.FromArgb("#6B7280"),
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 13,
        TextColor = Color.FromArgb("#64748B"),
        Margin = new Thickness(2, 0, 0, 0)
    };

    public static Entry FieldEntry(string placeholder) => new()
    {
        Placeholder = placeholder,
        IsReadOnly = true,
        FontFamily = "OpenSansRegular",
        FontSize = 16,
        TextColor = Color.FromArgb("#1F2937"),
        PlaceholderColor = Color.FromArgb("#9CA3AF"),
        BackgroundColor = Colors.Transparent,
        HeightRequest = 42
    };

    public static Border Field(View content) => new()
    {
        BackgroundColor = Color.FromArgb("#F8FAFC"),
        Stroke = Color.FromArgb("#D8E1ED"),
        StrokeThickness = 1,
        Padding = new Thickness(14, 12),
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        Content = content
    };

    public static Button Button(string text, string background, string textColor, double height, int corner, string? border = null)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(background),
            TextColor = Color.FromArgb(textColor),
            FontFamily = "OpenSansSemibold",
            FontSize = 16,
            FontAttributes = FontAttributes.None,
            CornerRadius = corner,
            HeightRequest = height,
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0,
            Padding = new Thickness(8, 0),
            HorizontalOptions = LayoutOptions.Fill
        };
        if (!string.IsNullOrWhiteSpace(border))
        {
            button.BorderColor = Color.FromArgb(border);
            button.BorderWidth = 1;
        }

        return button;
    }

    public static Grid DialogOverlay(View card)
    {
        var host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        host.Children.Add(new Grid
        {
            Padding = 36,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { card }
        });
        return host;
    }

    public static Grid ButtonRow(Button left, Button right)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            ColumnSpacing = 14,
            Margin = new Thickness(0, 8, 0, 0)
        };
        row.Add(left);
        row.Add(right, 1, 0);
        return row;
    }

    public static Border TappableField(Entry entry, Func<Task> onTap)
    {
        // Windows Entry swallows the tap and only shows a caret. Keep the Entry as the
        // value store and put a real button over a label so the popup keyboard opens.
        entry.IsReadOnly = true;
        entry.IsVisible = false;
        entry.HeightRequest = 0;
        entry.InputTransparent = true;

        var display = new Label
        {
            FontFamily = "OpenSansRegular",
            FontSize = 16,
            FontAttributes = FontAttributes.None,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 42,
            InputTransparent = true
        };
        void Refresh()
        {
            var hasText = !string.IsNullOrWhiteSpace(entry.Text);
            display.Text = hasText ? entry.Text : entry.Placeholder;
            display.TextColor = Color.FromArgb(hasText ? "#1F2937" : "#9CA3AF");
        }

        Refresh();
        entry.TextChanged += (_, _) => Refresh();

        var open = new Button
        {
            Style = null,
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Transparent,
            BorderWidth = 0,
            FontFamily = "OpenSansRegular",
            FontSize = 16,
            FontAttributes = FontAttributes.None,
            CornerRadius = 0,
            Padding = 0,
            Margin = 0,
            MinimumHeightRequest = 0,
            MinimumWidthRequest = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        open.Clicked += async (_, _) => await onTap();

        return Field(new Grid
        {
            MinimumHeightRequest = 48,
            Children = { display, open }
        });
    }

    public static decimal? ParseAmount(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
            ? amount
            : null;

    public static Task<decimal?> AskAmountAsync(ContentPage? page, Grid? overlay, string title, decimal? initial, decimal minimum, decimal maximum = 999999.99m)
    {
        var keyboard = new NumericKeyboardDialog();
        return keyboard.ShowCurrencyAsync(initial, title, page, minimum, maximum, overlay ?? HostGrid(page));
    }

    public static Task<string?> AskTextAsync(ContentPage? page, Grid? overlay, string title, string? placeholder, string? initial, bool required)
    {
        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt(title, "Done");
        keyboard.SetTextMode(VirtualKeyboardTextMode.Notes);
        keyboard.SetPlaceholder(placeholder);
        keyboard.SetRequired(required);
        keyboard.SetInitialText(initial ?? string.Empty);
        var host = overlay ?? HostGrid(page);
        return host is null
            ? keyboard.ShowAsync(page)
            : keyboard.ShowOverAsync(host, page);
    }

    private static Grid? HostGrid(ContentPage? page) => page?.Content as Grid;
}
