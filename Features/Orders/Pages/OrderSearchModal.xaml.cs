using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Views;
using System;
using System.Collections.ObjectModel;
using MySqlConnector;

namespace POS_in_NET.Pages
{
    public partial class OrderSearchModal : ContentPage
    {
        private readonly DatabaseService _databaseService;
        private ObservableCollection<SearchResultItem> SearchResults { get; set; } = new();
        private bool _isOpeningKeyboard;

        public event Action<string, DateTime>? OrderSelected;

        public OrderSearchModal()
        {
            InitializeComponent();
            
            _databaseService = new DatabaseService();
            SearchResultsCollection.ItemsSource = SearchResults;
        }

        private async Task<string?> OpenKeyboardAsync(string currentText)
        {
            if (_isOpeningKeyboard)
            {
                return null;
            }

            _isOpeningKeyboard = true;
            try
            {
                var keyboard = new VirtualKeyboardDialog();
                keyboard.SetInitialText(currentText);
                return await keyboard.ShowAsync(this);
            }
            finally
            {
                _isOpeningKeyboard = false;
            }
        }

        private async void OnOrderNumberTapped(object sender, EventArgs e)
        {
            var result = await OpenKeyboardAsync(OrderNumberEntry.Text ?? string.Empty);
            if (result != null)
            {
                OrderNumberEntry.Text = result.Trim();
            }
        }

        private async void OnPhoneNumberTapped(object sender, EventArgs e)
        {
            var result = await OpenKeyboardAsync(PhoneNumberEntry.Text ?? string.Empty);
            if (result != null)
            {
                PhoneNumberEntry.Text = result.Trim();
            }
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            var orderNumber = OrderNumberEntry.Text?.Trim() ?? "";
            var phoneNumber = PhoneNumberEntry.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(orderNumber) && string.IsNullOrEmpty(phoneNumber))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Search", "Please enter an Order Number or Phone Number");
                return;
            }

            try
            {
                SearchResults.Clear();
                
                using var connection = await _databaseService.GetConnectionAsync();
                
                // Build search query
                var conditions = new System.Collections.Generic.List<string>();
                
                if (!string.IsNullOrEmpty(orderNumber))
                {
                    conditions.Add("o.order_id LIKE @orderNumber");
                }
                
                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    conditions.Add("o.customer_phone LIKE @phoneNumber");
                }
                
                var whereClause = string.Join(" OR ", conditions);
                
                var query = $@"
                    SELECT o.id, o.order_id, o.order_type, o.total_amount, o.created_at
                    FROM orders o
                    WHERE ({whereClause})
                    AND COALESCE(o.source_channel, 'local') = 'local'
                    AND (COALESCE(LOWER(o.local_lifecycle_state), '') IN ('paid', 'voided') OR LOWER(o.status) IN ('completed', 'closed', 'paid', 'void', 'cancelled'))
                    ORDER BY o.created_at DESC
                    LIMIT 50";
                
                using var command = new MySqlCommand(query, connection);
                
                if (!string.IsNullOrEmpty(orderNumber))
                {
                    command.Parameters.AddWithValue("@orderNumber", $"%{orderNumber}%");
                }
                
                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    command.Parameters.AddWithValue("@phoneNumber", $"%{phoneNumber}%");
                }
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var orderType = reader.GetString("order_type");
                    var createdAt = reader.GetDateTime("created_at");
                    
                    SearchResults.Add(new SearchResultItem
                    {
                        Id = reader.GetInt32("id"),
                        OrderId = reader.GetString("order_id"),
                        OrderType = orderType,
                        OrderIcon = GetOrderIcon(orderType),
                        TotalAmount = reader.GetDecimal("total_amount"),
                        CreatedAt = createdAt,
                        OrderDateTime = $"{createdAt:h:mm tt} • {createdAt:dd/MM/yyyy}"
                    });
                }
                
                ResultsLayout.IsVisible = true;
                NoResultsLabel.IsVisible = SearchResults.Count == 0;
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Search failed: {ex.Message}");
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

        private async void OnResultSelected(object sender, EventArgs e)
        {
            if (sender is VisualElement element && element.BindingContext is SearchResultItem result)
            {
                OrderSelected?.Invoke(result.OrderId, result.CreatedAt.Date);
                await Navigation.PopModalAsync();
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }

    public class SearchResultItem
    {
        public int Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string OrderIcon { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string OrderDateTime { get; set; } = string.Empty;
    }
}
