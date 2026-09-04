using POS_in_NET.Views;
using MyFirstMauiApp.Models.FoodMenu;

namespace POS_in_NET.Pages;

public class AddAddonPopup
{
    public string? AddonName { get; set; }
    public decimal AddonPrice { get; set; }
    public bool WasSaved { get; set; }

    public static async Task<AddAddonPopup?> ShowAsync(Page parent)
    {
        // Use the modern AddAddonDialog view
        var dialog = new AddAddonDialog();
        
        // Add dialog to parent page grid
        if (parent is ContentPage contentPage && contentPage.Content is Grid parentGrid)
        {
            Grid.SetRowSpan(dialog, parentGrid.RowDefinitions.Count > 0 ? parentGrid.RowDefinitions.Count : 1);
            Grid.SetColumnSpan(dialog, parentGrid.ColumnDefinitions.Count > 0 ? parentGrid.ColumnDefinitions.Count : 1);
            parentGrid.Children.Add(dialog);
            
            var result = await dialog.ShowAsync();
            
            parentGrid.Children.Remove(dialog);
            
            if (result != null)
            {
                return new AddAddonPopup
                {
                    AddonName = result.Name,
                    AddonPrice = result.Price,
                    WasSaved = true
                };
            }
        }
        
        return null;
    }
}
