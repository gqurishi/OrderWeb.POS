using OrderWeb.Client.Models;

namespace OrderWeb.Client.Views.Customers;

public partial class CustomerFormView : ContentView
{
    public CustomerFormView()
    {
        InitializeComponent();
    }

    public event EventHandler<CustomerOrderDraft>? ContinueRequested;

    public void ApplyCustomer(CachedCustomer customer)
    {
        NameEntry.Text = customer.Name;
        PhoneEntry.Text = customer.Phone;
    }

    private void OnContinueClicked(object sender, EventArgs e)
    {
        var customer = new CachedCustomer(
            0,
            string.Empty,
            NameEntry.Text?.Trim() ?? string.Empty,
            PhoneEntry.Text?.Trim() ?? string.Empty,
            null,
            string.Empty,
            null,
            0);

        ContinueRequested?.Invoke(this, new CustomerOrderDraft(
            "Collection",
            customer,
            PickupTimeEntry.Text?.Trim(),
            null,
            NotesEditor.Text?.Trim(),
            null,
            null,
            null,
            0m));
    }
}
