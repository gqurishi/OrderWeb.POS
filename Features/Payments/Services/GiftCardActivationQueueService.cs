using System.Text.Json;
using Microsoft.Maui.Storage;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class GiftCardActivationQueueService : IDisposable
{
    private const int MaxQueuedActivations = 50;
    private const string PendingActivationsKey = "gift_card_pending_activations_v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OrderWebGiftCardApiService _giftCardApiService;
    private readonly BackgroundSyncManager? _backgroundSyncManager;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private Timer? _flushTimer;

    public GiftCardActivationQueueService(
        OrderWebGiftCardApiService giftCardApiService,
        BackgroundSyncManager? backgroundSyncManager = null)
    {
        _giftCardApiService = giftCardApiService;
        _backgroundSyncManager = backgroundSyncManager;
    }

    public void StartAutoFlush()
    {
        StopAutoFlush();
        _flushTimer = new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        AppDiagnostics.Log("Gift card activation queue auto-flush started");
    }

    public void StopAutoFlush()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
    }

    public async Task<GiftCardActivationQueueResult> QueueAsync(
        GiftCardActivateRequest request,
        string transactionId,
        string? lastError = null)
    {
        await _queueLock.WaitAsync();
        try
        {
            var queued = ReadQueue();
            var normalizedTransactionId = BuildQueueTransactionId(request, transactionId);
            var existing = queued.FirstOrDefault(q =>
                string.Equals(q.TransactionId, normalizedTransactionId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Request = CloneRequest(request);
                existing.LastError = lastError;
                existing.LastUpdatedAtUtc = DateTime.UtcNow;
                SaveQueue(queued);
                _backgroundSyncManager?.RequestRunSoon("gift-card-activate-flush");

                return GiftCardActivationQueueResult.Queued(
                    queued.Count,
                    $"Activation already queued for retry ({queued.Count}/{MaxQueuedActivations}).");
            }

            if (queued.Count >= MaxQueuedActivations)
            {
                return GiftCardActivationQueueResult.Failed(
                    queued.Count,
                    $"Gift card activation queue is full ({MaxQueuedActivations}/{MaxQueuedActivations}).");
            }

            queued.Add(new QueuedGiftCardActivation
            {
                TransactionId = normalizedTransactionId,
                Request = CloneRequest(request),
                LastError = lastError,
                QueuedAtUtc = DateTime.UtcNow,
                LastUpdatedAtUtc = DateTime.UtcNow
            });

            SaveQueue(queued);

            AppDiagnostics.Log($"Gift card activation queued ({queued.Count}/{MaxQueuedActivations})");
            _backgroundSyncManager?.RequestRunSoon("gift-card-activate-flush");
            return GiftCardActivationQueueResult.Queued(
                queued.Count,
                $"Activation queued for retry ({queued.Count}/{MaxQueuedActivations}).");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card activation queue failed: {ex.Message}");
            return GiftCardActivationQueueResult.Failed(0, $"Could not queue activation: {ex.Message}");
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public async Task<GiftCardActivationFlushResult> FlushAsync()
    {
        await _queueLock.WaitAsync();
        try
        {
            var queued = ReadQueue();
            var batch = queued.Take(MaxQueuedActivations).ToList();
            if (batch.Count == 0)
            {
                return GiftCardActivationFlushResult.Completed(0, 0, "No queued gift card activations.");
            }

            var result = await _giftCardApiService.FlushActivationsAsync(
                batch.Select(q => q.Request),
                BuildFlushTransactionId(batch));

            if (!result.Success)
            {
                var error = result.Error ?? result.Message ?? "Gift card activation flush failed.";
                AppDiagnostics.Log($"Gift card activation flush failed: {error}");
                return GiftCardActivationFlushResult.Failed(queued.Count, error);
            }

            var flushedTransactionIds = batch
                .Select(q => q.TransactionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var remaining = queued
                .Where(q => !flushedTransactionIds.Contains(q.TransactionId))
                .ToList();

            SaveQueue(remaining);

            AppDiagnostics.Log($"Gift card activation flush sent {batch.Count}, pending {remaining.Count}");
            return GiftCardActivationFlushResult.Completed(
                batch.Count,
                remaining.Count,
                $"Flushed {batch.Count} queued gift card activation(s).");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card activation flush exception: {ex.Message}");
            return GiftCardActivationFlushResult.Failed(GetPendingCountUnsafe(), ex.Message);
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync()
    {
        await _queueLock.WaitAsync();
        try
        {
            return ReadQueue().Count;
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public void Dispose()
    {
        StopAutoFlush();
        _queueLock.Dispose();
    }

    private static List<QueuedGiftCardActivation> ReadQueue()
    {
        var json = Preferences.Default.Get(PendingActivationsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<QueuedGiftCardActivation>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<QueuedGiftCardActivation>>(json, JsonOptions)
                ?? new List<QueuedGiftCardActivation>();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Gift card activation queue read failed: {ex.Message}");
            return new List<QueuedGiftCardActivation>();
        }
    }

    private static void SaveQueue(List<QueuedGiftCardActivation> queued)
    {
        if (queued.Count == 0)
        {
            Preferences.Default.Remove(PendingActivationsKey);
            return;
        }

        var limited = queued.Take(MaxQueuedActivations).ToList();
        Preferences.Default.Set(PendingActivationsKey, JsonSerializer.Serialize(limited, JsonOptions));
    }

    private static int GetPendingCountUnsafe()
    {
        return ReadQueue().Count;
    }

    private static GiftCardActivateRequest CloneRequest(GiftCardActivateRequest request)
    {
        return new GiftCardActivateRequest
        {
            CardNumber = request.CardNumber?.Trim().ToUpperInvariant() ?? string.Empty,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod?.Trim() ?? string.Empty,
            OrderId = request.OrderId?.Trim(),
            TillOrderId = request.TillOrderId?.Trim(),
            Description = request.Description
        };
    }

    private static string BuildQueueTransactionId(GiftCardActivateRequest request, string transactionId)
    {
        if (!string.IsNullOrWhiteSpace(transactionId))
        {
            return transactionId.Trim();
        }

        return OrderWebApiClient.BuildIdempotencyKey(
            "gift-card-activate",
            request.CardNumber,
            request.Amount,
            request.PaymentMethod,
            request.OrderId,
            request.TillOrderId);
    }

    private static string BuildFlushTransactionId(IReadOnlyCollection<QueuedGiftCardActivation> queued)
    {
        return string.Join("|", queued.Select(q => q.TransactionId));
    }

    private sealed class QueuedGiftCardActivation
    {
        public string TransactionId { get; set; } = string.Empty;

        public GiftCardActivateRequest Request { get; set; } = new();

        public string? LastError { get; set; }

        public DateTime QueuedAtUtc { get; set; }

        public DateTime LastUpdatedAtUtc { get; set; }
    }
}

public sealed class GiftCardActivationQueueResult
{
    public bool Success { get; private init; }

    public int PendingCount { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public static GiftCardActivationQueueResult Queued(int pendingCount, string message)
    {
        return new GiftCardActivationQueueResult
        {
            Success = true,
            PendingCount = pendingCount,
            Message = message
        };
    }

    public static GiftCardActivationQueueResult Failed(int pendingCount, string message)
    {
        return new GiftCardActivationQueueResult
        {
            Success = false,
            PendingCount = pendingCount,
            Message = message
        };
    }
}

public sealed class GiftCardActivationFlushResult
{
    public bool Success { get; private init; }

    public int FlushedCount { get; private init; }

    public int PendingCount { get; private init; }

    public string Message { get; private init; } = string.Empty;

    public static GiftCardActivationFlushResult Completed(int flushedCount, int pendingCount, string message)
    {
        return new GiftCardActivationFlushResult
        {
            Success = true,
            FlushedCount = flushedCount,
            PendingCount = pendingCount,
            Message = message
        };
    }

    public static GiftCardActivationFlushResult Failed(int pendingCount, string message)
    {
        return new GiftCardActivationFlushResult
        {
            Success = false,
            PendingCount = pendingCount,
            Message = message
        };
    }
}
