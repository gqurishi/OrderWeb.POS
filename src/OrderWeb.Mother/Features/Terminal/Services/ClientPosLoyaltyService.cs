using OrderWeb.Contracts.Dtos;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-facing loyalty operations for Client POS.
/// Cloud remains the points source of truth; this only wraps LoyaltyService via Mother.
/// </summary>
public sealed class ClientPosLoyaltyService
{
    private readonly LoyaltyService _loyalty;

    public ClientPosLoyaltyService(LoyaltyService loyalty)
    {
        _loyalty = loyalty;
    }

    public static ClientPosLoyaltyService? TryResolve()
    {
        var loyalty = ServiceHelper.GetService<LoyaltyService>();
        return loyalty == null ? null : new ClientPosLoyaltyService(loyalty);
    }

    public Task<LoyaltyLookupResponse> SearchAsync(string lookup) =>
        _loyalty.SearchCustomerAsync(lookup);

    public Task<LoyaltyLookupResponse> CreateCustomerAsync(string phone, string name, string? email) =>
        _loyalty.CreateCustomerAsync(phone, name, email);

    public Task<LoyaltyLookupResponse> AddPointsAsync(
        string lookup,
        int points,
        string? reason,
        string? idempotencyKey) =>
        _loyalty.AddPointsAsync(
            lookup,
            points,
            string.IsNullOrWhiteSpace(reason) ? "Client POS add points" : reason.Trim(),
            idempotencyKey);

    public Task<LoyaltyLookupResponse> RedeemPointsAsync(
        string lookup,
        int points,
        string? reason,
        string? idempotencyKey) =>
        _loyalty.RedeemPointsAsync(
            lookup,
            points,
            string.IsNullOrWhiteSpace(reason) ? "Client POS redeem points" : reason.Trim(),
            idempotencyKey);

    public Task<LoyaltyLookupResponse> HistoryAsync(string lookup) =>
        _loyalty.SearchCustomerAsync(lookup);

    public async Task<(bool Success, string Message, string? ErrorCode)> TestConnectionAsync(string? lookup)
    {
        await _loyalty.ReinitializeAsync();
        if (string.IsNullOrWhiteSpace(lookup))
        {
            // Configuration probe: empty search should return a validation error if cloud config is loaded,
            // or a configuration/mother-only error if cloud is not ready.
            var probe = await _loyalty.SearchCustomerAsync(" ");
            if (probe.Error?.Contains("mother/master", StringComparison.OrdinalIgnoreCase) == true ||
                probe.Error?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true ||
                probe.Error?.Contains("Cloud Settings", StringComparison.OrdinalIgnoreCase) == true)
            {
                return (false, probe.Error, LoyaltyErrorCodes.CloudDown);
            }

            return (true, "Mother loyalty service is reachable. Enter a phone or card to fully test OrderWeb lookup.", null);
        }

        var result = await _loyalty.SearchCustomerAsync(lookup);
        if (result.Success && result.Customer != null)
        {
            return (true, result.Message ?? $"Loyalty lookup OK. Points: {result.Customer.PointsBalance}.", null);
        }

        // Not-found still proves connectivity to OrderWeb.
        if (string.Equals(result.Error, "Customer not found", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "OrderWeb loyalty API reachable (customer not found for that lookup).", null);
        }

        var code = ClassifyError(result);
        return (false, result.Error ?? result.Message ?? "Loyalty connection test failed.", code);
    }

    public static ClientLoyaltyLookupResponseDto ToLookupDto(LoyaltyLookupResponse result, bool includeHistory = false)
    {
        var customer = result.Customer;
        IReadOnlyList<ClientLoyaltyHistoryItemDto>? history = null;
        if (includeHistory)
        {
            var transactions = result.Transactions?.Count > 0
                ? result.Transactions
                : result.RecentTransactions;
            history = transactions == null || transactions.Count == 0
                ? Array.Empty<ClientLoyaltyHistoryItemDto>()
                : transactions.Select(ToHistoryDto).ToArray();
        }

        return new ClientLoyaltyLookupResponseDto(
            Success: result.Success,
            CustomerExists: result.CustomerExists || customer != null,
            Message: result.Message,
            Error: result.Success ? null : result.Error ?? result.Message,
            ErrorCode: result.Success ? null : ClassifyError(result),
            Customer: customer == null ? null : ToCustomerDto(customer),
            PointsBalance: customer?.PointsBalance ?? result.Loyalty?.PointsBalance,
            History: history);
    }

    public static ClientLoyaltyCustomerDto ToCustomerDto(LoyaltyCustomer customer) =>
        new(
            Id: customer.Id,
            Phone: customer.Phone,
            DisplayPhone: string.IsNullOrWhiteSpace(customer.DisplayPhone) ? customer.Phone : customer.DisplayPhone,
            LoyaltyCardNumber: customer.LoyaltyCardNumber,
            Name: string.IsNullOrWhiteSpace(customer.CustomerName)
                ? (customer.Name ?? string.Empty)
                : customer.CustomerName,
            Email: customer.Email,
            PointsBalance: customer.PointsBalance,
            TotalPointsEarned: customer.TotalPointsEarned,
            TotalPointsRedeemed: customer.TotalPointsRedeemed,
            TierLevel: customer.TierLevel);

    public static ClientLoyaltyHistoryItemDto ToHistoryDto(LoyaltyTransaction tx) =>
        new(
            Id: tx.Id,
            PointsChange: tx.PointsChange,
            TransactionType: tx.TransactionType,
            Description: string.IsNullOrWhiteSpace(tx.Description) ? tx.Reason : tx.Description,
            CreatedAt: tx.CreatedAt,
            OrderValue: tx.OrderValue);

    public static string ClassifyError(LoyaltyLookupResponse result)
    {
        var text = $"{result.Error} {result.Message}".ToLowerInvariant();

        if (text.Contains("queued") && (text.Contains("retry") || text.Contains("mother")))
        {
            return LoyaltyErrorCodes.Queued;
        }

        if (text.Contains("mother/master") ||
            text.Contains("not configured") ||
            text.Contains("cloud settings") ||
            text.Contains("orderweb") ||
            text.Contains("cloud"))
        {
            return LoyaltyErrorCodes.CloudDown;
        }

        if (text.Contains("not found") || text.Contains("no customer"))
        {
            return LoyaltyErrorCodes.CustomerNotFound;
        }

        if (text.Contains("insufficient") || text.Contains("not enough") || text.Contains("exceed"))
        {
            return LoyaltyErrorCodes.InsufficientPoints;
        }

        if (text.Contains("already") && (text.Contains("added") || text.Contains("earn")))
        {
            return LoyaltyErrorCodes.AlreadyEarned;
        }

        if (text.Contains("enter a") ||
            text.Contains("required") ||
            text.Contains("valid") ||
            text.Contains("phone number or loyalty"))
        {
            return LoyaltyErrorCodes.Validation;
        }

        if (text.Contains("denied") || text.Contains("not allowed") || text.Contains("forbidden"))
        {
            return LoyaltyErrorCodes.AccessDenied;
        }

        return LoyaltyErrorCodes.Unknown;
    }
}
