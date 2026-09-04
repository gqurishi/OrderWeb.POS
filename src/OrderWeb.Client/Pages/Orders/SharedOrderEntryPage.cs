using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Orders;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

/// <summary>
/// Client host for shared Phase 12 order-entry (categories, products, modifiers,
/// basket, quantity, notes, discounts, totals, send, void, manager approval).
/// </summary>
public sealed class SharedOrderEntryPage : ContentPage
{
    private readonly OrderEntryView _view = new();
    private readonly OrderEntryViewModel _viewModel;
    private readonly CachedTable? _table;
    private readonly int _covers;

    public SharedOrderEntryPage(
        ClientOrderService orders,
        ClientMenuCatalogService menu,
        ClientOrderSession session,
        CachedTable? table = null,
        int covers = 1)
    {
        Title = "Order Entry";
        Shell.SetNavBarIsVisible(this, false);
        _table = table;
        _covers = Math.Max(1, covers);

        _viewModel = new OrderEntryViewModel(
            orders,
            menu,
            session.Current,
            async () =>
            {
                var code = await DisplayPromptAsync(
                    "Manager approval",
                    "Enter manager approval code to continue.",
                    accept: "Approve",
                    cancel: "Cancel",
                    placeholder: "Code");
                return string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            });

        _viewModel.ErrorOccurred += async (_, error) =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await DisplayAlert("Order", error.Message, "OK"));
        };

        _view.Bind(_viewModel);
        Content = _view;
    }

    public SharedOrderEntryPage(CachedTable table, int covers)
        : this(CreateDefaultOrders(), CreateDefaultMenu(), new ClientOrderSession(), table, covers)
    {
    }

    private static readonly AuthoritativeOrderService SharedMother = new();
    private static ClientOrderService CreateDefaultOrders() => new(SharedMother);
    private static ClientMenuCatalogService CreateDefaultMenu() =>
        new(new ClientCacheService(), SharedMother);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var tableId = _table?.Id.ToString() ?? _table?.TableNumber;
        await _viewModel.InitializeAsync(tableId, _covers, _table?.CurrentOrderId);
    }
}
