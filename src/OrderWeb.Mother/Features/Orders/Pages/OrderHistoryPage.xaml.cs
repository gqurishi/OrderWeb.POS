using Microsoft.Maui.Controls;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace POS_in_NET.Pages
{
    public partial class OrderHistoryPage : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private readonly CloudOrderService? _cloudOrderService;
        private const int PageSize = 20;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private CancellationTokenSource? _loadCts;
        private bool _subscribedToChanges;
        
        private DateTime _selectedDate;
        private string _selectedOrderType = "ALL"; // ALL, COL, DEL, TBL, WEB
        private string _searchQuery = string.Empty;
        private int _pageNumber = 1;

        public OrderHistoryPage() : this(false)
        {
        }

        protected OrderHistoryPage(bool openWebTab)
        {
            InitializeComponent();
            
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _cloudOrderService = ServiceHelper.GetService<CloudOrderService>();
            _selectedDate = DateTime.Today;
            if (openWebTab)
            {
                _selectedOrderType = "WEB";
            }
            
            TopBar.SetPageTitle("Order History");
            WireBoard();
            UpdateTabSelection();
            UpdateDateDisplay();
            UpdatePagination(false);
        }

        private void WireBoard()
        {
            Board.DateFilterTapped += async (_, _) => await PickSharedDateAsync();
            Board.SearchTapped += async (_, _) => await PickSharedSearchAsync();
            Board.ClearSearchRequested += (_, _) => OnClearSearchClicked(Board, EventArgs.Empty);
            Board.FilterChanged += (_, e) =>
            {
                _selectedOrderType = OrderHistoryFilterCodes.ToApiCode(e.Filter);
                ResetPageAndLoad();
            };
            Board.PreviousPageRequested += (_, _) => OnPreviousPageClicked(Board, EventArgs.Empty);
            Board.NextPageRequested += (_, _) => OnNextPageClicked(Board, EventArgs.Empty);
            Board.BackRequested += (_, _) => OnCancelClicked(Board, EventArgs.Empty);
            Board.ViewOrderRequested += async (_, e) =>
            {
                if (e.Row.Tag is not OrderHistoryItem order)
                {
                    return;
                }

                var detailsModal = order.CloudOrder != null
                    ? new OrderDetailsModal(order.CloudOrder)
                    : new OrderDetailsModal(order.Id);
                await Navigation.PushModalAsync(detailsModal);
            };
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_subscribedToChanges)
            {
                AppDataRefreshService.DataChanged += OnAppDataChanged;
                _subscribedToChanges = true;
            }
            _ = LoadOrdersSafeAsync();
        }

        protected override void OnDisappearing()
        {
            _loadCts?.Cancel();
            if (_subscribedToChanges)
            {
                AppDataRefreshService.DataChanged -= OnAppDataChanged;
                _subscribedToChanges = false;
            }
            base.OnDisappearing();
        }

        private void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (!e.HasKind(AppDataChangeKind.Orders)) return;
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private async Task LoadOrdersSafeAsync()
        {
            var nextCts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _loadCts, nextCts);
            previousCts?.Cancel();

            try
            {
                await _loadGate.WaitAsync(nextCts.Token);
                try
                {
                    await LoadOrdersAsync(nextCts.Token);
                }
                finally
                {
                    _loadGate.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order history load failed: {ex.Message}");
                AppDiagnostics.LogFatal("Order History load", ex);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await AppAlertService.ShowAlertAsync(
                        "Order History",
                        "Order history could not be loaded. Please try again. If it continues, check the database connection.");
                });
            }
            finally
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, nextCts), nextCts))
                    nextCts.Dispose();
            }
        }

        private void UpdateDateDisplay()
        {
            Board.SetDateDisplay(_selectedDate);
        }

        private async Task LoadOrdersAsync(CancellationToken cancellationToken)
        {
            var performance = PosPerformanceMonitor.BeginDataLoad("Order History");
            var completedRows = new List<OrderHistoryItem>(PageSize);
            var voidedRows = new List<OrderHistoryItem>(PageSize);
            using var connection = await _databaseService.GetConnectionAsync();
            var hasPaymentStatus = await HasColumnAsync(connection, "orders", "payment_status", cancellationToken);
            var paymentStatusProjection = OrderHistoryQueryCompatibility.PaymentStatusProjection(hasPaymentStatus);

            // Keep this query compatible with every MariaDB version supported by
            // the POS. Window functions/CTEs caused a permanent syntax failure on
            // older restaurant installations, so pagination uses LIMIT/OFFSET.
            var query = $@"
                SELECT o.id, o.order_id, o.order_number, o.order_type, o.total_amount,
                       o.created_at, o.status, o.customer_name, o.customer_phone,
                       o.payment_method, {paymentStatusProjection} AS payment_status, o.source_channel,
                       CASE WHEN o.local_lifecycle_state = 'voided' OR o.status IN ('void', 'cancelled')
                            THEN 'voided' ELSE 'completed' END AS history_group
                FROM orders o
                WHERE 1 = 1
                      {BuildSourceFilter()}
                      {BuildDateFilter()}
                      {BuildOrderTypeFilter()}
                      {BuildSearchFilter()}
                      {BuildLifecycleFilter()}
                ORDER BY o.created_at DESC, o.id DESC
                LIMIT @pageLimit OFFSET @offset";

            using var command = new MySqlCommand(query, connection);
            ApplyQueryParameters(command);
            var offset = (_pageNumber - 1) * PageSize;
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@pageLimit", PageSize + 1);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var orderType = ReadString(reader, "order_type");
                var createdAt = reader.GetDateTime("created_at");
                var row = new OrderHistoryItem
                {
                    Id = reader.GetInt32("id"),
                    OrderId = ReadString(reader, "order_id"),
                    OrderNumber = FormatOrderNumber(ReadString(reader, "order_number"), ReadString(reader, "order_id")),
                    OrderType = orderType,
                    OrderTypeDisplay = FormatOrderType(orderType),
                    TotalAmount = reader.GetDecimal("total_amount"),
                    CreatedAt = createdAt,
                    OrderDateTime = $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}",
                    Status = ReadString(reader, "status"),
                    CustomerDisplay = FormatCustomer(ReadString(reader, "customer_name"), ReadString(reader, "customer_phone")),
                    PaymentDisplay = OnlineOrderPaymentHelper.GetDisplayMethod(ReadString(reader, "payment_method")),
                    StatusDisplay = FormatStatus(ReadString(reader, "status"), ReadString(reader, "payment_status")),
                    IsWebOrder = ReadString(reader, "source_channel").Equals("web", StringComparison.OrdinalIgnoreCase)
                };

                (ReadString(reader, "history_group") == "voided" ? voidedRows : completedRows).Add(row);
            }

            await reader.DisposeAsync();

            var cloudMessage = string.Empty;
            if (_selectedOrderType == "WEB" && !string.IsNullOrWhiteSpace(_searchQuery))
            {
                if (_cloudOrderService == null)
                {
                    cloudMessage = "OrderWeb history service is unavailable.";
                }
                else
                {
                    var cloudResult = await _cloudOrderService.SearchOrderHistoryAsync(_searchQuery, cancellationToken);
                    cloudMessage = cloudResult.Message;
                    if (cloudResult.Success)
                    {
                        MergeCloudResults(completedRows, voidedRows, cloudResult.Orders);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var isCloudSearch = _selectedOrderType == "WEB" && !string.IsNullOrWhiteSpace(_searchQuery);
            var hasNextPage = !isCloudSearch && completedRows.Count + voidedRows.Count > PageSize;
            TrimToPageSize(completedRows, voidedRows);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var emptyText = string.IsNullOrWhiteSpace(_searchQuery)
                    ? "No orders found for this date"
                    : "No matching orders found";
                Board.SetRows(
                    completedRows.Select(ToPresentation).ToList(),
                    voidedRows.Select(ToPresentation).ToList(),
                    emptyText);
                UpdateSearchStatus(cloudMessage, completedRows.Count + voidedRows.Count);
                UpdatePagination(hasNextPage);
            });
            PosPerformanceMonitor.MarkDataVisible(performance);
        }

        private static OrderHistoryRowPresentation ToPresentation(OrderHistoryItem item) =>
            new(
                OrderNumber: item.OrderNumber,
                OrderDateTime: item.OrderDateTime,
                CustomerDisplay: item.CustomerDisplay,
                OrderTypeDisplay: item.OrderTypeDisplay,
                PaymentDisplay: item.PaymentDisplay,
                StatusDisplay: item.StatusDisplay,
                TotalDisplay: $"£{item.TotalAmount:F2}",
                IsVoided: false,
                Tag: item);

        private static string ReadString(MySqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static async Task<bool> HasColumnAsync(
            MySqlConnection connection,
            string tableName,
            string columnName,
            CancellationToken cancellationToken)
        {
            await using var command = new MySqlCommand(
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName
                  AND column_name = @columnName
                """,
                connection);
            command.Parameters.AddWithValue("@tableName", tableName);
            command.Parameters.AddWithValue("@columnName", columnName);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        private static string FormatOrderNumber(string? orderNumber, string? orderId)
        {
            var readableNumber = orderNumber?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(readableNumber))
            {
                return readableNumber.StartsWith('#') ? readableNumber : $"#{readableNumber}";
            }

            var legacyId = orderId?.Trim() ?? string.Empty;
            if (legacyId.Length > 10)
            {
                legacyId = legacyId[..10].ToUpperInvariant();
            }

            return string.IsNullOrWhiteSpace(legacyId) ? "#—" : $"#{legacyId}";
        }

        private static string FormatOrderType(string? orderType)
        {
            var normalized = orderType?.Trim().ToLowerInvariant() ?? string.Empty;

            return normalized switch
            {
                "pickup" or "collection" or "col" => "Collection",
                "delivery" or "del" => "Delivery",
                "table" or "tbl" or "dine_in" or "dine-in" => "Table",
                _ => "Order"
            };
        }

        private static string FormatCustomer(string? name, string? phone)
        {
            var parts = new[] { name?.Trim(), phone?.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var display = string.Join(" • ", parts);
            return string.IsNullOrWhiteSpace(display) ? "Customer not supplied" : display;
        }

        private static string FormatStatus(string? status, string? paymentStatus)
        {
            var orderStatus = string.IsNullOrWhiteSpace(status) ? "Order" : status.Replace('_', ' ');
            var payment = string.IsNullOrWhiteSpace(paymentStatus) ? string.Empty : paymentStatus.Replace('_', ' ');
            return string.IsNullOrWhiteSpace(payment)
                ? Humanize(orderStatus)
                : $"{Humanize(orderStatus)} • {Humanize(payment)}";
        }

        private static string Humanize(string value) => string.Join(" ", value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

        private string BuildSourceFilter()
        {
            return _selectedOrderType switch
            {
                "WEB" when !string.IsNullOrWhiteSpace(_searchQuery) =>
                    "AND LOWER(COALESCE(o.source_channel, '')) = 'web' AND o.created_at >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)",
                "WEB" => "AND LOWER(COALESCE(o.source_channel, '')) = 'web'",
                "COL" or "DEL" or "TBL" =>
                    "AND LOWER(COALESCE(o.source_channel, 'local')) = 'local'",
                _ => string.Empty
            };
        }

        private string BuildLifecycleFilter()
        {
            return _selectedOrderType == "WEB"
                ? string.Empty
                : @"AND (o.local_lifecycle_state IN ('paid', 'voided') OR o.status IN ('completed', 'closed', 'paid', 'void', 'cancelled'))
                    AND EXISTS (
                        SELECT 1
                        FROM order_items valid_item
                        WHERE valid_item.order_id = o.id
                          AND valid_item.quantity > 0
                          AND COALESCE(NULLIF(TRIM(valid_item.item_name), ''), '') <> ''
                    )";
        }

        private string BuildOrderTypeFilter()
        {
            return _selectedOrderType switch
            {
                "COL" => "AND LOWER(o.order_type) IN ('col', 'collection', 'pickup')",
                "DEL" => "AND LOWER(o.order_type) IN ('del', 'delivery')",
                "TBL" => "AND LOWER(o.order_type) IN ('tbl', 'table', 'dine_in', 'dine-in')",
                _ => string.Empty
            };
        }

        private string BuildDateFilter()
        {
            return string.IsNullOrWhiteSpace(_searchQuery)
                ? "AND o.created_at >= @dayStart AND o.created_at < @dayEnd"
                : string.Empty;
        }

        private string BuildSearchFilter()
        {
            return string.IsNullOrWhiteSpace(_searchQuery)
                ? string.Empty
                : "AND (o.order_id = @searchExact OR o.order_number LIKE @searchContains OR o.customer_phone LIKE @searchContains)";
        }

        private void ApplyQueryParameters(MySqlCommand command)
        {
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                command.Parameters.AddWithValue("@dayStart", _selectedDate.Date);
                command.Parameters.AddWithValue("@dayEnd", _selectedDate.Date.AddDays(1));
                return;
            }

            var search = _searchQuery.Trim().TrimStart('#');
            command.Parameters.AddWithValue("@searchExact", search);
            command.Parameters.AddWithValue("@searchContains", $"%{search}%");
        }

        private static void MergeCloudResults(
            List<OrderHistoryItem> completedRows,
            List<OrderHistoryItem> voidedRows,
            IReadOnlyList<Order> cloudOrders)
        {
            var existingKeys = new HashSet<string>(
                completedRows.Concat(voidedRows).Select(row => row.OrderId),
                StringComparer.OrdinalIgnoreCase);

            foreach (var order in cloudOrders)
            {
                var key = string.IsNullOrWhiteSpace(order.CloudOrderId) ? order.OrderId : order.CloudOrderId;
                if (!existingKeys.Add(key)) continue;

                var status = order.Status.ToString();
                var row = new OrderHistoryItem
                {
                    OrderId = key,
                    OrderNumber = FormatOrderNumber(order.OrderNumber, order.OrderId),
                    OrderType = order.OrderType ?? string.Empty,
                    OrderTypeDisplay = FormatOrderType(order.OrderType),
                    TotalAmount = order.TotalAmount,
                    CreatedAt = order.CreatedAt,
                    OrderDateTime = $"{order.CreatedAt:h:mm tt} • {order.CreatedAt:dd/MM/yyyy}",
                    Status = status,
                    StatusDisplay = FormatStatus(status, order.PaymentStatusRaw),
                    CustomerDisplay = FormatCustomer(order.CustomerName, order.CustomerPhone),
                    PaymentDisplay = OnlineOrderPaymentHelper.GetDisplayMethod(order.PaymentMethod),
                    IsWebOrder = true,
                    CloudOrder = order
                };

                if (order.Status == OrderStatus.Cancelled || order.LocalLifecycleState == LocalLifecycleState.Voided)
                    voidedRows.Add(row);
                else
                    completedRows.Add(row);
            }

            completedRows.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
            voidedRows.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        }

        private static void TrimToPageSize(
            List<OrderHistoryItem> completedRows,
            List<OrderHistoryItem> voidedRows)
        {
            while (completedRows.Count + voidedRows.Count > PageSize)
            {
                if (completedRows.Count == 0)
                {
                    voidedRows.RemoveAt(voidedRows.Count - 1);
                }
                else if (voidedRows.Count == 0)
                {
                    completedRows.RemoveAt(completedRows.Count - 1);
                }
                else if (completedRows[^1].CreatedAt <= voidedRows[^1].CreatedAt)
                {
                    completedRows.RemoveAt(completedRows.Count - 1);
                }
                else
                {
                    voidedRows.RemoveAt(voidedRows.Count - 1);
                }
            }
        }

        private void UpdateSearchStatus(string cloudMessage, int resultCount)
        {
            var searching = !string.IsNullOrWhiteSpace(_searchQuery);
            if (!searching)
            {
                Board.SetStatusBanner(false, string.Empty, string.Empty, showClear: false);
                return;
            }

            var source = _selectedOrderType == "WEB"
                ? (string.IsNullOrWhiteSpace(cloudMessage)
                    ? "Recent cache checked; checking OrderWeb history is available on the Web tab."
                    : $"Recent cache + cloud: {cloudMessage}")
                : "Local order history";
            Board.SetStatusBanner(
                visible: true,
                title: $"Search: {_searchQuery} • {resultCount} result(s)",
                detail: source,
                showClear: true);
        }

        private void UpdatePagination(bool hasNextPage)
        {
            Board.SetPaging(_pageNumber, _pageNumber > 1, hasNextPage);
        }

        private void ResetPageAndLoad()
        {
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private void UpdateTabSelection()
        {
            Board.SetFilter(OrderHistoryFilterCodes.FromApiCode(_selectedOrderType));
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            var roleAccess = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            await NavigationCoordinator.Shared.NavigateShellAsync(
                roleAccess.ResolveDashboardRoute(authService.CurrentUser?.Role),
                source: sender as VisualElement);
        }

        private async Task PickSharedDateAsync()
        {
            var picked = await OrderHistoryPickers.PickDateAsync(Navigation, _selectedDate);
            if (picked is null)
            {
                return;
            }

            _selectedDate = picked.Value.Date;
            UpdateDateDisplay();
            ResetPageAndLoad();
        }

        private async Task PickSharedSearchAsync()
        {
            var value = await OrderHistoryPickers.PickSearchAsync(this, _searchQuery);
            if (value is null)
            {
                return;
            }

            _searchQuery = value;
            UpdateSearchPlaceholder();
            ResetPageAndLoad();
        }
        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            _searchQuery = string.Empty;
            UpdateSearchPlaceholder();
            ResetPageAndLoad();
        }

        private void UpdateSearchPlaceholder()
        {
            Board.SetSearchDisplay(string.IsNullOrWhiteSpace(_searchQuery) ? null : _searchQuery);
        }

        private void OnPreviousPageClicked(object sender, EventArgs e)
        {
            if (_pageNumber <= 1) return;
            _pageNumber--;
            _ = LoadOrdersSafeAsync();
        }

        private void OnNextPageClicked(object sender, EventArgs e)
        {
            _pageNumber++;
            _ = LoadOrdersSafeAsync();
        }

    }

    // Helper class for order display
    public class OrderHistoryItem
    {
        public int Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string OrderTypeDisplay { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string OrderDateTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CustomerDisplay { get; set; } = string.Empty;
        public string PaymentDisplay { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public bool IsWebOrder { get; set; }
        public Order? CloudOrder { get; set; }
    }
}
