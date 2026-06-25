using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
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
        private bool _isSubscribedToLiveUpdates;
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();

        private List<Order> _collectionOrders = new();
        private List<Order> _deliveryOrders = new();
        private List<TableSession> _tableSessions = new();

        private const double CardWidth = 220;
        private const double CardMinHeight = 132;

        public LiveOrderPage()
        {
            InitializeComponent();
            
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _rolloutService = ServiceHelper.GetService<OrderLifecycleRolloutService>() ?? new OrderLifecycleRolloutService(_databaseService);
            _tableSessionService = ServiceHelper.GetService<TableSessionService>() ?? new TableSessionService();
            
            TopBar.SetPageTitle("Live Order");
            OnTableTabClicked(this, EventArgs.Empty);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SubscribeToLiveUpdates();
            LoadAllOrders();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            UnsubscribeFromLiveUpdates();
        }

        private void SubscribeToLiveUpdates()
        {
            if (_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged += OnAppDataChanged;
            _isSubscribedToLiveUpdates = true;
        }

        private void UnsubscribeFromLiveUpdates()
        {
            if (!_isSubscribedToLiveUpdates)
            {
                return;
            }

            AppDataRefreshService.DataChanged -= OnAppDataChanged;
            _isSubscribedToLiveUpdates = false;
        }

        private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (e.Kind != AppDataChangeKind.Manual &&
                e.Kind != AppDataChangeKind.Orders &&
                e.Kind != AppDataChangeKind.TableLayout &&
                e.Kind != AppDataChangeKind.All)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (e.Kind == AppDataChangeKind.Orders && !e.IsFromCurrentTerminal)
                {
                    await ToastNotification.ShowAsync("Live update", e.ToastMessage, NotificationType.Info, 1400);
                }

                LoadAllOrders();
            });
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

                await MainThread.InvokeOnMainThreadAsync(RebuildAllGrids);
            }
            finally
            {
                _isLoadingOrders = false;
            }
        }

        private void RebuildAllGrids()
        {
            RebuildOrderGrid(CollectionOrdersGrid, _collectionOrders, NavigateToOrderAsync);
            CollectionEmptyLabel.IsVisible = _collectionOrders.Count == 0;

            RebuildOrderGrid(DeliveryOrdersGrid, _deliveryOrders, NavigateToOrderAsync);
            DeliveryEmptyLabel.IsVisible = _deliveryOrders.Count == 0;

            RebuildTableGrid();
            TableEmptyLabel.IsVisible = _tableSessions.Count == 0;
        }

        private void RebuildOrderGrid(FlexLayout grid, IReadOnlyList<Order> orders, Func<Order, Task> onTap)
        {
            grid.Children.Clear();

            foreach (var order in orders)
            {
                grid.Children.Add(CreateOrderCard(order, onTap));
            }
        }

        private void RebuildTableGrid()
        {
            TableOrdersGrid.Children.Clear();

            foreach (var session in _tableSessions)
            {
                TableOrdersGrid.Children.Add(CreateTableCard(session));
            }
        }

        private Border CreateOrderCard(Order order, Func<Order, Task> onTap)
        {
            var accentColor = order.IsStaleDraft ? Color.FromArgb("#DC2626") : Color.FromArgb("#10B981");

            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = accentColor,
                StrokeThickness = 2,
                Padding = new Thickness(16, 14),
                Margin = new Thickness(0, 0, 14, 14),
                WidthRequest = CardWidth,
                MinimumHeightRequest = CardMinHeight,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Shadow = new Shadow
                {
                    Brush = Colors.Black,
                    Offset = new Point(0, 2),
                    Radius = 8,
                    Opacity = 0.08f
                }
            };

            var stack = new VerticalStackLayout { Spacing = 6 };

            stack.Children.Add(new Label
            {
                Text = FormatOrderNumber(order.OrderNumber, order.OrderId),
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            if (HasCustomerName(order.CustomerName))
            {
                stack.Children.Add(new Label
                {
                    Text = order.CustomerName.Trim(),
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                });
            }

            stack.Children.Add(new Label
            {
                Text = $"£{order.TotalAmount:F2}",
                FontSize = 24,
                FontAttributes = FontAttributes.Bold,
                TextColor = accentColor,
                Margin = new Thickness(0, 4, 0, 0)
            });

            stack.Children.Add(new Label
            {
                Text = order.CreatedAt.ToString("HH:mm · dd/MM"),
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8")
            });

            border.Content = stack;

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await onTap(order);
            border.GestureRecognizers.Add(tap);

            return border;
        }

        private Border CreateTableCard(TableSession session)
        {
            var accentColor = session.LinkedOrderIsStaleDraft || !session.HasLinkedOpenOrder
                ? Color.FromArgb("#F59E0B")
                : Color.FromArgb("#10B981");

            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = accentColor,
                StrokeThickness = 2,
                Padding = new Thickness(16, 14),
                Margin = new Thickness(0, 0, 14, 14),
                WidthRequest = CardWidth,
                MinimumHeightRequest = CardMinHeight,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Shadow = new Shadow
                {
                    Brush = Colors.Black,
                    Offset = new Point(0, 2),
                    Radius = 8,
                    Opacity = 0.08f
                }
            };

            var stack = new VerticalStackLayout { Spacing = 6 };

            stack.Children.Add(new Label
            {
                Text = $"Table {session.TableDisplay}",
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B")
            });

            if (session.HasLinkedOpenOrder)
            {
                stack.Children.Add(new Label
                {
                    Text = FormatOrderNumber(session.LinkedOrderNumber, session.LinkedOrderId),
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }

            stack.Children.Add(new Label
            {
                Text = session.LinkedOrderTotalAmount.HasValue
                    ? $"£{session.LinkedOrderTotalAmount.Value:F2}"
                    : "£0.00",
                FontSize = 24,
                FontAttributes = FontAttributes.Bold,
                TextColor = accentColor,
                Margin = new Thickness(0, 4, 0, 0)
            });

            stack.Children.Add(new Label
            {
                Text = $"{session.PartySize} guest{(session.PartySize == 1 ? string.Empty : "s")} · {session.TimeDisplay}",
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8")
            });

            border.Content = stack;

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await NavigateToTableAsync(session);
            border.GestureRecognizers.Add(tap);

            return border;
        }

        private static string FormatOrderNumber(string? orderNumber, string? orderId)
        {
            var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : orderId?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Order";
            }

            return value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
        }

        private static bool HasCustomerName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return !string.Equals(name.Trim(), "Guest", StringComparison.OrdinalIgnoreCase);
        }

        private async Task NavigateToOrderAsync(Order order)
        {
            var orderPage = new OrderPlacementPageSimple(existingOrderId: order.OrderId);
            await Navigation.PushAsync(orderPage, false);
        }

        private async Task NavigateToTableAsync(TableSession session)
        {
            var tableName = session.Table?.TableNumber ?? session.TableId.ToString();
            var existingOrderId = session.LinkedOrderId ?? session.CurrentOrderId;
            var orderPage = new OrderPlacementPageSimple(
                tableName,
                session.PartySize,
                "Staff",
                1,
                session.Id,
                existingOrderId);

            await Navigation.PushAsync(orderPage, false);
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
                var orders = new List<Order>();
                
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
                    orders.Add(ReadOrderFromReader(reader));
                }

                _collectionOrders = orders;
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load collection orders: {ex.Message}"));
            }
        }

        private async Task LoadDeliveryOrdersAsync()
        {
            try
            {
                var orders = new List<Order>();
                
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
                    orders.Add(ReadOrderFromReader(reader));
                }

                _deliveryOrders = orders;
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load delivery orders: {ex.Message}"));
            }
        }

        private static Order ReadOrderFromReader(MySqlDataReader reader)
        {
            var orderNumber = reader.IsDBNull(reader.GetOrdinal("OrderNumber"))
                ? reader.GetString(reader.GetOrdinal("OrderId"))
                : reader.GetString(reader.GetOrdinal("OrderNumber"));

            return new Order
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
            };
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
                // Schema may be managed elsewhere.
            }

            _lifecycleColumnsEnsured = true;
        }

        private async Task LoadTableSessionsAsync()
        {
            try
            {
                var sessions = new List<TableSession>();

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
                           o.total_amount AS LinkedOrderTotalAmount,
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
                        LinkedOrderTotalAmount = reader.IsDBNull(reader.GetOrdinal("LinkedOrderTotalAmount")) ? null : reader.GetDecimal("LinkedOrderTotalAmount"),
                        LinkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState")) ? null : reader.GetString("LinkedOrderLifecycleState"),
                        LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime("LinkedOrderUpdatedAt"),
                        LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean("LinkedOrderIsOpen"),
                        LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean("LinkedOrderDraftAbandonedFlag")
                    };
                    
                    if (!reader.IsDBNull(reader.GetOrdinal("TableNumber")))
                    {
                        session.Table = new RestaurantTable
                        {
                            TableNumber = reader.GetString("TableNumber")
                        };
                    }
                    
                    sessions.Add(session);
                }

                _tableSessions = sessions;
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load table sessions: {ex.Message}"));
            }
        }

        private void OnCollectionTabClicked(object? sender, EventArgs e)
        {
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            TableTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            
            CollectionTabLabel.TextColor = Colors.White;
            DeliveryTabLabel.TextColor = Color.FromArgb("#6B7280");
            TableTabLabel.TextColor = Color.FromArgb("#6B7280");
            
            CollectionOrdersLayout.IsVisible = true;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnDeliveryTabClicked(object? sender, EventArgs e)
        {
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            TableTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            
            CollectionTabLabel.TextColor = Color.FromArgb("#6B7280");
            DeliveryTabLabel.TextColor = Colors.White;
            TableTabLabel.TextColor = Color.FromArgb("#6B7280");
            
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = true;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnTableTabClicked(object? sender, EventArgs e)
        {
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            TableTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            
            CollectionTabLabel.TextColor = Color.FromArgb("#6B7280");
            DeliveryTabLabel.TextColor = Color.FromArgb("#6B7280");
            TableTabLabel.TextColor = Colors.White;
            
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = true;
        }
    }
}
