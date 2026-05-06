using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Views;

public partial class CreatePrintGroupDialog : ContentView
{
    private TaskCompletionSource<(string? name, string? color)> _taskCompletionSource;
    private string _selectedColor = "#6366F1"; // Default indigo

    private readonly List<(string name, string hex)> _availableColors = new()
    {
        ("Indigo", "#6366F1"),
        ("Green", "#22C55E"),
        ("Orange", "#F97316"),
        ("Red", "#EF4444"),
        ("Purple", "#A855F7"),
        ("Blue", "#3B82F6"),
        ("Pink", "#EC4899"),
        ("Teal", "#14B8A6"),
        ("Yellow", "#EAB308"),
        ("Cyan", "#06B6D4")
    };

    public CreatePrintGroupDialog()
    {
        InitializeComponent();
        _taskCompletionSource = new TaskCompletionSource<(string? name, string? color)>();
        
        RenderColorPicker();
        
        // Focus on entry after a short delay
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            GroupNameEntry.Focus();
        });
    }

    private void RenderColorPicker()
    {
        ColorPicker.Clear();
        
        foreach (var color in _availableColors)
        {
            var isSelected = color.hex == _selectedColor;
            
            var colorButton = new Border
            {
                BackgroundColor = Color.FromArgb(color.hex),
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeThickness = isSelected ? 3 : 0,
                Stroke = Colors.White,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = 0
            };

            // Add shadow for selected
            if (isSelected)
            {
                colorButton.Shadow = new Shadow
                {
                    Brush = Color.FromArgb(color.hex),
                    Offset = new Point(0, 2),
                    Radius = 8,
                    Opacity = 0.5f
                };
            }

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) =>
            {
                _selectedColor = color.hex;
                RenderColorPicker(); // Re-render to show selection
            };
            colorButton.GestureRecognizers.Add(tapGesture);

            // Add checkmark for selected
            if (isSelected)
            {
                colorButton.Content = new Label
                {
                    Text = "✓",
                    FontSize = 18,
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
            }

            ColorPicker.Add(colorButton);
        }
    }

    private void OnGroupNameCompleted(object sender, EventArgs e)
    {
        OnCreateClicked(sender, e);
    }

    private void OnCreateClicked(object sender, EventArgs e)
    {
        var groupName = GroupNameEntry.Text?.Trim();
        
        if (string.IsNullOrWhiteSpace(groupName))
        {
            // Show validation feedback
            GroupNameEntry.Placeholder = "⚠️ Group name is required";
            return;
        }

        _taskCompletionSource.TrySetResult((groupName, _selectedColor));
    }

    public Task<(string? name, string? color)> ShowAsync()
    {
        return _taskCompletionSource.Task;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult((null, null));
    }

    private void OnBackgroundTapped(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult((null, null));
    }
}
