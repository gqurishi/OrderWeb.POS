using System.Text.Json;

namespace POS_in_NET.Services;

/// <summary>
/// Routes OrderWeb webhook POSTs to orders or reservations handlers (same URL for both).
/// </summary>
public sealed class OrderWebWebhookRouterService
{
    private readonly CloudOrderService _cloudOrderService;
    private readonly ReservationSyncService _reservationSyncService;

    public OrderWebWebhookRouterService(
        CloudOrderService cloudOrderService,
        ReservationSyncService reservationSyncService)
    {
        _cloudOrderService = cloudOrderService;
        _reservationSyncService = reservationSyncService;
    }

    public async Task<(bool Success, string Message)> ProcessWebhookAsync(string body, string? headerEventType = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, "Empty webhook body.");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var eventType = headerEventType;
            if (string.IsNullOrWhiteSpace(eventType) && root.TryGetProperty("event", out var eventElement))
            {
                eventType = eventElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return (false, "Missing event type.");
            }

            return eventType.ToLowerInvariant() switch
            {
                "order_created" => await _cloudOrderService.ProcessWebhookOrderAsync(root),
                "reservation_created" or "reservation_updated" =>
                    await _reservationSyncService.ProcessWebhookPayloadAsync(body, eventType),
                _ => (true, $"Ignored event {eventType}.")
            };
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("OrderWebWebhook", ex);
            return (false, ex.Message);
        }
    }
}
