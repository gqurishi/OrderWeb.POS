using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

public partial class CollectionOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private bool _isContinuing;
    private bool _isClosing;
    private bool _enterAnimationStarted;
    /// <summary>Stable Mother order id for this Collection create attempt (avoids dual rows on retry).</summary>
    private string? _pendingCollectionOrderId;

    public CollectionOrderPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);
        // Start off-screen so Delivery/Collection slide in from the right like Mother.
        Opacity = 0;
        TranslationX = 420;

        Entry.SearchRequested += OnSearchRequested;
        Entry.ContinueRequested += OnContinueRequested;
        Entry.CancelRequested += OnCancelRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        if (_enterAnimationStarted)
        {
            return;
        }

        _enterAnimationStarted = true;
        var width = Width > 1 ? Width : (DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density);
        TranslationX = Math.Max(width, 420);
        Opacity = 1;
        await this.TranslateToAsync(0, 0, 280, Easing.CubicOut);
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnSearchRequested(object? sender, (string? Name, string? Phone) e)
    {
        var searchName = e.Name?.Trim();
        var searchPhone = e.Phone?.Trim();

        if (string.IsNullOrWhiteSpace(searchName) && string.IsNullOrWhiteSpace(searchPhone))
        {
            Entry.ShowStatus("Please enter customer name or phone number to search.", "#DC2626");
            return;
        }

        Entry.SetSearchBusy(true);
        try
        {
            var request = new CustomerSearchRequest("Collection", searchName, searchPhone, null);
            var mother = await _customerClient.SearchCustomersAsync(request);
            var results = mother
                .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
                .Select(group => group.First())
                .Take(10)
                .ToList();

            Entry.SetSearchResults(results.Select(ToSearchItem));
            Entry.ShowStatus(results.Count == 0 ? "No existing customer found." : $"Found {results.Count} customer(s).", results.Count == 0 ? "#64748B" : "#10B981");
        }
        catch (Exception ex)
        {
            Entry.ShowStatus($"Failed to search customers: {ex.Message}", "#DC2626");
        }
        finally
        {
            Entry.SetSearchBusy(false);
        }
    }

    private async void OnContinueRequested(object? sender, CustomerEntryResult e)
    {
        if (_isContinuing)
        {
            return;
        }

        var customer = new CachedCustomer(0, e.MotherId ?? string.Empty, e.Name, e.Phone, null, string.Empty, null, 0);

        var draft = new CustomerOrderDraft(
            "Collection",
            customer,
            "ASAP",
            null,
            string.Empty,
            null,
            null,
            null,
            0m);

        _isContinuing = true;
        Entry.SetContinueBusy(true, "Opening order...");

        try
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.SaveCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                Entry.ShowStatus(decision.Message, "#DC2626");
                return;
            }

            Entry.ShowStatus("Saving customer with Mother POS...", "#64748B");
            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomerForActiveOrderAsync(savedCustomer, isDelivery: false);

            Entry.ShowStatus("Opening collection order...", "#64748B");
            var session = await _cache.GetCurrentLoginSessionAsync();
            _pendingCollectionOrderId ??= Guid.NewGuid().ToString("N");
            var orderResult = await _orderClient.CreateCustomerOrderAsync(
                draft with { Customer = savedCustomer },
                session,
                _pendingCollectionOrderId);
            await _cache.SaveOrderStateAsync(orderResult.State);
            _pendingCollectionOrderId = null;
            if (orderResult.ConflictDetected)
            {
                Entry.ShowStatus(orderResult.Message, "#D97706");
            }

            await Navigation.PushAsync(
                new OrderPage(orderResult.State, savedCustomer.Name, savedCustomer.Phone),
                false);
        }
        catch (Exception ex)
        {
            Entry.ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
        finally
        {
            _isContinuing = false;
            Entry.SetContinueBusy(false);
        }
    }

    private async void OnCancelRequested(object? sender, EventArgs e) => await CloseAsync();

    private async Task CloseAsync()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        try
        {
            var width = Width > 1 ? Width : 420;
            await this.TranslateToAsync(width, 0, 220, Easing.CubicIn);
            await ClientSideNavigation.PopFromSideAsync(Navigation);
        }
        catch
        {
            await ClientSideNavigation.PopFromSideAsync(Navigation);
        }
        finally
        {
            _isClosing = false;
        }
    }

    private static CustomerEntrySearchItem ToSearchItem(CachedCustomer customer) =>
        new(customer.Name, customer.Phone, customer.MotherId);
}
