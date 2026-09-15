using MySqlConnector;

namespace POS_in_NET.Services;

/// <summary>
/// Website orders that have not finished both paper copies. Stickers do not count.
/// Clears only when the receipt and the kitchen ticket have both completed.
/// </summary>
public sealed class OnlineOrderPrintNoticeService
{
    private static readonly TimeSpan Lookback = TimeSpan.FromHours(18);
    private static readonly TimeSpan NewOrderWindow = TimeSpan.FromSeconds(25);

    private readonly DatabaseService _database;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly HashSet<string> _seenOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;
    private bool _seeded;
    private Timer? _timer;

    public OnlineOrderPrintNoticeService(DatabaseService database)
    {
        _database = database;
    }

    public event EventHandler? Changed;

    public OnlineOrderPrintNoticeSnapshot Snapshot { get; private set; } = OnlineOrderPrintNoticeSnapshot.Empty;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var orders = ServiceHelper.GetService<CloudOrderService>();
        if (orders != null)
        {
            orders.OnOrdersUpdated += OnOrdersUpdated;
        }

        var queue = ServiceHelper.GetService<NetworkPrintQueueService>();
        if (queue != null)
        {
            queue.JobCompleted += OnPrintChanged;
            queue.JobFailed += OnPrintChanged;
        }

        _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        _ = RefreshAsync();
    }

    private void OnOrdersUpdated() => _ = RefreshAsync();

    private void OnPrintChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!TerminalConfigurationService.IsConfigured)
            {
                Publish(OnlineOrderPrintNoticeSnapshot.Empty, announceNew: false);
                return;
            }

            var master = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync(_database);
            if (!master.Allowed)
            {
                Publish(OnlineOrderPrintNoticeSnapshot.Empty, announceNew: false);
                return;
            }

            var waiting = await LoadWaitingAsync();
            Publish(BuildSnapshot(waiting), announceNew: _seeded);
            if (!_seeded)
            {
                foreach (var order in waiting)
                {
                    _seenOrderIds.Add(order.OrderId);
                }

                _seeded = true;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Online order print notice refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Publish(OnlineOrderPrintNoticeSnapshot snapshot, bool announceNew)
    {
        if (announceNew)
        {
            var fresh = snapshot.Orders.Any(order => _seenOrderIds.Add(order.OrderId));
            if (fresh)
            {
                PlayArrivalSound();
            }
        }

        var same = snapshot.Text == Snapshot.Text
            && snapshot.Tone == Snapshot.Tone
            && snapshot.Count == Snapshot.Count;
        Snapshot = snapshot;
        if (!same)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static OnlineOrderPrintNoticeSnapshot BuildSnapshot(IReadOnlyList<WaitingOnlineOrder> waiting)
    {
        if (waiting.Count == 0)
        {
            return OnlineOrderPrintNoticeSnapshot.Empty;
        }

        var failed = waiting.Any(order => order.Tone == OnlineOrderPrintNoticeTone.Failed);
        var reference = waiting[0].Reference;
        if (waiting.Count == 1)
        {
            var singleText = waiting[0].Tone switch
            {
                OnlineOrderPrintNoticeTone.Failed => $"Print failed #{reference}",
                OnlineOrderPrintNoticeTone.New => $"New online order #{reference}",
                _ => $"Waiting to print #{reference}"
            };
            return new OnlineOrderPrintNoticeSnapshot(waiting.Count, singleText, waiting[0].Tone, waiting);
        }

        var tone = failed ? OnlineOrderPrintNoticeTone.Failed : OnlineOrderPrintNoticeTone.Waiting;
        var text = failed
            ? $"{waiting.Count} online orders — print failed"
            : $"{waiting.Count} online orders waiting to print";
        return new OnlineOrderPrintNoticeSnapshot(waiting.Count, text, tone, waiting);
    }

    private async Task<List<WaitingOnlineOrder>> LoadWaitingAsync()
    {
        var waiting = new List<WaitingOnlineOrder>();
        await using var connection = await _database.GetConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT o.order_id,
                   COALESCE(NULLIF(o.order_number, ''), o.order_id) AS order_number,
                   o.created_at
            FROM orders o
            WHERE LOWER(COALESCE(o.source_channel, '')) = 'web'
              AND o.created_at >= @since
              AND LOWER(COALESCE(o.local_lifecycle_state, '')) NOT IN ('voided', 'cancelled')
              AND LOWER(COALESCE(o.status, '')) NOT IN ('void', 'cancelled', 'voided')
            ORDER BY o.created_at";
        command.Parameters.AddWithValue("@since", DateTime.Now.Subtract(Lookback));

        var orders = new List<(string Id, string Number, DateTime Created)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                orders.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                    reader.GetDateTime(2)));
            }
        }

        if (orders.Count == 0)
        {
            return waiting;
        }

        var jobs = await LoadTicketJobsAsync(connection, orders.Select(order => order.Id).ToList());
        var now = DateTime.Now;
        foreach (var order in orders)
        {
            jobs.TryGetValue(order.Id, out var ticket);
            if (ticket is { ReceiptCompleted: true, KitchenCompleted: true })
            {
                continue;
            }

            var tone = Classify(ticket, now - order.Created);
            waiting.Add(new WaitingOnlineOrder(order.Id, DisplayReference(order.Number), tone));
        }

        return waiting;
    }

    private static async Task<Dictionary<string, TicketPrintState>> LoadTicketJobsAsync(
        MySqlConnection connection,
        IReadOnlyList<string> orderIds)
    {
        var jobs = new Dictionary<string, TicketPrintState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var command = connection.CreateCommand();
            var names = new List<string>();
            for (var index = 0; index < orderIds.Count; index++)
            {
                var name = "@id" + index;
                names.Add(name);
                command.Parameters.AddWithValue(name, orderIds[index]);
            }

            command.CommandText = $@"
                SELECT q.order_id, q.job_type, q.status
                FROM network_print_queue q
                INNER JOIN (
                    SELECT order_id, job_type, MAX(id) AS latest_id
                    FROM network_print_queue
                    WHERE job_type IN ('online_receipt', 'takeaway_ticket')
                      AND order_id IN ({string.Join(", ", names)})
                    GROUP BY order_id, job_type
                ) latest ON latest.latest_id = q.id
                UNION ALL
                SELECT order_id, job_type, 'completed'
                FROM network_print_queue
                WHERE status = 'completed'
                  AND job_type IN ('online_receipt', 'takeaway_ticket')
                  AND order_id IN ({string.Join(", ", names)})";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var orderId = reader.GetString(0);
                if (!jobs.TryGetValue(orderId, out var state))
                {
                    state = new TicketPrintState();
                    jobs[orderId] = state;
                }

                var jobType = reader.GetString(1);
                var status = reader.GetString(2);
                var receipt = jobType.Equals("online_receipt", StringComparison.OrdinalIgnoreCase);
                if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (receipt)
                    {
                        state.ReceiptCompleted = true;
                    }
                    else
                    {
                        state.KitchenCompleted = true;
                    }
                }
                else if (receipt)
                {
                    state.ReceiptLatest = status;
                }
                else
                {
                    state.KitchenLatest = status;
                }
            }
        }
        catch (MySqlException ex) when (ex.Number == 1146)
        {
            return jobs;
        }

        return jobs;
    }

    private static OnlineOrderPrintNoticeTone Classify(TicketPrintState? ticket, TimeSpan age)
    {
        if (ticket == null)
        {
            return age <= NewOrderWindow
                ? OnlineOrderPrintNoticeTone.New
                : OnlineOrderPrintNoticeTone.Failed;
        }

        var failed = IsFailed(ticket.ReceiptLatest) || IsFailed(ticket.KitchenLatest);
        if (failed && !ticket.ReceiptCompleted && !ticket.KitchenCompleted)
        {
            return OnlineOrderPrintNoticeTone.Failed;
        }

        if (failed && (!ticket.ReceiptCompleted || !ticket.KitchenCompleted))
        {
            return OnlineOrderPrintNoticeTone.Failed;
        }

        if (ticket.ReceiptLatest == null && ticket.KitchenLatest == null && !ticket.ReceiptCompleted && !ticket.KitchenCompleted)
        {
            return age <= NewOrderWindow
                ? OnlineOrderPrintNoticeTone.New
                : OnlineOrderPrintNoticeTone.Failed;
        }

        return OnlineOrderPrintNoticeTone.Waiting;
    }

    private static bool IsFailed(string? status) =>
        status != null && status.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private static string DisplayReference(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 12 ? trimmed : trimmed[^8..];
    }

    private static void PlayArrivalSound()
    {
        try
        {
#if WINDOWS
            Console.Beep(880, 160);
#endif
        }
        catch
        {
            // A missing sound must not hide the notice.
        }
    }

    private sealed class TicketPrintState
    {
        public bool ReceiptCompleted { get; set; }
        public bool KitchenCompleted { get; set; }
        public string? ReceiptLatest { get; set; }
        public string? KitchenLatest { get; set; }
    }
}

public enum OnlineOrderPrintNoticeTone
{
    None,
    New,
    Waiting,
    Failed
}

public sealed record WaitingOnlineOrder(string OrderId, string Reference, OnlineOrderPrintNoticeTone Tone);

public sealed record OnlineOrderPrintNoticeSnapshot(
    int Count,
    string Text,
    OnlineOrderPrintNoticeTone Tone,
    IReadOnlyList<WaitingOnlineOrder> Orders)
{
    public static OnlineOrderPrintNoticeSnapshot Empty { get; } = new(0, "", OnlineOrderPrintNoticeTone.None, Array.Empty<WaitingOnlineOrder>());

    public bool IsVisible => Count > 0 && !string.IsNullOrWhiteSpace(Text);
}
