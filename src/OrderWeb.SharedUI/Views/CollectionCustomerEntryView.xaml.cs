using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Host-neutral Collection customer entry screen (Mother CollectionCustomerModal / Client CollectionOrderPage
/// parity). Does not call HTTP/DB — the host wires <see cref="SearchRequested"/> / <see cref="ContinueRequested"/>
/// to its own customer service and feeds results back via <see cref="SetSearchResults"/>.
/// </summary>
public partial class CollectionCustomerEntryView : ContentView
{
    private CustomerEntrySearchItem? _selectedCustomer;
    private bool _isOpeningKeyboard;

    public CollectionCustomerEntryView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the host should search for an existing customer by (name, phone).</summary>
    public event EventHandler<(string? Name, string? Phone)>? SearchRequested;

    /// <summary>Raised when the form is valid and the host should save the customer and open the order.</summary>
    public event EventHandler<CustomerEntryResult>? ContinueRequested;

    /// <summary>Raised when the user cancels out of this screen.</summary>
    public event EventHandler? CancelRequested;

    public string GetName() => CustomerNameEntry.Text?.Trim() ?? string.Empty;

    public string GetPhone() => PhoneNumberEntry.Text?.Trim() ?? string.Empty;

    public void SetSearchResults(IEnumerable<CustomerEntrySearchItem> results)
    {
        var list = results?.ToList() ?? new List<CustomerEntrySearchItem>();
        SearchResultsCollection.ItemsSource = list;
        SearchResultsBorder.IsVisible = list.Count > 0;
        NoResultsLabel.IsVisible = list.Count == 0;
    }

    public void ShowStatus(string message, string colorHex)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(colorHex);
        StatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    public void SetSearchBusy(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        SearchButton.Text = busy ? "Searching..." : "Search Existing Customer";
    }

    public void SetContinueBusy(bool busy, string? text = null)
    {
        ContinueButton.IsEnabled = !busy;
        ContinueButton.Text = busy ? (text ?? "Opening order...") : "Continue to Order";
    }

    public void ApplyCustomer(CustomerEntrySearchItem item)
    {
        _selectedCustomer = item;
        CustomerNameEntry.Text = item.Name;
        PhoneNumberEntry.Text = item.Phone;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ShowStatus("Existing customer selected.", "#10B981");
    }

    private async void OnCustomerNameFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CustomerNameEntry, "Customer name", numericOnly: false);

    private async void OnPhoneNumberFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PhoneNumberEntry, "Phone number", numericOnly: true);

    private async Task OpenKeyboardForEntryAsync(Entry entry, string title, bool numericOnly)
    {
        if (_isOpeningKeyboard)
        {
            return;
        }

        _isOpeningKeyboard = true;
        try
        {
            entry.Unfocus();
            var hostPage = FindHostPage();
            if (hostPage is null)
            {
                return;
            }

            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetNumericOnly(numericOnly);
            keyboard.SetPrompt(title, "DONE");
            keyboard.SetInitialText(entry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync(hostPage);
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

    private ContentPage? FindHostPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is ContentPage page)
            {
                return page;
            }

            current = current.Parent;
        }

        return null;
    }

    private void OnSearchClicked(object? sender, EventArgs e) =>
        SearchRequested?.Invoke(this, (GetName(), GetPhone()));

    private void OnCustomerSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CustomerEntrySearchItem item)
        {
            ApplyCustomer(item);
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void OnCustomerTapped(object? sender, EventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CustomerEntrySearchItem item)
        {
            ApplyCustomer(item);
        }
    }

    private void OnContinueClicked(object? sender, EventArgs e)
    {
        var name = GetName();
        var phone = GetPhone();

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

        ContinueRequested?.Invoke(this, new CustomerEntryResult(name, phone, _selectedCustomer?.MotherId));
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}
