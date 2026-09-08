using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Client.Views.Dialogs;

namespace OrderWeb.Client.Pages.Orders;

public partial class CollectionOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private CachedCustomer? _selectedCustomer;
    private bool _isContinuing;
    private bool _isClosing;
    private bool _isOpeningKeyboard;
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

    private async void OnCustomerNameFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(CustomerNameEntry);
    }

    private async void OnPhoneNumberFieldTapped(object sender, TappedEventArgs e)
    {
        await OpenKeyboardForEntryAsync(PhoneNumberEntry);
    }

    private async Task OpenKeyboardForEntryAsync(Entry entry)
    {
        if (_isOpeningKeyboard)
        {
            return;
        }

        _isOpeningKeyboard = true;
        try
        {
            entry.Unfocus();

            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(GetKeyboardTitle(entry), "DONE");
            keyboard.SetInitialText(entry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync(this);
            if (result != null)
            {
                entry.Text = result.Trim();
            }
        }
        finally
        {
            _isOpeningKeyboard = false;
        }
    }

    private string GetKeyboardTitle(Entry entry)
    {
        if (entry == CustomerNameEntry)
        {
            return "Customer name";
        }

        if (entry == PhoneNumberEntry)
        {
            return "Phone number";
        }

        return "Keyboard";
    }

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        var searchName = CustomerNameEntry.Text?.Trim();
        var searchPhone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(searchName) && string.IsNullOrWhiteSpace(searchPhone))
        {
            ShowStatus("Please enter customer name or phone number to search.", "#DC2626");
            return;
        }

        var originalText = SearchButton.Text;
        SearchButton.Text = "Searching...";
        SearchButton.IsEnabled = false;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;

        try
        {
            var request = new CustomerSearchRequest("Collection", searchName, searchPhone, null);
            var mother = await _customerClient.SearchCustomersAsync(request);
            var results = mother
                .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
                .Select(group => group.First())
                .Take(10)
                .ToList();

            SearchResultsCollection.ItemsSource = results;
            SearchResultsBorder.IsVisible = results.Count > 0;
            NoResultsLabel.IsVisible = results.Count == 0;
            ShowStatus(results.Count == 0 ? "No existing customer found." : $"Found {results.Count} customer(s).", results.Count == 0 ? "#64748B" : "#10B981");
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to search customers: {ex.Message}", "#DC2626");
        }
        finally
        {
            SearchButton.Text = originalText;
            SearchButton.IsEnabled = true;
        }
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CachedCustomer customer)
        {
            return;
        }

        ApplyCustomer(customer);
        ((CollectionView)sender).SelectedItem = null;
    }

    private void OnCustomerTapped(object sender, EventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CachedCustomer customer)
        {
            ApplyCustomer(customer);
        }
    }

    private void ApplyCustomer(CachedCustomer customer)
    {
        _selectedCustomer = customer;
        CustomerNameEntry.Text = customer.Name;
        PhoneNumberEntry.Text = customer.Phone;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ShowStatus("Existing customer selected.", "#10B981");
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Customer Name is required to continue.", "#DC2626");
            CustomerNameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            ShowStatus("Phone Number is required to continue.", "#DC2626");
            PhoneNumberEntry.Focus();
            return;
        }

        var customer = _selectedCustomer ?? new CachedCustomer(0, string.Empty, name, phone, null, string.Empty, null, 0);
        customer = customer with
        {
            Name = name,
            Phone = phone
        };

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

        var button = sender as Button;
        var originalText = button?.Text;
        _isContinuing = true;
        if (button != null)
        {
            button.IsEnabled = false;
            button.Text = "Opening order...";
        }

        try
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.SaveCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                ShowStatus(decision.Message, "#DC2626");
                return;
            }
            ShowStatus("Saving customer with Mother POS...", "#64748B");
            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomerForActiveOrderAsync(savedCustomer, isDelivery: false);

            ShowStatus("Opening collection order...", "#64748B");
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
                ShowStatus(orderResult.Message, "#D97706");
            }

            await Navigation.PushAsync(
                new OrderPage(orderResult.State, savedCustomer.Name, savedCustomer.Phone),
                false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
        finally
        {
            _isContinuing = false;
            if (button != null)
            {
                button.Text = originalText ?? "Continue to Order";
                button.IsEnabled = true;
            }
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e) => await CloseAsync();

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

    private void ShowStatus(string message, string color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(color);
        StatusLabel.IsVisible = true;
    }
}
