using System;
using System.Threading.Tasks;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;

namespace POS_in_NET.Services
{
    /// <summary>
    /// Service for managing label printing operations
    /// Handles business logic for when and what to print
    /// </summary>
    public class LabelPrintingService
    {
        private readonly BrotherLabelPrinter? _printer;
        private readonly bool _isEnabled;

        public LabelPrintingService(string? printerIp, int printerPort = 9100, bool enabled = false)
        {
            _isEnabled = enabled && !string.IsNullOrEmpty(printerIp);
            
            if (_isEnabled && !string.IsNullOrEmpty(printerIp))
            {
                _printer = new BrotherLabelPrinter(printerIp, printerPort);
            }
        }

        /// <summary>
        /// Print label for a menu item based on its configuration
        /// </summary>
        public async Task<bool> PrintItemLabelAsync(FoodMenuItem item, string? tableNumber = null, int quantity = 1)
        {
            if (!_isEnabled || _printer == null)
            {
                System.Diagnostics.Debug.WriteLine("[INFO] Label printing is disabled");
                return false;
            }

            // Check if item has label text configured
            if (string.IsNullOrWhiteSpace(item.LabelText))
            {
                System.Diagnostics.Debug.WriteLine($"[INFO] No label text for item: {item.Name}");
                return false;
            }

            try
            {
                // Check if this is a meal deal with component printing enabled
                if (item.VatConfigType == "component" && item.PrintComponentLabels && item.Components?.Count > 0)
                {
                    // Print component labels only
                    return await PrintMealDealComponentsAsync(item, tableNumber);
                }
                else
                {
                    // Print standard item label
                    string? additionalInfo = tableNumber != null ? $"Table {tableNumber}" : null;
                    
                    for (int i = 0; i < quantity; i++)
                    {
                        await _printer.PrintItemLabelAsync(
                            itemName: item.Name,
                            customText: item.LabelText,
                            useRedInk: item.PrintInRed,
                            additionalInfo: additionalInfo
                        );
                        
                        if (i < quantity - 1)
                        {
                            await Task.Delay(100); // Delay between multiple labels
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[SUCCESS] Printed {quantity} label(s) for: {item.Name}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to print label for {item.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Print individual labels for each component in a meal deal
        /// </summary>
        private async Task<bool> PrintMealDealComponentsAsync(FoodMenuItem mealDeal, string? tableNumber)
        {
            if (mealDeal.Components == null || mealDeal.Components.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[WARNING] Meal deal has no components");
                return false;
            }

            try
            {
                int totalLabels = 0;
                
                foreach (var component in mealDeal.Components)
                {
                    // Determine component quantity (default to 1 if not specified)
                    int componentQty = DetermineComponentQuantity(component.ComponentName);
                    
                    // Print label for each quantity
                    await _printer.PrintComponentLabelAsync(
                        mealDealName: mealDeal.LabelText ?? mealDeal.Name,
                        componentName: component.ComponentName,
                        componentType: component.ComponentType,
                        quantity: componentQty
                    );
                    
                    totalLabels += componentQty;
                }
                
                System.Diagnostics.Debug.WriteLine($"[SUCCESS] Printed {totalLabels} component labels for: {mealDeal.Name}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to print meal deal components: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract quantity from component name (e.g., "Roti x2" returns 2)
        /// </summary>
        private int DetermineComponentQuantity(string componentName)
        {
            // Look for pattern like "x2", "x3", etc.
            var match = System.Text.RegularExpressions.Regex.Match(componentName, @"x(\d+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success && int.TryParse(match.Groups[1].Value, out int qty))
            {
                return qty;
            }
            
            return 1; // Default quantity
        }

        /// <summary>
        /// Test printer connection
        /// </summary>
        public async Task<bool> TestPrinterConnectionAsync()
        {
            if (!_isEnabled || _printer == null)
            {
                return false;
            }

            try
            {
                return await _printer.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Printer test failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Print a test label
        /// </summary>
        public async Task<bool> PrintTestLabelAsync()
        {
            if (!_isEnabled || _printer == null)
            {
                return false;
            }

            try
            {
                await _printer.PrintTextLabelAsync(
                    text: $"Test Label\n{DateTime.Now:yyyy-MM-dd HH:mm}\nPrinter OK!",
                    useRedInk: false,
                    copies: 1
                );
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Test label failed: {ex.Message}");
                return false;
            }
        }
    }
}
