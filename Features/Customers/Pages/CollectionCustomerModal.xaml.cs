using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

namespace POS_in_NET.Pages;

public partial class CollectionCustomerModal : ContentPage
{
    private readonly CollectionCustomerService _customerService;
    private readonly CustomerDataService _customerDataService = new();
    private CollectionCustomer? _selectedCustomer;
    private bool _isOpeningKeyboard;

    public CollectionCustomerModal()
    {
        InitializeComponent();
        _customerService = new CollectionCustomerService();
    }

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
            await ToastNotification.ShowAsync("Required", "Please enter customer name or phone number to search.", NotificationType.Warning, 3000);
            return;
        }

        // Show loading
        var button = (Button)sender;
        var originalText = button.Text;
        button.Text = "Searching...";
        button.IsEnabled = false;

        try
        {
            var results = await _customerDataService.SearchForCollectionAsync(searchName, searchPhone);

            if (results.Count > 0)
            {
                SearchResultsCollection.ItemsSource = results.Select(ToCollectionCustomer).ToList();
                SearchResultsBorder.IsVisible = true;
                NoResultsLabel.IsVisible = false;
            }
            else
            {
                SearchResultsBorder.IsVisible = false;
                NoResultsLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to search customers: {ex.Message}", NotificationType.Error, 4000);
        }
        finally
        {
            button.Text = originalText;
            button.IsEnabled = true;
        }
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CollectionCustomer customer)
        {
            _selectedCustomer = customer;
            CustomerNameEntry.Text = customer.Name;
            PhoneNumberEntry.Text = customer.PhoneNumber;
            SearchResultsBorder.IsVisible = false;
        }
    }

    private void OnCustomerTapped(object sender, EventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CollectionCustomer customer)
        {
            _selectedCustomer = customer;
            CustomerNameEntry.Text = customer.Name;
            PhoneNumberEntry.Text = customer.PhoneNumber;
            SearchResultsBorder.IsVisible = false;
        }
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        // Both fields are mandatory before continuing to order
        if (string.IsNullOrWhiteSpace(name))
        {
            await ToastNotification.ShowAsync("Required", "Customer Name is required to continue.", NotificationType.Warning, 3000);
            CustomerNameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            await ToastNotification.ShowAsync("Required", "Phone Number is required to continue.", NotificationType.Warning, 3000);
            PhoneNumberEntry.Focus();
            return;
        }

        try
        {
            // Save or get existing customer
            var customer = await _customerService.SaveCustomerAsync(name, phone);

            if (customer == null)
            {
                await ToastNotification.ShowAsync("Error", "Failed to save customer information.", NotificationType.Error, 4000);
                return;
            }

            try
            {
                await _customerDataService.UpsertCollectionCustomerAsync(name, phone);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectionCustomerModal] Customer Data save: {ex.Message}");
            }

            // Navigate to order placement page with customer info
            var orderPlacementPage = new OrderPlacementPageSimple("COL", 1, "Staff", 1);
            
            // Pass customer info to order placement page
            orderPlacementPage.SetCollectionOrderInfo(customer.Id, customer.Name, customer.PhoneNumber);

            await Navigation.PushAsync(orderPlacementPage);
        }
        catch (Exception ex)
        {
            await ToastNotification.ShowAsync("Error", $"Failed to proceed: {ex.Message}", NotificationType.Error, 4000);
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        var roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        var dashboardRoute = roleAccessService.ResolveDashboardRoute(authService.CurrentUser?.Role);
        await Shell.Current.GoToAsync($"//{dashboardRoute}");
    }

    private static CollectionCustomer ToCollectionCustomer(CustomerDataRecord record)
    {
        return new CollectionCustomer
        {
            Id = record.Id,
            Name = record.Name,
            PhoneNumber = record.PhoneNumber,
            CreatedAt = record.CreatedAt,
            LastOrderDate = record.LastOrderDate
        };
    }
}
