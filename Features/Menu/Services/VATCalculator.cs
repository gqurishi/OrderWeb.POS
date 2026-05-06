using System;
using System.Collections.Generic;
using System.Linq;
using MyFirstMauiApp.Models.FoodMenu;

namespace MyFirstMauiApp.Services
{
    /// <summary>
    /// Smart VAT Calculator for UK restaurant orders
    /// Handles both standard items and meal deals with mixed hot/cold components
    /// </summary>
    public static class VATCalculator
    {
        /// <summary>
        /// Calculate VAT rate for a menu item based on order type
        /// </summary>
        /// <param name="item">The menu item</param>
        /// <param name="orderType">Order type: Table, DineIn, Takeaway, Collection, or Delivery</param>
        /// <returns>VAT rate as percentage (0-20)</returns>
        public static decimal CalculateVatRate(FoodMenuItem item, string orderType)
        {
            if (item == null)
                return 20m;

            string normalizedOrderType = NormalizeOrderType(orderType);

            // ═══════════════════════════════════════
            // TABLE/DINE-IN ORDERS = ALWAYS FLAT 20%
            // ═══════════════════════════════════════
            if (normalizedOrderType == "Table" || normalizedOrderType == "DineIn")
            {
                return 20m; // Simple! Always 20% for dine-in
            }

            // ═══════════════════════════════════════
            // TAKEAWAY/DELIVERY = SMART CALCULATION
            // ═══════════════════════════════════════
            if (normalizedOrderType == "Takeaway" || normalizedOrderType == "Collection" || normalizedOrderType == "Delivery")
            {
                // --- STANDARD ITEMS ---
                if (item.VatConfigType == "standard")
                {
                    return item.VatCategory switch
                    {
                        "NoVAT" => 0m,
                        "ColdFood" => 0m,      // Cold takeaway = 0% VAT
                        "ColdBeverage" => 0m,  // Cold drinks = 0% VAT
                        "HotFood" => 20m,      // Hot food = 20% VAT
                        "HotBeverage" => 20m,  // Hot drinks = 20% VAT
                        "Alcohol" => 20m,      // Alcohol = 20% VAT
                        _ => 20m               // Default to 20%
                    };
                }

                // --- COMPONENT ITEMS (MEAL DEALS) ---
                if (item.VatConfigType == "component" && item.HasComponents)
                {
                    decimal totalPrice = item.Components.Sum(c => c.ComponentPrice);
                    if (totalPrice == 0) return 20m;

                    decimal totalVat = 0m;

                    foreach (var component in item.Components)
                    {
                        decimal componentVatRate = component.ComponentType switch
                        {
                            "ColdFood" => 0m,      // Cold = 0% for takeaway
                            "ColdBeverage" => 0m,  // Cold = 0% for takeaway
                            "HotFood" => 20m,      // Hot = 20%
                            "HotBeverage" => 20m,  // Hot = 20%
                            "Alcohol" => 20m,      // Alcohol = 20%
                            _ => 20m               // Default
                        };

                        totalVat += (component.ComponentPrice * componentVatRate / 100);
                    }

                    // Return effective VAT rate
                    return Math.Round((totalVat / totalPrice) * 100, 2);
                }
            }

            // Default fallback
            return 20m;
        }

        /// <summary>
        /// Calculate VAT amount in currency for a menu item
        /// </summary>
        /// <param name="item">The menu item</param>
        /// <param name="orderType">Order type</param>
        /// <param name="quantity">Quantity ordered</param>
        /// <returns>VAT amount in currency</returns>
        public static decimal CalculateVatAmount(FoodMenuItem item, string orderType, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return 0m;

            decimal vatRate = CalculateVatRate(item, orderType);
            decimal itemTotal = item.Price * quantity;
            
            return Math.Round(itemTotal * (vatRate / 100), 2);
        }

        /// <summary>
        /// Get VAT breakdown for a component-based item
        /// </summary>
        /// <param name="item">The meal deal item</param>
        /// <param name="orderType">Order type</param>
        /// <returns>Dictionary of component name to VAT amount</returns>
        public static Dictionary<string, decimal> GetComponentVatBreakdown(FoodMenuItem item, string orderType)
        {
            var breakdown = new Dictionary<string, decimal>();

            if (item == null || !item.HasComponents)
                return breakdown;

            string normalizedOrderType = NormalizeOrderType(orderType);

            // Table orders = all 20%
            if (normalizedOrderType == "Table" || normalizedOrderType == "DineIn")
            {
                foreach (var component in item.Components)
                {
                    breakdown[component.ComponentName] = component.ComponentPrice * 0.20m;
                }
                return breakdown;
            }

            // Takeaway/Delivery = smart VAT per component
            foreach (var component in item.Components)
            {
                decimal vatRate = component.ComponentType switch
                {
                    "ColdFood" => 0m,
                    "ColdBeverage" => 0m,
                    "HotFood" => 20m,
                    "HotBeverage" => 20m,
                    "Alcohol" => 20m,
                    _ => 20m
                };

                breakdown[component.ComponentName] = component.ComponentPrice * (vatRate / 100);
            }

            return breakdown;
        }

        private static string NormalizeOrderType(string? orderType)
        {
            if (string.IsNullOrWhiteSpace(orderType))
                return "DineIn";

            string normalized = orderType.Trim().ToLowerInvariant();

            return normalized switch
            {
                "table" => "Table",
                "dinein" or "dine-in" or "dine_in" => "DineIn",
                "takeaway" or "take-away" or "take_away" => "Takeaway",
                "collection" => "Collection",
                "delivery" => "Delivery",
                "pickup" or "pick-up" or "pick_up" => "Collection",
                _ => "DineIn"
            };
        }

        /// <summary>
        /// Get human-readable VAT description for display
        /// </summary>
        /// <param name="item">The menu item</param>
        /// <param name="orderType">Order type</param>
        /// <returns>Description string like "Price includes 20% VAT" or "Mixed VAT (16%)"</returns>
        public static string GetVatDescription(FoodMenuItem item, string orderType)
        {
            if (item == null)
                return "Price includes 20% VAT";

            decimal vatRate = CalculateVatRate(item, orderType);

            if (vatRate == 0)
                return "No VAT applied (0%)";

            if (vatRate == 20)
                return "Price includes 20% VAT";

            // Component items with mixed VAT
            return $"Mixed VAT ({vatRate:F0}% effective)";
        }

        /// <summary>
        /// Calculate net amount (price excluding VAT)
        /// </summary>
        public static decimal CalculateNetAmount(decimal grossAmount, decimal vatRate)
        {
            if (vatRate == 0)
                return grossAmount;

            return Math.Round(grossAmount / (1 + (vatRate / 100)), 2);
        }

        /// <summary>
        /// Get available VAT categories for dropdown
        /// </summary>
        public static List<VatCategoryOption> GetVatCategories()
        {
            return new List<VatCategoryOption>
            {
                new VatCategoryOption("NoVAT", "No VAT (0% always)", "For gift cards, service charges, etc."),
                new VatCategoryOption("HotFood", "Hot Food (20% always)", "Hot takeaway and dine-in food"),
                new VatCategoryOption("ColdFood", "Cold Food (Smart VAT)", "0% takeaway, 20% dine-in"),
                new VatCategoryOption("HotBeverage", "Hot Beverage (20% always)", "Coffee, tea, hot chocolate"),
                new VatCategoryOption("ColdBeverage", "Cold Beverage (Smart VAT)", "Soft drinks, milkshakes, lassi"),
                new VatCategoryOption("Alcohol", "Alcohol (20% always)", "Beer, wine, spirits")
            };
        }

        /// <summary>
        /// Get available component types for meal deals
        /// </summary>
        public static List<ComponentTypeOption> GetComponentTypes()
        {
            return new List<ComponentTypeOption>
            {
                new ComponentTypeOption("HotFood", "Hot Food", "20% VAT", "🔥"),
                new ComponentTypeOption("ColdFood", "Cold Food", "0% VAT (takeaway)", "❄️"),
                new ComponentTypeOption("HotBeverage", "Hot Beverage", "20% VAT", "☕"),
                new ComponentTypeOption("ColdBeverage", "Cold Beverage", "0% VAT (takeaway)", "🥤"),
                new ComponentTypeOption("Alcohol", "Alcohol", "20% VAT", "🍺")
            };
        }
    }

    /// <summary>
    /// VAT category option for dropdowns
    /// </summary>
    public class VatCategoryOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }

        public VatCategoryOption(string value, string label, string description)
        {
            Value = value;
            Label = label;
            Description = description;
        }
    }

    /// <summary>
    /// Component type option for dropdowns
    /// </summary>
    public class ComponentTypeOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public string VatInfo { get; set; }
        public string Icon { get; set; }

        public ComponentTypeOption(string value, string label, string vatInfo, string icon)
        {
            Value = value;
            Label = label;
            VatInfo = vatInfo;
            Icon = icon;
        }
    }
}
