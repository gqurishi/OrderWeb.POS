using System.Diagnostics;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Phase 2 print reliability: when a printer goes offline → online (paper change feels like this),
/// auto-flush waiting jobs for that printer; promote kitchen queued → printed on JobCompleted.
/// </summary>
public sealed class PrintReliabilityCoordinator
{
    private readonly PrinterHealthService _healthService;
    private readonly NetworkPrintQueueService _queueService;
    private readonly DatabaseService _databaseService;
    private bool _started;

    public event EventHandler<PrintReliabilityToastEventArgs>? ToastRequested;

    public PrintReliabilityCoordinator(
        PrinterHealthService healthService,
        NetworkPrintQueueService queueService,
        DatabaseService databaseService)
    {
        _healthService = healthService;
        _queueService = queueService;
        _databaseService = databaseService;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _healthService.PrinterStatusChanged += OnPrinterStatusChanged;
        _queueService.JobCompleted += OnJobCompleted;
        _queueService.JobFailed += OnJobFailed;
        // 30s probe so paper-change → online is caught without waiting for the slower sync job alone.
        _healthService.Start();
        Debug.WriteLine("[Print reliability] Coordinator started (ready-flush + kitchen lifecycle)");
    }

    private void OnPrinterStatusChanged(object? sender, PrinterStatusChangedEventArgs e)
    {
        if (e.WasOnline || !e.IsNowOnline || e.Printer == null || e.Printer.Id <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var flushed = await _queueService.FlushWaitingJobsForPrinterAsync(e.Printer.Id);
                if (flushed > 0)
                {
                    var ticketWord = flushed == 1 ? "ticket" : "tickets";
                    var message = $"Printer ready — retrying {flushed} failed {ticketWord}…";
                    AppDiagnostics.Log($"[Print reliability] {message} ({e.Printer.Name})");
                    ToastRequested?.Invoke(this, new PrintReliabilityToastEventArgs
                    {
                        Message = message,
                        PrinterName = e.Printer.Name,
                        JobCount = flushed
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Print reliability] Ready-flush error: {ex.Message}");
            }
        });
    }

    private void OnJobCompleted(object? sender, PrintJobCompletedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.OrderId))
        {
            return;
        }

        if (!PrintJobLifecycle.IsKitchenQueueJob(e.JobType)
            && !PrintJobLifecycle.IsReceiptQueueJob(e.JobType))
        {
            return;
        }

        // Online order ACK stays in OnlineOrderAutoPrintService (waits for both jobs).
        if (string.Equals(e.JobType, "online_receipt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.JobType, "takeaway_ticket", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!PrintJobLifecycle.IsKitchenQueueJob(e.JobType))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PromoteKitchenQueuedToPrintedAsync(e.OrderId!);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Print reliability] Promote printed failed: {ex.Message}");
            }
        });
    }

    private void OnJobFailed(object? sender, PrintJobFailedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.OrderId) || !PrintJobLifecycle.IsKitchenQueueJob(e.JobType))
        {
            return;
        }

        if (string.Equals(e.JobType, "takeaway_ticket", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await MarkKitchenQueuedNeedsAttentionAsync(e.OrderId!, e.ErrorMessage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Print reliability] Mark needs_attention failed: {ex.Message}");
            }
        });
    }

    private async Task PromoteKitchenQueuedToPrintedAsync(string orderId)
    {
        // Multi-route kitchen: wait until no other kitchen jobs for this order are still waiting.
        if (await HasWaitingKitchenQueueJobsAsync(orderId))
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        var now = DateTime.Now;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_kitchen_revision_items i
                INNER JOIN order_kitchen_revisions r ON r.id = i.revision_id
                INNER JOIN orders o ON o.id = r.order_id
                SET i.print_status = @printed,
                    i.printed_at = @now,
                    i.failure_reason = NULL
                WHERE i.print_status IN (@queued, @needsAttention)
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)";
            command.Parameters.AddWithValue("@printed", PrintJobLifecycle.Printed);
            command.Parameters.AddWithValue("@queued", PrintJobLifecycle.Queued);
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_kitchen_revisions r
                INNER JOIN orders o ON o.id = r.order_id
                SET r.status = @printed,
                    r.completed_at = @now,
                    r.failure_reason = NULL
                WHERE r.status IN (@queued, @needsAttention, 'partial')
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)
                  AND NOT EXISTS (
                      SELECT 1 FROM order_kitchen_revision_items i
                      WHERE i.revision_id = r.id AND i.print_status <> @printed
                  )";
            command.Parameters.AddWithValue("@printed", PrintJobLifecycle.Printed);
            command.Parameters.AddWithValue("@queued", PrintJobLifecycle.Queued);
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_item_send_tracking t
                INNER JOIN orders o ON o.id = t.order_id
                SET t.send_status = 'printed',
                    t.printed_at = @now,
                    t.failure_reason = NULL,
                    t.updated_at = @now
                WHERE t.send_status IN ('queued', @needsAttention)
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)";
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<bool> HasWaitingKitchenQueueJobsAsync(string orderId)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM network_print_queue
            WHERE order_id = @orderId
              AND status IN ('pending', 'printing', 'failed', 'needs_attention')
              AND LOWER(job_type) IN ('kitchen', 'bar', 'kitchen_takeaway')";
        command.Parameters.AddWithValue("@orderId", orderId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private async Task MarkKitchenQueuedNeedsAttentionAsync(string orderId, string? error)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        var reason = string.IsNullOrWhiteSpace(error) ? "Print needs attention" : error;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_kitchen_revision_items i
                INNER JOIN order_kitchen_revisions r ON r.id = i.revision_id
                INNER JOIN orders o ON o.id = r.order_id
                SET i.print_status = @needsAttention,
                    i.failure_reason = @reason
                WHERE i.print_status = @queued
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)";
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@queued", PrintJobLifecycle.Queued);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_kitchen_revisions r
                INNER JOIN orders o ON o.id = r.order_id
                SET r.status = @needsAttention,
                    r.failure_reason = @reason
                WHERE r.status IN (@queued, 'partial')
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)";
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@queued", PrintJobLifecycle.Queued);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE order_item_send_tracking t
                INNER JOIN orders o ON o.id = t.order_id
                SET t.send_status = @needsAttention,
                    t.failure_reason = @reason,
                    t.updated_at = NOW()
                WHERE t.send_status = 'queued'
                  AND (o.order_id = @orderId OR CAST(o.id AS CHAR) = @orderId)";
            command.Parameters.AddWithValue("@needsAttention", PrintJobLifecycle.NeedsAttention);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PrintReliabilityToastEventArgs : EventArgs
{
    public string Message { get; init; } = "";
    public string PrinterName { get; init; } = "";
    public int JobCount { get; init; }
}
