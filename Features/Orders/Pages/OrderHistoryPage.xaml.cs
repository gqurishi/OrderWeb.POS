using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Syncfusion.Maui.Calendar;

namespace POS_in_NET.Pages
{
    public partial class OrderHistoryPage : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private const int PageSize = 50;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private CancellationTokenSource? _loadCts;
        private bool _subscribedToChanges;
        
        private DateTime _selectedDate;
        private string _selectedOrderType = "ALL"; // ALL, COL, DEL, TBL
        private string _searchQuery = string.Empty;
        private int _pageNumber = 1;

        public OrderHistoryPage()
        {
            InitializeComponent();
            
            _databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
            _selectedDate = DateTime.Today;
            
            TopBar.SetPageTitle("Order History");
            
            UpdateTabSelection();
            UpdateDateDisplay();
            UpdatePagination(false);
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
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await AppAlertService.ShowAlertAsync("Error", $"Failed to load order history: {ex.Message}");
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
            if (SelectedDateLabel != null)
            {
                SelectedDateLabel.Text = _selectedDate.ToString("MMMM dd, yyyy");
            }
        }

        private async Task LoadOrdersAsync(CancellationToken cancellationToken)
        {
            var performance = PosPerformanceMonitor.BeginDataLoad("Order History");
            var completedRows = new List<OrderHistoryItem>(PageSize);
            var voidedRows = new List<OrderHistoryItem>(PageSize);
            using var connection = await _databaseService.GetConnectionAsync();

            var query = $@"
                WITH ranked_history AS (
                    SELECT o.id, o.order_id, o.order_number, o.order_type, o.total_amount,
                           o.created_at, o.status,
                           CASE WHEN o.local_lifecycle_state = 'voided' OR o.status IN ('void', 'cancelled')
                                THEN 'voided' ELSE 'completed' END AS history_group,
                           ROW_NUMBER() OVER (
                               PARTITION BY CASE WHEN o.local_lifecycle_state = 'voided' OR o.status IN ('void', 'cancelled')
                                                 THEN 'voided' ELSE 'completed' END
                               ORDER BY o.created_at DESC, o.id DESC) AS row_number
                    FROM orders o
                    WHERE (o.source_channel = 'local' OR o.source_channel IS NULL)
                          {BuildDateFilter()}
                          {BuildOrderTypeFilter()}
                          {BuildSearchFilter()}
                      AND (o.local_lifecycle_state IN ('paid', 'voided')
                           OR o.status IN ('completed', 'closed', 'paid', 'void', 'cancelled'))
                )
                SELECT id, order_id, order_number, order_type, total_amount,
                       created_at, status, history_group
                FROM ranked_history
                WHERE row_number > @offset AND row_number <= @pageEnd
                ORDER BY history_group, created_at DESC, id DESC";

            using var command = new MySqlCommand(query, connection);
            ApplyQueryParameters(command);
            var offset = (_pageNumber - 1) * PageSize;
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@pageEnd", offset + PageSize + 1);

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
                    OrderDateTime = $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}",
                    Status = ReadString(reader, "status")
                };

                (ReadString(reader, "history_group") == "voided" ? voidedRows : completedRows).Add(row);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var hasNextPage = completedRows.Count > PageSize || voidedRows.Count > PageSize;
            if (completedRows.Count > PageSize) completedRows.RemoveAt(PageSize);
            if (voidedRows.Count > PageSize) voidedRows.RemoveAt(PageSize);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CompletedOrdersCollection.ItemsSource = completedRows;
                VoidedOrdersCollection.ItemsSource = voidedRows;
                CompletedEmptyLabel.Text = string.IsNullOrWhiteSpace(_searchQuery)
                    ? "No orders found for this date"
                    : "No matching orders found";
                CompletedEmptyLabel.IsVisible = completedRows.Count == 0;
                VoidedOrdersLayout.IsVisible = voidedRows.Count > 0;
                UpdatePagination(hasNextPage);
            });
            PosPerformanceMonitor.MarkDataVisible(performance);
        }

        private static string ReadString(MySqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
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
                : "AND (o.order_id = @searchExact OR o.order_number = @searchExact OR o.customer_phone LIKE @searchPrefix)";
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
            command.Parameters.AddWithValue("@searchPrefix", $"{search}%");
        }

        private void UpdatePagination(bool hasNextPage)
        {
            PageNumberLabel.Text = $"Page {_pageNumber}";
            PreviousPageButton.IsEnabled = _pageNumber > 1;
            PreviousPageButton.Opacity = PreviousPageButton.IsEnabled ? 1 : 0.45;
            NextPageButton.IsEnabled = hasNextPage;
            NextPageButton.Opacity = hasNextPage ? 1 : 0.45;
        }

        private void ResetPageAndLoad()
        {
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        // Tab Selection Handlers
        private void OnAllTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "ALL";
            UpdateTabSelection();
            ResetPageAndLoad();
        }

        private void OnCollectionTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "COL";
            UpdateTabSelection();
            ResetPageAndLoad();
        }

        private void OnDeliveryTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "DEL";
            UpdateTabSelection();
            ResetPageAndLoad();
        }

        private void OnTableTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "TBL";
            UpdateTabSelection();
            ResetPageAndLoad();
        }

        private void UpdateTabSelection()
        {
            // Reset all tabs
            AllTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            AllTabLabel.TextColor = Color.FromArgb("#6B7280");
            CollectionTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            CollectionTabLabel.TextColor = Color.FromArgb("#6B7280");
            DeliveryTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            DeliveryTabLabel.TextColor = Color.FromArgb("#6B7280");
            TableTabBorder.BackgroundColor = Color.FromArgb("#F5F5F5");
            TableTabLabel.TextColor = Color.FromArgb("#6B7280");

            // Highlight selected tab
            switch (_selectedOrderType)
            {
                case "ALL":
                    AllTabBorder.BackgroundColor = Color.FromArgb("#10B981");
                    AllTabLabel.TextColor = Colors.White;
                    break;
                case "COL":
                    CollectionTabBorder.BackgroundColor = Color.FromArgb("#10B981");
                    CollectionTabLabel.TextColor = Colors.White;
                    break;
                case "DEL":
                    DeliveryTabBorder.BackgroundColor = Color.FromArgb("#10B981");
                    DeliveryTabLabel.TextColor = Colors.White;
                    break;
                case "TBL":
                    TableTabBorder.BackgroundColor = Color.FromArgb("#10B981");
                    TableTabLabel.TextColor = Colors.White;
                    break;
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            var roleAccess = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            await NavigationCoordinator.Shared.NavigateShellAsync(
                roleAccess.ResolveDashboardRoute(authService.CurrentUser?.Role),
                source: sender as VisualElement);
        }

        private async void OnCalendarClicked(object sender, EventArgs e)
        {
            // Create a modal with Syncfusion Calendar
            var modal = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#80000000")
            };

            var calendar = new SfCalendar
            {
                SelectedDate = _selectedDate,
                MinimumDate = new DateTime(2020, 1, 1),
                MaximumDate = DateTime.Today,
                SelectionMode = CalendarSelectionMode.Single,
                HeightRequest = 380,
                Background = Colors.White,
                HeaderView = new CalendarHeaderView
                {
                    Background = Color.FromArgb("#10B981"),
                    TextStyle = new CalendarTextStyle
                    {
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold
                    }
                },
                MonthView = new CalendarMonthView
                {
                    Background = Colors.White,
                    HeaderView = new CalendarMonthHeaderView
                    {
                        Background = Color.FromArgb("#F3F4F6"),
                        TextStyle = new CalendarTextStyle
                        {
                            TextColor = Color.FromArgb("#6B7280"),
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        }
                    },
                    TextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#1F2937"),
                        FontSize = 15
                    },
                    TodayTextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#10B981"),
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold
                    },
                    TrailingLeadingDatesTextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#D1D5DB"),
                        FontSize = 14
                    },
                    TodayBackground = Color.FromArgb("#D1FAE5")
                },
                SelectionBackground = Color.FromArgb("#10B981")
            };

            var frame = new Frame
            {
                BackgroundColor = Colors.White,
                Padding = 0,
                CornerRadius = 16,
                HasShadow = true,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                WidthRequest = 420
            };

            var mainLayout = new VerticalStackLayout
            {
                Spacing = 0
            };

            // Header with green background
            var headerLayout = new VerticalStackLayout
            {
                BackgroundColor = Color.FromArgb("#10B981"),
                Padding = new Thickness(24, 20),
                Spacing = 4
            };

            var titleLabel = new Label
            {
                Text = "Select Date",
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Start
            };

            var subtitleLabel = new Label
            {
                Text = "Choose a date to view orders",
                FontSize = 14,
                TextColor = Color.FromArgb("#D1FAE5"),
                HorizontalOptions = LayoutOptions.Start
            };

            headerLayout.Children.Add(titleLabel);
            headerLayout.Children.Add(subtitleLabel);

            // Content area with calendar
            var contentLayout = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16),
                Spacing = 0
            };

            contentLayout.Children.Add(calendar);

            // Button area
            var buttonLayout = new Grid
            {
                Padding = new Thickness(24, 16, 24, 24),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 12
            };

            var cancelBorder = new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 2,
                Stroke = Color.FromArgb("#D1D5DB"),
                Padding = new Thickness(0, 14)
            };
            cancelBorder.StrokeShape = new RoundRectangle { CornerRadius = 8 };
            var cancelLabel = new Label
            {
                Text = "Cancel",
                TextColor = Color.FromArgb("#6B7280"),
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            cancelBorder.Content = cancelLabel;
            var cancelTap = new TapGestureRecognizer();
            cancelTap.Tapped += async (s, args) =>
            {
                await Navigation.PopModalAsync();
            };
            cancelBorder.GestureRecognizers.Add(cancelTap);

            var okBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#10B981"),
                StrokeThickness = 0,
                Padding = new Thickness(0, 14)
            };
            okBorder.StrokeShape = new RoundRectangle { CornerRadius = 8 };
            var okLabel = new Label
            {
                Text = "Apply",
                TextColor = Colors.White,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            okBorder.Content = okLabel;
            var okTap = new TapGestureRecognizer();
            okTap.Tapped += async (s, args) =>
            {
                if (calendar.SelectedDate.HasValue)
                {
                    _selectedDate = calendar.SelectedDate.Value;
                    UpdateDateDisplay();
                    ResetPageAndLoad();
                }
                await Navigation.PopModalAsync();
            };
            okBorder.GestureRecognizers.Add(okTap);

            buttonLayout.Children.Add(cancelBorder);
            Grid.SetColumn(cancelBorder, 0);
            buttonLayout.Children.Add(okBorder);
            Grid.SetColumn(okBorder, 1);

            mainLayout.Children.Add(headerLayout);
            mainLayout.Children.Add(contentLayout);
            mainLayout.Children.Add(buttonLayout);

            frame.Content = mainLayout;
            modal.Content = frame;

            await Navigation.PushModalAsync(modal);
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            var searchPage = new OrderSearchModal();
            searchPage.SearchSubmitted += query =>
            {
                _searchQuery = query?.Trim() ?? string.Empty;
                ResetPageAndLoad();
            };
            
            await Navigation.PushModalAsync(searchPage);
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

        // Order Action Handlers
        private async void OnViewOrderClicked(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is OrderHistoryItem order)
            {
                var detailsModal = new OrderDetailsModal(order.Id);
                await Navigation.PushModalAsync(detailsModal);
            }
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
        public string OrderDateTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
