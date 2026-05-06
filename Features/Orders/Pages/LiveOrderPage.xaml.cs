using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MySqlConnector;

namespace POS_in_NET.Pages
{
    public partial class LiveOrderPage : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private readonly OrderLifecycleRolloutService _rolloutService;
        private readonly TableSessionService _tableSessionService;
        private bool _lifecycleColumnsEnsured;
        private bool _isLoadingOrders;
        private bool _tableBackfillCompleted;
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();
        private ObservableCollection<Order> CollectionOrders { get; set; } = new();
        private ObservableCollection<Order> DeliveryOrders { get; set; } = new();
        private ObservableCollection<TableSession> TableSessions { get; set; } = new();

        public LiveOrderPage()
        {
            InitializeComponent();
            
            _databaseService = new DatabaseService();
            _rolloutService = ServiceHelper.GetService<OrderLifecycleRolloutService>() ?? new OrderLifecycleRolloutService(_databaseService);
            _tableSessionService = new TableSessionService();
            
            // Set page title
            TopBar.SetPageTitle("Live Order");
            
            CollectionOrdersCollection.ItemsSource = CollectionOrders;
            DeliveryOrdersCollection.ItemsSource = DeliveryOrders;
            TableOrdersCollection.ItemsSource = TableSessions;
            
            // Set initial tab selection (Table is default)
            OnTableTabClicked(this, EventArgs.Empty);
            
            LoadAllOrders();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadAllOrders();
        }

        private async void LoadAllOrders()
        {
            if (_isLoadingOrders)
            {
                return;
            }

            _isLoadingOrders = true;
            await EnsureRolloutConfigAsync();
            try
            {
                await Task.WhenAll(
                    LoadCollectionOrdersAsync(),
                    LoadDeliveryOrdersAsync(),
                    LoadTableSessionsAsync());
            }
            finally
            {
                _isLoadingOrders = false;
            }
        }

        private async Task EnsureRolloutConfigAsync()
        {
            try
            {
                _rolloutConfig = await _rolloutService.GetConfigAsync();
            }
            catch
            {
                _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();
            }
        }

        private async Task LoadCollectionOrdersAsync()
        {
            try
            {
                CollectionOrders.Clear();
                
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureOrderLifecycleSchemaAsync(connection);
                var lifecycleFilter = _rolloutConfig.EnableLifecycleReads
                    ? "AND COALESCE(LOWER(o.local_lifecycle_state), 'active') NOT IN ('paid', 'voided')"
                    : "AND LOWER(o.status) NOT IN ('completed', 'cancelled')";

                var query = $@"
                    SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                           o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                           COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                           COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                           COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
                    FROM orders o
                    WHERE LOWER(o.order_type) IN ('pickup', 'collection', 'col') 
                          AND COALESCE(o.source_channel, 'local') = 'local'
                      AND COALESCE(o.is_open, CASE WHEN LOWER(o.status) IN ('completed', 'cancelled') THEN 0 ELSE 1 END) = 1
                      {lifecycleFilter}
                    ORDER BY FIELD(COALESCE(LOWER(o.local_lifecycle_state), 'active'), 'draft', 'active', 'sent_partial', 'sent_full', 'payment_partial'),
                             o.updated_at DESC, o.created_at DESC";
                
                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var orderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber"))
                        ? reader.GetString(reader.GetOrdinal("OrderId"))
                        : reader.GetString(reader.GetOrdinal("OrderNumber"));

                    CollectionOrders.Add(new Order
                    {
                        Id = reader.GetInt32("id"),
                        OrderId = reader.IsDBNull(reader.GetOrdinal("OrderId")) ? string.Empty : reader.GetString("OrderId"),
                        OrderNumber = orderNumber,
                        CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Guest" : reader.GetString("CustomerName"),
                        TotalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal("TotalAmount"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? reader.GetDateTime("CreatedAt") : reader.GetDateTime("UpdatedAt"),
                        LocalLifecycleState = reader.IsDBNull(reader.GetOrdinal("LocalLifecycleState"))
                            ? LocalLifecycleState.Active
                            : Enum.TryParse<LocalLifecycleState>(reader.GetString("LocalLifecycleState"), true, out var lifecycleState)
                                ? lifecycleState
                                : LocalLifecycleState.Active,
                        IsOpen = !reader.IsDBNull(reader.GetOrdinal("IsOpen")) && reader.GetBoolean("IsOpen"),
                        DraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("DraftAbandonedFlag")) && reader.GetBoolean("DraftAbandonedFlag"),
                        SendAttemptCount = reader.IsDBNull(reader.GetOrdinal("SendAttemptCount")) ? 0 : reader.GetInt32("SendAttemptCount"),
                        PaymentAttemptCount = reader.IsDBNull(reader.GetOrdinal("PaymentAttemptCount")) ? 0 : reader.GetInt32("PaymentAttemptCount")
                    });
                }
                
                CollectionEmptyLabel.IsVisible = CollectionOrders.Count == 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load collection orders: {ex.Message}");
            }
        }

        private async Task LoadDeliveryOrdersAsync()
        {
            try
            {
                DeliveryOrders.Clear();
                
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureOrderLifecycleSchemaAsync(connection);
                var lifecycleFilter = _rolloutConfig.EnableLifecycleReads
                    ? "AND COALESCE(LOWER(o.local_lifecycle_state), 'active') NOT IN ('paid', 'voided')"
                    : "AND LOWER(o.status) NOT IN ('completed', 'cancelled')";

                var query = $@"
                    SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                           o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                           COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                           COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                           COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
                    FROM orders o
                    WHERE LOWER(o.order_type) IN ('delivery', 'del') 
                          AND COALESCE(o.source_channel, 'local') = 'local'
                      AND COALESCE(o.is_open, CASE WHEN LOWER(o.status) IN ('completed', 'cancelled') THEN 0 ELSE 1 END) = 1
                      {lifecycleFilter}
                    ORDER BY FIELD(COALESCE(LOWER(o.local_lifecycle_state), 'active'), 'draft', 'active', 'sent_partial', 'sent_full', 'payment_partial'),
                             o.updated_at DESC, o.created_at DESC";
                
                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var orderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber"))
                        ? reader.GetString(reader.GetOrdinal("OrderId"))
                        : reader.GetString(reader.GetOrdinal("OrderNumber"));

                    DeliveryOrders.Add(new Order
                    {
                        Id = reader.GetInt32("id"),
                        OrderId = reader.IsDBNull(reader.GetOrdinal("OrderId")) ? string.Empty : reader.GetString("OrderId"),
                        OrderNumber = orderNumber,
                        CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Guest" : reader.GetString("CustomerName"),
                        TotalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal("TotalAmount"),
                        CreatedAt = reader.GetDateTime("CreatedAt"),
                        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? reader.GetDateTime("CreatedAt") : reader.GetDateTime("UpdatedAt"),
                        LocalLifecycleState = reader.IsDBNull(reader.GetOrdinal("LocalLifecycleState"))
                            ? LocalLifecycleState.Active
                            : Enum.TryParse<LocalLifecycleState>(reader.GetString("LocalLifecycleState"), true, out var lifecycleState)
                                ? lifecycleState
                                : LocalLifecycleState.Active,
                        IsOpen = !reader.IsDBNull(reader.GetOrdinal("IsOpen")) && reader.GetBoolean("IsOpen"),
                        DraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("DraftAbandonedFlag")) && reader.GetBoolean("DraftAbandonedFlag"),
                        SendAttemptCount = reader.IsDBNull(reader.GetOrdinal("SendAttemptCount")) ? 0 : reader.GetInt32("SendAttemptCount"),
                        PaymentAttemptCount = reader.IsDBNull(reader.GetOrdinal("PaymentAttemptCount")) ? 0 : reader.GetInt32("PaymentAttemptCount")
                    });
                }
                
                DeliveryEmptyLabel.IsVisible = DeliveryOrders.Count == 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load delivery orders: {ex.Message}");
            }
        }

        private async Task EnsureOrderLifecycleSchemaAsync(MySqlConnection connection)
        {
            if (_lifecycleColumnsEnsured)
            {
                return;
            }

            const string alterSql = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
                ADD COLUMN IF NOT EXISTS is_open BOOLEAN DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS void_reason VARCHAR(255) NULL,
                ADD COLUMN IF NOT EXISTS voided_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS voided_by VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS paid_at DATETIME NULL";

            try
            {
                using var alter = new MySqlCommand(alterSql, connection);
                await alter.ExecuteNonQueryAsync();
            }
            catch
            {
                // Keep Live Order page resilient on environments where schema migration may run elsewhere.
            }

            _lifecycleColumnsEnsured = true;
        }

        private async Task LoadTableSessionsAsync()
        {
            try
            {
                TableSessions.Clear();

                if (!_tableBackfillCompleted)
                {
                    var backfillResult = await _tableSessionService.BackfillOpenTableSessionsAsync();
                    _tableBackfillCompleted = true;

                    if (backfillResult.repairedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LiveOrder] {backfillResult.message}");
                    }
                }
                
                using var connection = await _databaseService.GetConnectionAsync();
                    var query = @"
                        SELECT ts.Id, ts.TableId, ts.SessionNumber, ts.PartySize,
                          ts.StartTime, ts.Status, ts.CurrentOrderId, ts.ParentSessionId, ts.MergedIntoSessionId, rt.TableNumber,
                          o.id AS LinkedOrderDbId, o.order_id AS LinkedOrderId, o.order_number AS LinkedOrderNumber,
                          o.local_lifecycle_state AS LinkedOrderLifecycleState, o.updated_at AS LinkedOrderUpdatedAt,
                          COALESCE(o.is_open, 1) AS LinkedOrderIsOpen, COALESCE(o.draft_abandoned_flag, 0) AS LinkedOrderDraftAbandonedFlag
                    FROM TableSessions ts
                    LEFT JOIN RestaurantTables rt ON ts.TableId = rt.Id
                      LEFT JOIN orders o ON o.id = (
                          SELECT o2.id
                          FROM orders o2
                          WHERE o2.table_session_id = ts.Id
                            AND COALESCE(o2.source_channel, 'local') = 'local'
                            AND COALESCE(o2.is_open, 1) = 1
                            AND COALESCE(LOWER(o2.local_lifecycle_state), 'active') NOT IN ('paid', 'voided')
                          ORDER BY o2.updated_at DESC, o2.id DESC
                          LIMIT 1
                      )
                    WHERE ts.Status != 'Closed' AND ts.IsActive = 1
                    ORDER BY ts.StartTime DESC";
                
                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var session = new TableSession
                    {
                        Id = reader.GetInt32("Id"),
                        TableId = reader.GetInt32("TableId"),
                        SessionNumber = reader.GetString("SessionNumber"),
                        PartySize = reader.GetInt32("PartySize"),
                        StartTime = reader.GetDateTime("StartTime"),
                        Status = Enum.Parse<TableSessionStatus>(reader.GetString("Status")),
                        CurrentOrderId = reader.IsDBNull(reader.GetOrdinal("CurrentOrderId")) ? null : reader.GetString("CurrentOrderId"),
                        ParentSessionId = reader.IsDBNull(reader.GetOrdinal("ParentSessionId")) ? null : reader.GetInt32("ParentSessionId"),
                        MergedIntoSessionId = reader.IsDBNull(reader.GetOrdinal("MergedIntoSessionId")) ? null : reader.GetInt32("MergedIntoSessionId"),
                        LinkedOrderDbId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderDbId")) ? null : reader.GetInt32("LinkedOrderDbId"),
                        LinkedOrderId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderId")) ? null : reader.GetString("LinkedOrderId"),
                        LinkedOrderNumber = reader.IsDBNull(reader.GetOrdinal("LinkedOrderNumber")) ? null : reader.GetString("LinkedOrderNumber"),
                        LinkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState")) ? null : reader.GetString("LinkedOrderLifecycleState"),
                        LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime("LinkedOrderUpdatedAt"),
                        LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean("LinkedOrderIsOpen"),
                        LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean("LinkedOrderDraftAbandonedFlag")
                    };
                    
                    // Create a navigation property for table display
                    if (!reader.IsDBNull(reader.GetOrdinal("TableNumber")))
                    {
                        session.Table = new RestaurantTable
                        {
                            TableNumber = reader.GetString("TableNumber")
                        };
                    }
                    
                    TableSessions.Add(session);
                }
                
                TableEmptyLabel.IsVisible = TableSessions.Count == 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load table sessions: {ex.Message}");
            }
        }

        private void OnCollectionTabClicked(object? sender, EventArgs e)
        {
            // Highlight Collection tab with green
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#10B981"); // Green
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            TableTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            
            // Update label colors
            CollectionTabLabel.TextColor = Colors.White;
            DeliveryTabLabel.TextColor = Color.FromArgb("#6B7280");
            TableTabLabel.TextColor = Color.FromArgb("#6B7280");
            
            // Show Collection view
            CollectionOrdersLayout.IsVisible = true;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnDeliveryTabClicked(object? sender, EventArgs e)
        {
            // Highlight Delivery tab with green
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#10B981"); // Green
            TableTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            
            // Update label colors
            CollectionTabLabel.TextColor = Color.FromArgb("#6B7280");
            DeliveryTabLabel.TextColor = Colors.White;
            TableTabLabel.TextColor = Color.FromArgb("#6B7280");
            
            // Show Delivery view
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = true;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnTableTabClicked(object? sender, EventArgs e)
        {
            // Highlight Table tab with green
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            TableTabBorder.BackgroundColor = Color.FromArgb("#10B981"); // Green
            
            // Update label colors
            CollectionTabLabel.TextColor = Color.FromArgb("#6B7280");
            DeliveryTabLabel.TextColor = Color.FromArgb("#6B7280");
            TableTabLabel.TextColor = Colors.White;
            
            // Show Table view
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = true;
        }

        private async void OnOrderTapped(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is Order order)
            {
                // Navigate to order edit page
                var orderPage = new OrderPlacementPageSimple(existingOrderId: order.OrderId);

                await Navigation.PushAsync(orderPage, false);
            }
        }

        private async void OnTableTapped(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is TableSession session)
            {
                // Navigate to table order page
                // OrderPlacementPageSimple constructor: (string tableNumber, int coverCount, string staffName, int staffId)
                var tableName = session.Table?.TableNumber ?? session.TableId.ToString();
                var existingOrderId = session.LinkedOrderId ?? session.CurrentOrderId;
                var orderPage = new OrderPlacementPageSimple(
                    tableName, 
                    session.PartySize, 
                    "Staff", 
                    1,
                    session.Id,
                    existingOrderId
                );

                await Navigation.PushAsync(orderPage, false);
            }
        }
    }
}
