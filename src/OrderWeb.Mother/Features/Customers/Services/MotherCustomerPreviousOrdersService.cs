using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace POS_in_NET.Services;

public sealed class MotherCustomerPreviousOrdersService : ICustomerPreviousOrdersService
{
    private readonly OrderService _orders;
    private readonly CustomerDataService _customerData;

    public MotherCustomerPreviousOrdersService(OrderService orders, CustomerDataService customerData)
    {
        _orders = orders;
        _customerData = customerData;
    }

    public async Task<OperationResult<CustomerPreviousOrdersDto>> GetPreviousOrdersAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? phone = null;
            string? display = null;
            if (int.TryParse(customerId, out var id))
            {
                var record = await _customerData.GetByIdAsync(id);
                phone = record?.PhoneNumber;
                display = record?.Name;
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                return OperationResult<CustomerPreviousOrdersDto>.Ok(new CustomerPreviousOrdersDto(
                    customerId,
                    display,
                    Array.Empty<CustomerPreviousOrderDto>(),
                    CanAccessHistory: true,
                    SyncStatus: new CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live")));
            }

            var previous = await _orders.GetPreviousCustomerOrdersAsync(phone);
            var mapped = previous
                .Select(order => new CustomerPreviousOrderDto(
                    OrderId: order.OrderDatabaseId.ToString(),
                    OrderReferenceDisplay: order.OrderReferenceDisplay,
                    DateDisplay: order.DateDisplay,
                    ChannelLabel: order.OrderTypeDisplay,
                    TotalDisplay: order.TotalDisplay,
                    StatusDisplay: order.StatusDisplay,
                    ItemsText: order.ItemsText,
                    NotesDisplay: order.OrderNotesDisplay))
                .ToList();

            var sync = await BuildSyncStatusAsync();
            return OperationResult<CustomerPreviousOrdersDto>.Ok(new CustomerPreviousOrdersDto(
                customerId,
                display,
                mapped,
                CanAccessHistory: true,
                SyncStatus: sync));
        }
        catch (Exception ex)
        {
            return OperationResult<CustomerPreviousOrdersDto>.Fail(
                OperationError.Failure("Could not load previous orders.", ex.Message));
        }
    }

    private async Task<CustomerSyncStatusDto> BuildSyncStatusAsync()
    {
        var summary = await _customerData.GetSyncSummaryAsync();
        return MotherCustomerMapping.ToSyncStatus(summary);
    }
}
