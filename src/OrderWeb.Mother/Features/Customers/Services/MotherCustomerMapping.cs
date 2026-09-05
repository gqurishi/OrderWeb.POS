using OrderWeb.Contracts.Customers;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

internal static class MotherCustomerMapping
{
    public static CustomerSummaryDto ToSummary(
        CustomerDataRecord record,
        CustomerFieldAccessPolicy policy,
        CustomerFieldAccessScope scope = CustomerFieldAccessScope.Search)
    {
        var orderKind = record.OrderTypes switch
        {
            "delivery" => CustomerOrderKind.Delivery,
            "both" => CustomerOrderKind.Both,
            _ => CustomerOrderKind.Collection
        };

        var raw = new CustomerSummaryDto(
            Id: record.Id.ToString(),
            MotherId: string.IsNullOrWhiteSpace(record.CloudCustomerId) ? null : record.CloudCustomerId,
            Name: record.Name,
            Phone: record.PhoneNumber,
            Email: null,
            Address: string.IsNullOrWhiteSpace(record.FullAddress) ? null : record.FullAddress.Replace('\n', ' ').Trim(),
            City: string.IsNullOrWhiteSpace(record.City) ? null : record.City,
            County: string.IsNullOrWhiteSpace(record.County) ? null : record.County,
            Postcode: string.IsNullOrWhiteSpace(record.Postcode) ? null : record.Postcode,
            LoyaltyPoints: record.PointsBalance,
            OrderKind: orderKind,
            DisplayLine: record.OneLineSummary);

        return CustomerFieldProjector.Project(raw, policy, scope);
    }

    public static CustomerSyncStatusDto ToSyncStatus(CustomerSyncSummary summary)
    {
        var isStale = summary.FailedCount > 0 || summary.PendingCount > 0 || summary.QueueCount > 0;
        var parts = new List<string>();
        if (summary.FailedCount > 0)
        {
            parts.Add($"{summary.FailedCount} sync failed");
        }

        if (summary.PendingCount > 0)
        {
            parts.Add($"{summary.PendingCount} pending");
        }

        if (summary.QueueCount > 0)
        {
            parts.Add($"{summary.QueueCount} queued");
        }

        var display = parts.Count == 0
            ? "Customer data live"
            : string.Join(" · ", parts);

        return new CustomerSyncStatusDto(
            IsOnline: true,
            IsStale: isStale,
            LastSyncedAtUtc: summary.LastCloudSyncAt.HasValue
                ? new DateTimeOffset(summary.LastCloudSyncAt.Value.ToUniversalTime())
                : null,
            DisplayText: display);
    }

    public static CustomerSearchResultDto BuildSearchResult(
        IReadOnlyList<CustomerSummaryDto> customers,
        CustomerFieldAccessPolicy policy,
        CustomerSyncStatusDto sync,
        bool isLoading = false,
        string? loadingMessage = null) =>
        new(
            Customers: customers,
            FieldPolicy: policy,
            SyncStatus: sync,
            IsLoading: isLoading,
            LoadingMessage: loadingMessage);

    public static CustomerOrderKind ToOrderKind(CustomerSearchRequestDto request) =>
        request.OrderKind ?? CustomerOrderKind.Both;
}
