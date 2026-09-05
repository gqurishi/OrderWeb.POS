using OrderWeb.Client.Services;
using OrderWeb.Contracts.Customers;
using SharedCustomerSearchView = OrderWeb.SharedUI.Views.CustomerSearchView;

namespace OrderWeb.Client.Views.Customers;

/// <summary>Thin Client host for SharedUI customer search.</summary>
public partial class CustomerSearchView : ContentView
{
    private readonly SharedCustomerSearchView _sharedView = new();

    public CustomerSearchView()
    {
        Content = _sharedView;
        _sharedView.SearchRequested += async (_, request) =>
        {
            var result = await ClientServiceProvider.CustomerDirectory.SearchCustomersAsync(request);
            if (result.IsSuccess && result.Value is not null)
            {
                _sharedView.Apply(result.Value);
            }

            SearchRequested?.Invoke(this, request);
        };
        _sharedView.CustomerSelected += (_, customer) => CustomerSelected?.Invoke(this, customer);
    }

    public event EventHandler<CustomerSearchRequestDto>? SearchRequested;
    public event EventHandler<CustomerSummaryDto>? CustomerSelected;

    public void SetOrderKind(CustomerOrderKind? orderKind) => _sharedView.SetOrderKind(orderKind);

    public void Apply(CustomerSearchResultDto state) => _sharedView.Apply(state);
}
