using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Views;

public partial class PrintGroupDialog : ContentView
{
    private TaskCompletionSource<string?> _taskCompletionSource;
    private string? _selectedPrintGroupId;

    public PrintGroupDialog(List<PrintGroup> printGroups, string? currentSelectionId)
    {
        InitializeComponent();
        _taskCompletionSource = new TaskCompletionSource<string?>();
        _selectedPrintGroupId = currentSelectionId;
        
        RenderPrintGroups(printGroups);
    }

    private void RenderPrintGroups(List<PrintGroup> printGroups)
    {
        PrintGroupsList.Clear();
        
        // Add "None" option
        var noneButton = CreatePrintGroupButton(
            "None (No printer routing)",
            "Skip automatic printing to kitchen stations",
            null,
            "#94A3B8",
            null
        );
        PrintGroupsList.Add(noneButton);
        
        // Add separator
        PrintGroupsList.Add(new BoxView 
        { 
            HeightRequest = 1, 
            BackgroundColor = Color.FromArgb("#E2E8F0"),
            Margin = new Thickness(0, 8)
        });
        
        // Add each print group
        foreach (var group in printGroups)
        {
            var button = CreatePrintGroupButton(
                group.Name,
                group.PrinterInfo,
                group.Id,
                group.ColorCode ?? "#6366F1",
                group.IsActive ? null : "(Disabled)"
            );
            
            PrintGroupsList.Add(button);
        }
    }

    private Border CreatePrintGroupButton(string name, string description, string? id, string colorCode, string? statusText)
    {
        var isSelected = (id == _selectedPrintGroupId) || (id == null && _selectedPrintGroupId == null);
        
        var border = new Border
        {
            BackgroundColor = isSelected ? Color.FromArgb("#F0F9FF") : Colors.White,
            StrokeThickness = 2,
            Stroke = isSelected ? Color.FromArgb(colorCode) : Color.FromArgb("#E2E8F0"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 16,
            Margin = new Thickness(0, 4)
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => OnPrintGroupSelected(id);
        border.GestureRecognizers.Add(tapGesture);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12
        };

        // Color indicator
        var colorIndicator = new BoxView
        {
            Color = Color.FromArgb(colorCode),
            WidthRequest = 8,
            HeightRequest = 40,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(colorIndicator, 0, 0);

        // Text content
        var textStack = new VerticalStackLayout { Spacing = 4 };
        
        var nameStack = new HorizontalStackLayout { Spacing = 8 };
        nameStack.Add(new Label
        {
            Text = name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalOptions = LayoutOptions.Center
        });
        
        if (!string.IsNullOrEmpty(statusText))
        {
            nameStack.Add(new Label
            {
                Text = statusText,
                FontSize = 12,
                TextColor = Color.FromArgb("#EF4444"),
                VerticalOptions = LayoutOptions.Center
            });
        }
        
        textStack.Add(nameStack);
        textStack.Add(new Label
        {
            Text = description,
            FontSize = 13,
            TextColor = Color.FromArgb("#64748B")
        });
        
        grid.Add(textStack, 1, 0);

        // Checkmark for selected
        if (isSelected)
        {
            grid.Add(new Label
            {
                Text = "✓",
                FontSize = 24,
                TextColor = Color.FromArgb(colorCode),
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            }, 2, 0);
        }

        border.Content = grid;
        return border;
    }

    private void OnPrintGroupSelected(string? printGroupId)
    {
        _taskCompletionSource.TrySetResult(printGroupId);
    }

    public Task<string?> ShowAsync()
    {
        return _taskCompletionSource.Task;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult(_selectedPrintGroupId); // Return original selection
    }

    private void OnBackgroundTapped(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult(_selectedPrintGroupId); // Return original selection
    }
}
