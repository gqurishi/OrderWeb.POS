using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Pages;

public partial class PrintGroupsPage : ContentPage
{
    private readonly PrintGroupService _printGroupService;
    private List<PrintGroup> _printGroups;

    public PrintGroupsPage()
    {
        InitializeComponent();
        _printGroupService = new PrintGroupService();
        _printGroups = new List<PrintGroup>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPrintGroupsAsync();
    }

    private async Task LoadPrintGroupsAsync()
    {
        try
        {
            _printGroups = await _printGroupService.GetAllPrintGroupsAsync();
            _printGroups = _printGroups.OrderBy(g => g.DisplayOrder).ToList();
            RenderPrintGroups();
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load print groups: {ex.Message}");
        }
    }

    private void RenderPrintGroups()
    {
        PrintGroupsList.Clear();

        if (_printGroups.Count == 0)
        {
            PrintGroupsList.Add(new Label
            {
                Text = "No print groups configured. Click 'Add Print Group' to create one.",
                TextColor = Colors.Gray,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0)
            });
            return;
        }

        foreach (var group in _printGroups)
        {
            var card = CreatePrintGroupCard(group);
            PrintGroupsList.Add(card);
        }
    }

    private Border CreatePrintGroupCard(PrintGroup group)
    {
        var border = new Border
        {
            Padding = 15,
            Margin = new Thickness(0, 5),
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb(group.ColorCode ?? "#E5E7EB"),
            StrokeThickness = 1
        };
        border.Shadow = new Shadow
        {
            Brush = Colors.Gray,
            Radius = 5,
            Opacity = 0.3f,
            Offset = new Point(0, 2)
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Color indicator
        var colorBox = new BoxView
        {
            Color = Color.FromArgb(group.ColorCode ?? "#9CA3AF"),
            WidthRequest = 30,
            HeightRequest = 30,
            CornerRadius = 5,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 0, 10, 0)
        };

        // Name and status
        var nameLabel = new Label
        {
            Text = group.Name,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };

        var statusLabel = new Label
        {
            Text = group.StatusText,
            FontSize = 12,
            TextColor = group.IsActive ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var headerStack = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center
        };
        headerStack.Add(colorBox);
        headerStack.Add(nameLabel);
        headerStack.Add(statusLabel);

        Grid.SetRow(headerStack, 0);
        Grid.SetColumn(headerStack, 0);
        grid.Add(headerStack);

        // Printer info
        var printerLabel = new Label
        {
            Text = group.PrinterInfo,
            FontSize = 14,
            TextColor = Colors.Gray,
            Margin = new Thickness(40, 5, 0, 0)
        };

        Grid.SetRow(printerLabel, 1);
        Grid.SetColumn(printerLabel, 0);
        grid.Add(printerLabel);

        // Printer type
        var typeLabel = new Label
        {
            Text = $"Type: {group.PrinterType ?? "Not set"}",
            FontSize = 12,
            TextColor = Colors.Gray,
            Margin = new Thickness(40, 2, 0, 0)
        };

        Grid.SetRow(typeLabel, 2);
        Grid.SetColumn(typeLabel, 0);
        grid.Add(typeLabel);

        // Action buttons
        var buttonStack = new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        var editButton = new Button
        {
            Text = "Edit",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(20, 8),
            FontSize = 14
        };
        editButton.Clicked += async (s, e) => await OnEditPrintGroupClicked(group);

        var toggleButton = new Button
        {
            Text = group.IsActive ? "Disable" : "Enable",
            BackgroundColor = group.IsActive ? Color.FromArgb("#EF4444") : Color.FromArgb("#10B981"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(20, 8),
            FontSize = 14
        };
        toggleButton.Clicked += async (s, e) => await OnTogglePrintGroupClicked(group, toggleButton);

        var deleteButton = new Button
        {
            Text = "Delete",
            BackgroundColor = Color.FromArgb("#6B7280"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(20, 8),
            FontSize = 14
        };
        deleteButton.Clicked += async (s, e) => await OnDeletePrintGroupClicked(group);

        buttonStack.Add(editButton);
        buttonStack.Add(toggleButton);
        buttonStack.Add(deleteButton);

        Grid.SetRow(buttonStack, 0);
        Grid.SetRowSpan(buttonStack, 3);
        Grid.SetColumn(buttonStack, 1);
        grid.Add(buttonStack);

        border.Content = grid;
        return border;
    }

    private async void OnAddPrintGroupClicked(object sender, EventArgs e)
    {
        var newGroup = new PrintGroup
        {
            Id = Guid.NewGuid().ToString(),
            Name = "",
            PrinterType = "Receipt",
            IsActive = true,
            ColorCode = "#9CA3AF",
            DisplayOrder = _printGroups.Count + 1
        };

        await Navigation.PushAsync(new AddEditPrintGroupPage(newGroup, true));
    }

    private async Task OnEditPrintGroupClicked(PrintGroup group)
    {
        await Navigation.PushAsync(new AddEditPrintGroupPage(group, false));
    }

    private async Task OnTogglePrintGroupClicked(PrintGroup group, Button toggleButton)
    {
        try
        {
            await _printGroupService.ToggleActiveAsync(group.Id);
            group.IsActive = !group.IsActive;
            
            toggleButton.Text = group.IsActive ? "Disable" : "Enable";
            toggleButton.BackgroundColor = group.IsActive ? Color.FromArgb("#EF4444") : Color.FromArgb("#10B981");
            
            await LoadPrintGroupsAsync();
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to toggle print group: {ex.Message}");
        }
    }

    private async Task OnDeletePrintGroupClicked(PrintGroup group)
    {
        var confirm = await DisplayAlert("Confirm Delete", 
            $"Are you sure you want to delete '{group.Name}'? Items using this print group will no longer route to a printer.", 
            "Delete", "Cancel");

        if (!confirm)
            return;

        try
        {
            await _printGroupService.DeletePrintGroupAsync(group.Id);
            await LoadPrintGroupsAsync();
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Print group deleted successfully");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to delete print group: {ex.Message}");
        }
    }
}
