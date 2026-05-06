using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MySqlConnector;
using Syncfusion.Maui.Calendar;

namespace POS_in_NET.Pages
{
    public partial class OrderHistoryPage : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private ObservableCollection<OrderHistoryItem> CompletedOrders { get; set; } = new();
        private ObservableCollection<OrderHistoryItem> VoidedOrders { get; set; } = new();
        
        private DateTime _selectedDate;
        private string _selectedOrderType = "ALL"; // ALL, COL, DEL, TBL

        public OrderHistoryPage()
        {
            InitializeComponent();
            
            _databaseService = new DatabaseService();
            _selectedDate = DateTime.Today;
            
            // Set page title
            TopBar.SetPageTitle("Order History");
            
            // Bind collections
            CompletedOrdersCollection.ItemsSource = CompletedOrders;
            VoidedOrdersCollection.ItemsSource = VoidedOrders;

            UpdateTabSelection();
            
            // Load today's orders
            UpdateDateDisplay();
            LoadOrders();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadOrders();
        }

        private void UpdateDateDisplay()
        {
            SelectedDateLabel.Text = _selectedDate.ToString("MMMM dd, yyyy");
        }

        private async void LoadOrders()
        {
            await LoadCompletedOrdersAsync();
            await LoadVoidedOrdersAsync();
        }

        private async Task LoadCompletedOrdersAsync()
        {
            try
            {
                CompletedOrders.Clear();
                
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Build query based on selected order type
                string orderTypeFilter = BuildOrderTypeFilter();
                
                var query = $@"
                    SELECT o.id, o.order_id, o.order_type, o.total_amount, 
                           o.created_at, o.status
                    FROM orders o
                    WHERE DATE(o.created_at) = @selectedDate
                          AND COALESCE(o.source_channel, 'local') = 'local'
                    {orderTypeFilter}
                    AND (COALESCE(LOWER(o.local_lifecycle_state), '') = 'paid' OR LOWER(o.status) IN ('completed', 'closed', 'paid'))
                    ORDER BY o.created_at DESC";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@selectedDate", _selectedDate.ToString("yyyy-MM-dd"));
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var orderType = reader.GetString("order_type");
                    var orderIcon = GetOrderIcon(orderType);
                    var createdAt = reader.GetDateTime("created_at");
                    
                    CompletedOrders.Add(new OrderHistoryItem
                    {
                        Id = reader.GetInt32("id"),
                        OrderId = reader.GetString("order_id"),
                        OrderType = orderType,
                        OrderIcon = orderIcon,
                        TotalAmount = reader.GetDecimal("total_amount"),
                        OrderDateTime = $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}",
                        Status = reader.GetString("status")
                    });
                }
                
                CompletedEmptyLabel.IsVisible = CompletedOrders.Count == 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load orders: {ex.Message}");
            }
        }

        private async Task LoadVoidedOrdersAsync()
        {
            try
            {
                VoidedOrders.Clear();
                
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Build query based on selected order type
                string orderTypeFilter = BuildOrderTypeFilter();
                
                var query = $@"
                    SELECT o.id, o.order_id, o.order_type, o.total_amount, 
                           o.created_at, o.status
                    FROM orders o
                    WHERE DATE(o.created_at) = @selectedDate
                          AND COALESCE(o.source_channel, 'local') = 'local'
                    {orderTypeFilter}
                    AND (COALESCE(LOWER(o.local_lifecycle_state), '') = 'voided' OR LOWER(o.status) IN ('void', 'cancelled'))
                    ORDER BY o.created_at DESC";
                
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@selectedDate", _selectedDate.ToString("yyyy-MM-dd"));
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var orderType = reader.GetString("order_type");
                    var createdAt = reader.GetDateTime("created_at");
                    
                    VoidedOrders.Add(new OrderHistoryItem
                    {
                        Id = reader.GetInt32("id"),
                        OrderId = reader.GetString("order_id"),
                        OrderType = orderType,
                        OrderIcon = "❌",
                        TotalAmount = reader.GetDecimal("total_amount"),
                        OrderDateTime = $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}",
                        Status = reader.GetString("status")
                    });
                }
                
                VoidedOrdersLayout.IsVisible = VoidedOrders.Count > 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load voided orders: {ex.Message}");
            }
        }

        private string GetOrderIcon(string orderType)
        {
            var normalized = orderType?.Trim().ToLowerInvariant() ?? string.Empty;

            return normalized switch
            {
                "pickup" => "📦",
                "collection" => "📦",
                "col" => "📦",
                "delivery" => "🚗",
                "del" => "🚗",
                "table" => "🍽",
                "tbl" => "🍽",
                "dine_in" => "🍽",
                "dine-in" => "🍽",
                _ => "📋"
            };
        }

        private string BuildOrderTypeFilter()
        {
            return _selectedOrderType switch
            {
                "COL" => "AND LOWER(o.order_type) IN ('col', 'collection', 'pickup')",
                "DEL" => "AND LOWER(o.order_type) IN ('del', 'delivery')",
                "TBL" => "AND LOWER(o.order_type) IN ('tbl', 'table')",
                _ => string.Empty
            };
        }

        // Tab Selection Handlers
        private void OnAllTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "ALL";
            UpdateTabSelection();
            LoadOrders();
        }

        private void OnCollectionTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "COL";
            UpdateTabSelection();
            LoadOrders();
        }

        private void OnDeliveryTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "DEL";
            UpdateTabSelection();
            LoadOrders();
        }

        private void OnTableTabClicked(object sender, EventArgs e)
        {
            _selectedOrderType = "TBL";
            UpdateTabSelection();
            LoadOrders();
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
            await Shell.Current.GoToAsync("//dashboard");
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
                    LoadOrders();
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
            searchPage.OrderSelected += (orderId, orderDate) =>
            {
                _selectedDate = orderDate;
                UpdateDateDisplay();
                LoadOrders();
            };
            
            await Navigation.PushModalAsync(searchPage);
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

        private async void OnPrintOrderClicked(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is OrderHistoryItem order)
            {
                // TODO: Implement print functionality
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print", $"Printing receipt for {order.OrderId}");
            }
        }

        private async void OnRefundOrderClicked(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is OrderHistoryItem order)
            {
                var confirm = await DisplayAlert(
                    "Refund Order",
                    $"Process refund for {order.OrderId}?\nAmount: £{order.TotalAmount:F2}",
                    "Yes",
                    "Cancel"
                );

                if (confirm)
                {
                    var refundModal = new RefundModal(order.Id, order.OrderId, order.TotalAmount);
                    refundModal.RefundCompleted += () => LoadOrders();
                    await Navigation.PushModalAsync(refundModal);
                }
            }
        }
    }

    // Helper class for order display
    public class OrderHistoryItem
    {
        public int Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string OrderIcon { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string OrderDateTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
