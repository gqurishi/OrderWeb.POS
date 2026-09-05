using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Customers;
using SharedAssignCustomerView = OrderWeb.SharedUI.Views.AssignCustomerView;

namespace OrderWeb.Client.Views.Customers;

/// <summary>Thin Client host for SharedUI assign-customer flow.</summary>
public partial class CustomerFormView : ContentView
{
    private readonly SharedAssignCustomerView _sharedView = new();

    public CustomerFormView()
    {
        Content = _sharedView;
        _sharedView.SearchRequested += async (_, request) =>
        {
            var result = await ClientServiceProvider.CustomerDirectory.SearchCustomersAsync(request);
            if (result.IsSuccess && result.Value is not null)
            {
                _sharedView.ApplySearch(result.Value);
            }
        };
        _sharedView.CustomerSelected += async (_, customer) =>
        {
            var detail = await ClientServiceProvider.CustomerDirectory.GetCustomerAsync(customer.Id);
            if (detail.IsSuccess && detail.Value is not null)
            {
                _sharedView.ApplyDetail(detail.Value);
            }

            var history = await ClientServiceProvider.PreviousOrders.GetPreviousOrdersAsync(customer.Id);
            if (history.IsSuccess && history.Value is not null)
            {
                _sharedView.ApplyPreviousOrders(history.Value);
            }
        };
        _sharedView.AssignRequested += (_, customer) => AssignRequested?.Invoke(this, customer);
    }

    public event EventHandler<CustomerSummaryDto>? AssignRequested;

    public void ApplyCustomer(CachedCustomer customer)
    {
        var summary = new CustomerSummaryDto(
            customer.Id.ToString(),
            customer.MotherId,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.Address,
            null,
            null,
            customer.Postcode,
            customer.LoyaltyPoints,
            CustomerOrderKind.Both,
            null);

        _sharedView.ApplyDetail(new CustomerDetailDto(
            summary,
            null,
            null,
            null,
            null,
            ClientServiceProvider.FieldPolicy.GetPolicy().DetailFields,
            ClientServiceProvider.FieldPolicy.GetPolicy(),
            new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live")));
    }
}
