using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Orders;
using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

/// <summary>
/// Client host for shared order-entry. Supplies ClientOrderService as IOrderService
/// so UI → ClientOrderService → Mother authority, with local estimates only until
/// Mother returns the authoritative order.
/// </summary>
public sealed class SharedOrderEntryPage : ContentPage
{
    private readonly OrderEntryView _view = new();
    private readonly OrderEntryViewModel _viewModel;
    private readonly CachedTable? _table;
    private readonly int _covers;
    private readonly string _orderType;
    private readonly string? _existingOrderId;
    private bool _initialized;

    public SharedOrderEntryPage(
        ClientOrderService orders,
        ClientMenuCatalogService menu,
        ClientOrderSession session,
        CachedTable? table = null,
        int covers = 1,
        string orderType = "Table",
        string? existingOrderId = null,
        string? title = null)
    {
        Title = title ?? $"{orderType} Order";
        Shell.SetNavBarIsVisible(this, false);
        _table = table;
        _covers = Math.Max(1, covers);
        _orderType = string.IsNullOrWhiteSpace(orderType) ? "Table" : orderType.Trim();
        _existingOrderId = existingOrderId;

        if (!string.IsNullOrWhiteSpace(table?.Id.ToString()))
            orders.EnsureTable(table!.Id.ToString());

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
        : this(
            ResolveOrders(),
            ResolveMenu(),
            ResolveSession(),
            table,
            covers,
            orderType: "Table")
    {
    }

    public static SharedOrderEntryPage ForTable(CachedTable table, int covers) =>
        new(table, covers);

    public static ContentPage ForTableOrLegacy(CachedTable table, int covers) =>
        PreferLegacy()
            ? new OrderPage(table, covers)
            : new SharedOrderEntryPage(table, covers);

    public static ContentPage ForServiceType(string orderType, string? existingOrderId = null) =>
        PreferLegacy()
            ? new OrderPage()
            : new SharedOrderEntryPage(
                ResolveOrders(),
                ResolveMenu(),
                ResolveSession(),
                table: null,
                covers: 1,
                orderType: orderType,
                existingOrderId: existingOrderId,
                title: $"{orderType} Order");

    /// <summary>Explicit temporary rollback path — keep reachable for testing only.</summary>
    public static ContentPage CreateLegacyRollback(CachedTable? table = null, int covers = 1) =>
        table is null ? new OrderPage() : new OrderPage(table, covers);

    private static bool PreferLegacy() => LegacyOrderEntryAccess.PreferLegacyRollback;

    private static IServiceProvider? Services =>
        Application.Current?.Handler?.MauiContext?.Services;

    private static readonly AuthoritativeOrderService FallbackMother = new();

    private static ClientOrderService ResolveOrders() =>
        Services?.GetService(typeof(ClientOrderService)) as ClientOrderService
        ?? new ClientOrderService(
            Services?.GetService(typeof(AuthoritativeOrderService)) as AuthoritativeOrderService
            ?? FallbackMother);

    private static ClientMenuCatalogService ResolveMenu() =>
        Services?.GetService(typeof(ClientMenuCatalogService)) as ClientMenuCatalogService
        ?? new ClientMenuCatalogService(
            Services?.GetService(typeof(ClientCacheService)) as ClientCacheService
            ?? new ClientCacheService(),
            Services?.GetService(typeof(AuthoritativeOrderService)) as AuthoritativeOrderService
            ?? FallbackMother);

    private static ClientOrderSession ResolveSession() =>
        Services?.GetService(typeof(ClientOrderSession)) as ClientOrderSession
        ?? new ClientOrderSession();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;

        _initialized = true;
        var tableId = _table?.Id.ToString() ?? _table?.TableNumber;
        // Do not pass MotherOrderClient cache order ids into AuthoritativeOrderService.
        // Shared entry opens/creates through ClientOrderService (Mother authority).
        await _viewModel.InitializeAsync(
            tableId,
            _covers,
            existingOrderId: _existingOrderId,
            orderType: _orderType);
    }
}
