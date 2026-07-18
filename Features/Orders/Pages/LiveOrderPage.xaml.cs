using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
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
    public partial class LiveOrderPage : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private readonly OrderLifecycleRolloutService _rolloutService;
        private readonly TableSessionService _tableSessionService;
        private bool _lifecycleColumnsEnsured;
        private bool _isLoadingOrders;
        private bool _pendingReload;
        private readonly SemaphoreSlim _ordersReloadGate = new(1, 1);
        private bool _tableBackfillCompleted;
        private bool _isSubscribedToLiveUpdates;
        private bool _webOrderTotalsRepairCompleted;
        private bool _isPageActive;
        private bool _isNavigatingAway;
        private int _pageGeneration;
        private CancellationTokenSource? _pageRefreshCts;
        private OrderLifecycleRolloutConfig _rolloutConfig = OrderLifecycleRolloutConfig.CreateDefault();

        private List<Order> _allOrders = new();
        private List<Order> _collectionOrders = new();
        private List<Order> _deliveryOrders = new();
        private List<TableSession> _tableSessions = new();

        private const double CardWidth = 220;
        private const double CardMinHeight = 132;
        private const string LocalSourceFilter = @"
                      (
                        LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) = 'local'
                        OR (
                            LOWER(COALESCE(o.source_channel, '')) = 'web'
                            AND NULLIF(TRIM(COALESCE(o.order_id, '')), '') IS NOT NULL
                            AND NULLIF(TRIM(COALESCE(o.cloud_order_id, '')), '') IS NOT NULL
                            AND TRIM(o.order_id) <> TRIM(o.cloud_order_id)
                        )
                      )";
        private const string LocalLinkedOrderSourceFilter = @"
                          (
                            LOWER(COALESCE(NULLIF(o2.source_channel, ''), 'local')) = 'local'
                            OR (
                                LOWER(COALESCE(o2.source_channel, '')) = 'web'
                                AND NULLIF(TRIM(COALESCE(o2.order_id, '')), '') IS NOT NULL
                                AND NULLIF(TRIM(COALESCE(o2.cloud_order_id, '')), '') IS NOT NULL
                                AND TRIM(o2.order_id) <> TRIM(o2.cloud_order_id)
                            )
                          )";
        private const string WebCashDueSourceFilter = @"
                      (
                        LOWER(COALESCE(o.source_channel, '')) = 'web'
                        AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
                        AND REPLACE(REPLACE(REPLACE(LOWER(COALESCE(o.payment_method, 'cash')), ' ', ''), '_', ''), '-', '')
                            IN ('cash', 'cod', 'cashondelivery', 'cashoncollection')
                      )";
        private const string LiveOrderSourceFilter = @"
                      (
                        " + LocalSourceFilter + @"
                        OR
                        " + WebCashDueSourceFilter + @"
                      )";
        private const string ActiveLifecycleFilter = @"
                      AND COALESCE(o.is_open, 1) = 1
                      AND LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";
        private const string ActiveLinkedOrderLifecycleFilter = @"
                          AND COALESCE(o2.is_open, 1) = 1
                          AND LOWER(COALESCE(NULLIF(o2.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";

        public LiveOrderPage()
        {
            InitializeComponent();
            
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _rolloutService = ServiceHelper.GetService<OrderLifecycleRolloutService>() ?? new OrderLifecycleRolloutService(_databaseService);
            _tableSessionService = ServiceHelper.GetService<TableSessionService>() ?? new TableSessionService();
            
            TopBar.SetPageTitle("Live Order");
            OnAllTabClicked(this, EventArgs.Empty);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _isPageActive = true;
            _isNavigatingAway = false;
            _pageGeneration++;
            _pageRefreshCts?.Cancel();
            _pageRefreshCts?.Dispose();
            _pageRefreshCts = new CancellationTokenSource();
            SubscribeToLiveUpdates();
            RequestOrdersReload();
        }

        protected override void OnDisappearing()
        {
            _isPageActive = false;
            _pendingReload = false;
            _pageRefreshCts?.Cancel();
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

        private void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (!_isPageActive || _isNavigatingAway
                || !e.HasAny(AppDataChangeKind.Orders, AppDataChangeKind.TableLayout))
            {
                return;
            }

            if (e.HasKind(AppDataChangeKind.Orders) && !e.IsFromCurrentTerminal)
            {
                var generation = _pageGeneration;
                var cancellationToken = _pageRefreshCts?.Token ?? CancellationToken.None;
                _ = MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (CanRenderOrders(generation, cancellationToken))
                    {
                        await ToastNotification.ShowAsync("Live update", e.ToastMessage, NotificationType.Info, 1400);
                    }
                });
            }

            RequestOrdersReload();
        }

        private void RequestOrdersReload()
        {
            var refreshCts = _pageRefreshCts;
            if (!_isPageActive || _isNavigatingAway || refreshCts == null || refreshCts.IsCancellationRequested)
            {
                return;
            }

            _ = LoadAllOrdersAsync(_pageGeneration, refreshCts.Token);
        }

        private bool CanRenderOrders(int generation, CancellationToken cancellationToken) =>
            _isPageActive
            && !_isNavigatingAway
            && generation == _pageGeneration
            && !cancellationToken.IsCancellationRequested
            && ReferenceEquals(Shell.Current?.CurrentPage, this);

        private async Task LoadAllOrdersAsync(int generation, CancellationToken cancellationToken)
        {
            if (!CanRenderOrders(generation, cancellationToken))
            {
                return;
            }

            if (!await _ordersReloadGate.WaitAsync(0))
            {
                _pendingReload = true;
                return;
            }

            _isLoadingOrders = true;
            var scheduleTrailingReload = false;

            try
            {
                var reloadPasses = 0;
                do
                {
                    _pendingReload = false;
                    reloadPasses++;
                    cancellationToken.ThrowIfCancellationRequested();
                    await EnsureRolloutConfigAsync();
                    await RepairOpenWebOrderTotalsAsync();

                    await LoadAllOpenOrdersAsync();
                    await LoadCollectionOrdersAsync();
                    await LoadDeliveryOrdersAsync();
                    await LoadTableSessionsAsync();

                    if (!CanRenderOrders(generation, cancellationToken))
                    {
                        return;
                    }

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (CanRenderOrders(generation, cancellationToken))
                        {
                            RebuildAllGrids();
                        }
                    });
                }
                while (_pendingReload && reloadPasses < 2 && CanRenderOrders(generation, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                // The page was closed or navigation started while data was loading.
            }
            finally
            {
                _isLoadingOrders = false;
                scheduleTrailingReload = _pendingReload && _isPageActive && !_isNavigatingAway;
                _pendingReload = false;
                _ordersReloadGate.Release();
            }

            if (scheduleTrailingReload)
            {
                await Task.Delay(750);
                RequestOrdersReload();
            }
        }

        private void RebuildAllGrids()
        {
            RebuildOrderGrid(AllOrdersGrid, _allOrders, NavigateToOrderAsync);
            AllEmptyLabel.IsVisible = _allOrders.Count == 0;

            RebuildOrderGrid(CollectionOrdersGrid, _collectionOrders, NavigateToOrderAsync);
            CollectionEmptyLabel.IsVisible = _collectionOrders.Count == 0;

            RebuildOrderGrid(DeliveryOrdersGrid, _deliveryOrders, NavigateToOrderAsync);
            DeliveryEmptyLabel.IsVisible = _deliveryOrders.Count == 0;

            RebuildTableGrid();
            TableEmptyLabel.IsVisible = _tableSessions.Count == 0;
        }

        private void RebuildOrderGrid(FlexLayout grid, IReadOnlyList<Order> orders, Func<Order, Task> onTap)
        {
            var cards = orders
                .Select(order => CreateOrderCard(order, onTap))
                .ToList();

            RemoveLayoutChildrenSafely(grid);

            foreach (var card in cards)
            {
                grid.Children.Add(card);
            }
        }

        private void RebuildTableGrid()
        {
            var cards = _tableSessions
                .Select(CreateTableCard)
                .ToList();

            RemoveLayoutChildrenSafely(TableOrdersGrid);

            foreach (var card in cards)
            {
                TableOrdersGrid.Children.Add(card);
            }
        }

        private static void RemoveLayoutChildrenSafely(FlexLayout layout)
        {
            // FlexLayout.Children.Clear() maps to WinUI UIElementCollection.Clear(),
            // which can terminate the process with 0xC0000005 during page transitions.
            // Removing one child at a time uses the stable native removal path instead.
            for (var index = layout.Children.Count - 1; index >= 0; index--)
            {
                layout.Children.RemoveAt(index);
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

            var stack = new VerticalStackLayout { Spacing = 4 };

            var badges = BuildOrderBadges(order);
            if (badges.Count > 0)
            {
                stack.Children.Add(CreateBadgeRow(badges));
            }

            stack.Children.Add(new Label
            {
                Text = FormatOrderTypeLabel(order.OrderType),
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            stack.Children.Add(new Label
            {
                Text = FormatOrderNumber(order.OrderNumber, order.OrderId),
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8"),
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
                    MaxLines = 2,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            stack.Children.Add(new Label
            {
                Text = $"£{order.TotalAmount:F2}",
                FontSize = 24,
                FontAttributes = FontAttributes.Bold,
                TextColor = accentColor,
                Margin = new Thickness(0, 6, 0, 0)
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

        private static List<OrderBadge> BuildOrderBadges(Order order)
        {
            var badges = new List<OrderBadge>();
            if (!IsWebOrder(order))
            {
                return badges;
            }

            badges.Add(new OrderBadge("WEB", "#DBEAFE", "#1D4ED8"));
            badges.Add(new OrderBadge(FormatOrderTypeLabel(order.OrderType).ToUpperInvariant(), "#E0F2FE", "#0369A1"));

            if (OnlineOrderPaymentHelper.IsDeferredPaymentMethod(order.PaymentMethod)
                && order.LocalLifecycleState != LocalLifecycleState.Paid
                && order.LocalLifecycleState != LocalLifecycleState.Voided)
            {
                badges.Add(new OrderBadge("CASH DUE", "#FEF3C7", "#B45309"));
            }

            return badges;
        }

        private static HorizontalStackLayout CreateBadgeRow(IReadOnlyList<OrderBadge> badges)
        {
            var row = new HorizontalStackLayout
            {
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 4)
            };

            foreach (var badge in badges)
            {
                row.Children.Add(CreateBadge(badge));
            }

            return row;
        }

        private static Border CreateBadge(OrderBadge badge)
        {
            return new Border
            {
                BackgroundColor = Color.FromArgb(badge.BackgroundColor),
                StrokeThickness = 0,
                Padding = new Thickness(6, 4),
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Content = new Label
                {
                    Text = badge.Text,
                    FontSize = 9,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(badge.TextColor),
                    LineBreakMode = LineBreakMode.NoWrap
                }
            };
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

            var stack = new VerticalStackLayout { Spacing = 4 };

            stack.Children.Add(new Label
            {
                Text = "Table",
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E293B")
            });

            if (!string.IsNullOrWhiteSpace(session.TableDisplay))
            {
                stack.Children.Add(new Label
                {
                    Text = session.TableDisplay.StartsWith("Table ", StringComparison.OrdinalIgnoreCase)
                        ? session.TableDisplay
                        : $"Table {session.TableDisplay}",
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (session.HasLinkedOpenOrder)
            {
                stack.Children.Add(new Label
                {
                    Text = FormatOrderNumber(session.LinkedOrderNumber, session.LinkedOrderId),
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8"),
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

        private static string FormatOrderTypeLabel(string? orderType)
        {
            return (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "delivery" or "del" => "Delivery",
                "table" or "tbl" or "dine_in" or "dine-in" => "Table",
                _ => "Collection"
            };
        }

        private static bool IsWebOrder(Order order)
        {
            return string.Equals(order.SourceChannel, "web", StringComparison.OrdinalIgnoreCase);
        }

        private async Task NavigateToOrderAsync(Order order)
        {
            if (_isNavigatingAway)
            {
                return;
            }

            if (IsTableOrderType(order.OrderType) && order.TableSessionId.HasValue && order.TableSessionId.Value > 0)
            {
                var session = _tableSessions.FirstOrDefault(s => s.Id == order.TableSessionId.Value);
                if (session != null)
                {
                    await NavigateToTableAsync(session);
                    return;
                }
            }

            var orderPage = new OrderPlacementPageSimple(existingOrderId: order.OrderId);
            await PushOrderPageAsync(orderPage);
        }

        private static bool IsTableOrderType(string? orderType)
        {
            return (orderType ?? string.Empty).Trim().ToLowerInvariant() is "table" or "tbl" or "dine_in" or "dine-in";
        }

        private async Task NavigateToTableAsync(TableSession session)
        {
            if (_isNavigatingAway)
            {
                return;
            }

            var tableName = session.Table?.TableNumber ?? session.TableId.ToString();
            var existingOrderId = session.LinkedOrderId ?? session.CurrentOrderId;
            if (session.Id < 0 && !string.IsNullOrWhiteSpace(existingOrderId))
            {
                await PushOrderPageAsync(new OrderPlacementPageSimple(existingOrderId));
                return;
            }

            var orderPage = new OrderPlacementPageSimple(
                tableName,
                session.PartySize,
                "Staff",
                1,
                session.Id,
                existingOrderId);

            await PushOrderPageAsync(orderPage);
        }

        private async Task PushOrderPageAsync(Page page)
        {
            _isNavigatingAway = true;
            _pageRefreshCts?.Cancel();

            try
            {
                await Navigation.PushAsync(page, false);
            }
            catch
            {
                _isNavigatingAway = false;
                if (_isPageActive)
                {
                    _pageRefreshCts?.Dispose();
                    _pageRefreshCts = new CancellationTokenSource();
                    RequestOrdersReload();
                }

                throw;
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

        private async Task RepairOpenWebOrderTotalsAsync()
        {
            if (_webOrderTotalsRepairCompleted)
            {
                return;
            }

            try
            {
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureOrderLifecycleSchemaAsync(connection);

                const string repairSql = @"
                    UPDATE orders o
                    INNER JOIN (
                        SELECT oi.order_id AS order_db_id,
                               SUM((COALESCE(oi.item_price, 0.00) + COALESCE(addons.addon_unit_total, 0.00)) * COALESCE(oi.quantity, 0)) AS item_total
                        FROM order_items oi
                        LEFT JOIN (
                            SELECT order_item_id,
                                   SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total
                            FROM order_item_addons
                            GROUP BY order_item_id
                        ) addons ON addons.order_item_id = oi.id
                        GROUP BY oi.order_id
                    ) totals ON totals.order_db_id = o.id
                    SET o.subtotal_amount = CASE
                            WHEN COALESCE(o.subtotal_amount, 0.00) = 0.00 THEN totals.item_total
                            ELSE o.subtotal_amount
                        END,
                        o.total_amount = CASE
                            WHEN COALESCE(o.total_amount, 0.00) = 0.00 THEN totals.item_total + COALESCE(o.delivery_fee, 0.00)
                            ELSE o.total_amount
                        END,
                        o.updated_at = CURRENT_TIMESTAMP
                    WHERE LOWER(COALESCE(o.source_channel, '')) = 'web'
                      AND COALESCE(o.is_open, 1) = 1
                      AND LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')
                      AND COALESCE(o.total_amount, 0.00) = 0.00
                      AND totals.item_total > 0.00";

                using var command = new MySqlCommand(repairSql, connection);
                var repaired = await command.ExecuteNonQueryAsync();
                if (repaired > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveOrder] Repaired {repaired} open web order totals from item rows.");
                }

                _webOrderTotalsRepairCompleted = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveOrder] Open web order total repair skipped: {ex.Message}");
            }
        }

        private async Task LoadAllOpenOrdersAsync()
        {
            try
            {
                var orders = new List<Order>();

                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureOrderLifecycleSchemaAsync(connection);
                var query = $@"
                    SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                           o.order_type AS OrderType, o.table_session_id AS TableSessionId,
                           o.source_channel AS SourceChannel, o.payment_method AS PaymentMethod,
                           o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                           COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                           COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                           COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
                    FROM orders o
                    WHERE {LiveOrderSourceFilter}
                      {ActiveLifecycleFilter}
                    ORDER BY FIELD(COALESCE(LOWER(o.local_lifecycle_state), 'active'), 'draft', 'active', 'sent_partial', 'sent_full', 'payment_partial'),
                             o.updated_at DESC, o.created_at DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    orders.Add(ReadOrderFromReader(reader));
                }

                _allOrders = orders;
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load open orders: {ex.Message}"));
            }
        }

        private async Task LoadCollectionOrdersAsync()
        {
            try
            {
                var orders = new List<Order>();
                
                using var connection = await _databaseService.GetConnectionAsync();
                await EnsureOrderLifecycleSchemaAsync(connection);
                var query = $@"
                    SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                           o.order_type AS OrderType, o.table_session_id AS TableSessionId,
                           o.source_channel AS SourceChannel, o.payment_method AS PaymentMethod,
                           o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                           COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                           COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                           COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
                    FROM orders o
                    WHERE LOWER(o.order_type) IN ('pickup', 'collection', 'col', 'takeaway') 
                      AND {LiveOrderSourceFilter}
                      {ActiveLifecycleFilter}
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
                var query = $@"
                    SELECT o.id, o.order_id AS OrderId, o.order_number AS OrderNumber, o.customer_name AS CustomerName,
                           o.order_type AS OrderType, o.table_session_id AS TableSessionId,
                           o.source_channel AS SourceChannel, o.payment_method AS PaymentMethod,
                           o.total_amount AS TotalAmount, o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt, o.local_lifecycle_state AS LocalLifecycleState,
                           COALESCE(o.is_open, 1) AS IsOpen, COALESCE(o.draft_abandoned_flag, 0) AS DraftAbandonedFlag,
                           COALESCE(o.send_attempt_count, 0) AS SendAttemptCount,
                           COALESCE(o.payment_attempt_count, 0) AS PaymentAttemptCount
                    FROM orders o
                    WHERE LOWER(o.order_type) IN ('delivery', 'del') 
                      AND {LiveOrderSourceFilter}
                      {ActiveLifecycleFilter}
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
                OrderType = reader.IsDBNull(reader.GetOrdinal("OrderType")) ? "pickup" : reader.GetString("OrderType"),
                SourceChannel = reader.IsDBNull(reader.GetOrdinal("SourceChannel")) ? "local" : reader.GetString("SourceChannel"),
                PaymentMethod = reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? null : reader.GetString("PaymentMethod"),
                TableSessionId = reader.IsDBNull(reader.GetOrdinal("TableSessionId")) ? null : reader.GetInt32("TableSessionId"),
                CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Guest" : reader.GetString("CustomerName"),
                TotalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal("TotalAmount"),
                CreatedAt = reader.GetDateTime("CreatedAt"),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? reader.GetDateTime("CreatedAt") : reader.GetDateTime("UpdatedAt"),
                LocalLifecycleState = reader.IsDBNull(reader.GetOrdinal("LocalLifecycleState"))
                    ? LocalLifecycleState.Active
                    : ParseLifecycleState(reader.GetString("LocalLifecycleState")),
                IsOpen = !reader.IsDBNull(reader.GetOrdinal("IsOpen")) && reader.GetBoolean("IsOpen"),
                DraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("DraftAbandonedFlag")) && reader.GetBoolean("DraftAbandonedFlag"),
                SendAttemptCount = reader.IsDBNull(reader.GetOrdinal("SendAttemptCount")) ? 0 : reader.GetInt32("SendAttemptCount"),
                PaymentAttemptCount = reader.IsDBNull(reader.GetOrdinal("PaymentAttemptCount")) ? 0 : reader.GetInt32("PaymentAttemptCount")
            };
        }

        private static LocalLifecycleState ParseLifecycleState(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "draft" => LocalLifecycleState.Draft,
                "active" => LocalLifecycleState.Active,
                "sent_partial" => LocalLifecycleState.SentPartial,
                "sentfull" => LocalLifecycleState.SentFull,
                "sent_full" => LocalLifecycleState.SentFull,
                "payment_partial" => LocalLifecycleState.PaymentPartial,
                "paid" => LocalLifecycleState.Paid,
                "voided" => LocalLifecycleState.Voided,
                _ => Enum.TryParse<LocalLifecycleState>(value, true, out var parsed) ? parsed : LocalLifecycleState.Active
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

            await RepairLegacyLocalOrderClassificationAsync(connection);
            await RepairInconsistentOpenOrderStatusAsync(connection);

            _lifecycleColumnsEnsured = true;
        }

        private static async Task RepairInconsistentOpenOrderStatusAsync(MySqlConnection connection)
        {
            var repairStatements = new[]
            {
                @"UPDATE orders
                  SET is_open = 0
                  WHERE LOWER(COALESCE(local_lifecycle_state, '')) IN ('paid', 'voided')
                    AND COALESCE(is_open, 1) = 1",
                @"UPDATE orders
                  SET status = CASE
                          WHEN LOWER(COALESCE(local_lifecycle_state, '')) IN ('sent_partial', 'sent_full', 'payment_partial') THEN 'kitchen'
                          ELSE 'new'
                      END,
                      is_open = 1
                  WHERE LOWER(COALESCE(NULLIF(source_channel, ''), 'local')) = 'local'
                    AND LOWER(COALESCE(local_lifecycle_state, 'active')) NOT IN ('paid', 'voided')
                    AND LOWER(COALESCE(status, '')) IN ('completed', 'paid', 'closed', 'cancelled', 'canceled', 'void', 'voided')",
                @"UPDATE orders
                  SET status = 'completed',
                      is_open = 0
                  WHERE LOWER(COALESCE(local_lifecycle_state, '')) = 'paid'
                    AND LOWER(COALESCE(status, '')) NOT IN ('completed', 'paid', 'closed')",
                @"UPDATE orders
                  SET status = 'cancelled',
                      is_open = 0
                  WHERE LOWER(COALESCE(local_lifecycle_state, '')) = 'voided'
                    AND LOWER(COALESCE(status, '')) NOT IN ('cancelled', 'canceled', 'void', 'voided')"
            };

            try
            {
                var repaired = 0;
                foreach (var statement in repairStatements)
                {
                    using var repair = new MySqlCommand(statement, connection);
                    repaired += await repair.ExecuteNonQueryAsync();
                }

                if (repaired > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveOrder] Repaired {repaired} inconsistent local order status rows.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveOrder] Status repair skipped: {ex.Message}");
            }
        }

        private static async Task RepairLegacyLocalOrderClassificationAsync(MySqlConnection connection)
        {
            const string repairSql = @"
                UPDATE orders
                SET source_channel = 'local'
                WHERE LOWER(COALESCE(source_channel, '')) = 'web'
                  AND LOWER(COALESCE(order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del', 'table', 'tbl', 'dine_in', 'dine-in')
                  AND NULLIF(TRIM(COALESCE(order_id, '')), '') IS NOT NULL
                  AND NULLIF(TRIM(COALESCE(cloud_order_id, '')), '') IS NOT NULL
                  AND TRIM(order_id) <> TRIM(cloud_order_id)
                  AND COALESCE(is_open, 1) = 1
                  AND LOWER(COALESCE(NULLIF(local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";

            try
            {
                using var repair = new MySqlCommand(repairSql, connection);
                var repaired = await repair.ExecuteNonQueryAsync();
                if (repaired > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LiveOrder] Repaired {repaired} legacy local order source values.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveOrder] Legacy source repair skipped: {ex.Message}");
            }
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
                await EnsureOrderLifecycleSchemaAsync(connection);
                var query = $@"
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
                          AND {LocalLinkedOrderSourceFilter}
                          {ActiveLinkedOrderLifecycleFilter}
                        ORDER BY o2.updated_at DESC, o2.id DESC
                        LIMIT 1
                    )
                    WHERE ts.Status != 'Closed' AND ts.IsActive = 1
                    ORDER BY ts.StartTime DESC";
                
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var session = new TableSession
                        {
                            Id = reader.GetInt32("Id"),
                            TableId = reader.GetInt32("TableId"),
                            SessionNumber = reader.GetString("SessionNumber"),
                            PartySize = reader.GetInt32("PartySize"),
                            StartTime = reader.GetDateTime("StartTime"),
                            Status = Enum.TryParse<TableSessionStatus>(reader.GetString("Status"), true, out var status)
                                ? status
                                : TableSessionStatus.Ordering,
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
                }

                await AddOpenTableOrdersWithoutActiveSessionAsync(connection, sessions);
                _tableSessions = sessions;
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load table sessions: {ex.Message}"));
            }
        }

        private async Task AddOpenTableOrdersWithoutActiveSessionAsync(MySqlConnection connection, List<TableSession> sessions)
        {
            var knownOrderIds = sessions
                .Where(session => !string.IsNullOrWhiteSpace(session.LinkedOrderId))
                .Select(session => session.LinkedOrderId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var query = $@"
                SELECT o.id AS LinkedOrderDbId, o.order_id AS LinkedOrderId, o.order_number AS LinkedOrderNumber,
                       o.customer_name AS CustomerName, o.total_amount AS LinkedOrderTotalAmount,
                       o.local_lifecycle_state AS LinkedOrderLifecycleState, o.updated_at AS LinkedOrderUpdatedAt,
                       o.created_at AS CreatedAt, COALESCE(o.is_open, 1) AS LinkedOrderIsOpen,
                       COALESCE(o.draft_abandoned_flag, 0) AS LinkedOrderDraftAbandonedFlag,
                       o.table_session_id AS TableSessionId
                FROM orders o
                WHERE LOWER(COALESCE(o.order_type, '')) IN ('table', 'tbl', 'dine_in', 'dine-in')
                  AND {LocalSourceFilter}
                  {ActiveLifecycleFilter}
                  AND (
                        o.table_session_id IS NULL
                        OR NOT EXISTS (
                            SELECT 1
                            FROM TableSessions ts
                            WHERE ts.Id = o.table_session_id
                              AND ts.Status <> 'Closed'
                              AND ts.IsActive = 1
                        )
                  )
                ORDER BY o.updated_at DESC, o.created_at DESC";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var linkedOrderId = reader.IsDBNull(reader.GetOrdinal("LinkedOrderId"))
                    ? string.Empty
                    : reader.GetString("LinkedOrderId");
                if (string.IsNullOrWhiteSpace(linkedOrderId) || knownOrderIds.Contains(linkedOrderId))
                {
                    continue;
                }

                var tableNumber = ExtractTableNumber(reader.IsDBNull(reader.GetOrdinal("CustomerName"))
                    ? null
                    : reader.GetString("CustomerName"));
                var orderDbId = reader.GetInt32("LinkedOrderDbId");

                sessions.Add(new TableSession
                {
                    Id = -orderDbId,
                    TableId = 0,
                    SessionNumber = $"ORDER-{orderDbId}",
                    PartySize = 1,
                    StartTime = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.Now : reader.GetDateTime("CreatedAt"),
                    Status = TableSessionStatus.Ordering,
                    CurrentOrderId = linkedOrderId,
                    LinkedOrderDbId = orderDbId,
                    LinkedOrderId = linkedOrderId,
                    LinkedOrderNumber = reader.IsDBNull(reader.GetOrdinal("LinkedOrderNumber")) ? null : reader.GetString("LinkedOrderNumber"),
                    LinkedOrderTotalAmount = reader.IsDBNull(reader.GetOrdinal("LinkedOrderTotalAmount")) ? null : reader.GetDecimal("LinkedOrderTotalAmount"),
                    LinkedOrderLifecycleState = reader.IsDBNull(reader.GetOrdinal("LinkedOrderLifecycleState")) ? null : reader.GetString("LinkedOrderLifecycleState"),
                    LinkedOrderUpdatedAt = reader.IsDBNull(reader.GetOrdinal("LinkedOrderUpdatedAt")) ? null : reader.GetDateTime("LinkedOrderUpdatedAt"),
                    LinkedOrderIsOpen = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderIsOpen")) && reader.GetBoolean("LinkedOrderIsOpen"),
                    LinkedOrderDraftAbandonedFlag = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderDraftAbandonedFlag")) && reader.GetBoolean("LinkedOrderDraftAbandonedFlag"),
                    Table = new RestaurantTable
                    {
                        TableNumber = string.IsNullOrWhiteSpace(tableNumber) ? "Unlinked" : tableNumber
                    }
                });

                knownOrderIds.Add(linkedOrderId);
            }
        }

        private sealed record OrderBadge(string Text, string BackgroundColor, string TextColor);

        private static string? ExtractTableNumber(string? customerName)
        {
            if (string.IsNullOrWhiteSpace(customerName))
            {
                return null;
            }

            var value = customerName.Trim();
            const string tablePrefix = "Table ";
            return value.StartsWith(tablePrefix, StringComparison.OrdinalIgnoreCase)
                ? value[tablePrefix.Length..].Trim()
                : value;
        }

        private void ResetTabStyles()
        {
            var inactiveBackground = Color.FromArgb("#F5F5F5");
            var inactiveText = Color.FromArgb("#6B7280");

            AllTabBorder.BackgroundColor = inactiveBackground;
            CollectionTabBorder.BackgroundColor = inactiveBackground;
            DeliveryTabBorder.BackgroundColor = inactiveBackground;
            TableTabBorder.BackgroundColor = inactiveBackground;

            AllTabLabel.TextColor = inactiveText;
            CollectionTabLabel.TextColor = inactiveText;
            DeliveryTabLabel.TextColor = inactiveText;
            TableTabLabel.TextColor = inactiveText;
        }

        private void OnAllTabClicked(object? sender, EventArgs e)
        {
            ResetTabStyles();
            AllTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            AllTabLabel.TextColor = Colors.White;

            AllOrdersLayout.IsVisible = true;
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnCollectionTabClicked(object? sender, EventArgs e)
        {
            ResetTabStyles();
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            CollectionTabLabel.TextColor = Colors.White;

            AllOrdersLayout.IsVisible = false;
            CollectionOrdersLayout.IsVisible = true;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnDeliveryTabClicked(object? sender, EventArgs e)
        {
            ResetTabStyles();
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            DeliveryTabLabel.TextColor = Colors.White;

            AllOrdersLayout.IsVisible = false;
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = true;
            TableOrdersLayout.IsVisible = false;
        }

        private void OnTableTabClicked(object? sender, EventArgs e)
        {
            ResetTabStyles();
            TableTabBorder.BackgroundColor = Color.FromArgb("#10B981");
            TableTabLabel.TextColor = Colors.White;

            AllOrdersLayout.IsVisible = false;
            CollectionOrdersLayout.IsVisible = false;
            DeliveryOrdersLayout.IsVisible = false;
            TableOrdersLayout.IsVisible = true;
        }
    }
}
