using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

public sealed class MotherOnlineOrderClient
{
    public async Task<CachedOnlineOrder> UpdateStatusAsync(CachedOnlineOrder order, string status, LoginSession? session)
    {
        await Task.Delay(160);
        return order with
        {
            Status = status,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
    }

}
