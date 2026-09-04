using Microsoft.Maui.Controls;
using System;
using System.Text.RegularExpressions;

namespace POS_in_NET.Pages
{
    public partial class CustomColorPickerPage : ContentPage
    {
        private string _selectedColor = "#3B82F6";
        
        // Popular colors for quick pick
        private readonly List<(string Name, string Hex)> _popularColors = new()
        {
            ("Crimson Red", "#DC143C"),
            ("Coral", "#FF7F50"),
            ("Gold", "#FFD700"),
            ("Lime Green", "#32CD32"),
            ("Forest Green", "#228B22"),
            ("Sky Blue", "#87CEEB"),
            ("Royal Blue", "#4169E1"),
            ("Navy", "#000080"),
            ("Violet", "#8B00FF"),
            ("Hot Pink", "#FF69B4"),
            ("Magenta", "#FF00FF"),
            ("Teal", "#008080"),
            ("Turquoise", "#40E0D0"),
            ("Orange Red", "#FF4500"),
            ("Chocolate", "#D2691E"),
            ("Salmon", "#FA8072"),
            ("Mint", "#98FF98"),
            ("Lavender", "#E6E6FA"),
        };

        public string SelectedColor => _selectedColor;

        public CustomColorPickerPage(string initialColor = "#3B82F6")
        {
            InitializeComponent();
            _selectedColor = initialColor;
            
            // Set initial color
            ColorInput.Text = initialColor.TrimStart('#');
            UpdateColorPreview(initialColor);
            
            // Setup quick pick colors
            SetupQuickPickColors();
        }

        private void SetupQuickPickColors()
        {
            foreach (var (name, hex) in _popularColors)
            {
                var colorButton = new Frame
                {
                    BackgroundColor = Color.FromArgb(hex),
                    CornerRadius = 20,
                    Padding = 0,
                    HasShadow = false,
                    WidthRequest = 40,
                    HeightRequest = 40,
                    Margin = new Thickness(4)
                };

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) =>
                {
                    ColorInput.Text = hex.TrimStart('#');
                    UpdateColorPreview(hex);
                };
                colorButton.GestureRecognizers.Add(tapGesture);

                // Add tooltip/label
                var container = new VerticalStackLayout
                {
                    Spacing = 4,
                    HorizontalOptions = LayoutOptions.Center
                };
                
                container.Children.Add(colorButton);
                
                var label = new Label
                {
                    Text = name,
                    FontSize = 9,
                    TextColor = Color.FromArgb("#64748B"),
                    HorizontalOptions = LayoutOptions.Center,
                    MaximumWidthRequest = 50,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                container.Children.Add(label);

                QuickPickContainer.Children.Add(container);
            }
        }

        private void OnColorInputChanged(object sender, TextChangedEventArgs e)
        {
            var input = e.NewTextValue?.Trim() ?? "";
            
            // Remove # if user typed it
            input = input.TrimStart('#');
            
            // Only allow valid hex characters
            input = Regex.Replace(input, @"[^0-9A-Fa-f]", "");
            
            if (input.Length > 6)
            {
                input = input.Substring(0, 6);
            }

            // Update entry without triggering TextChanged again
            if (ColorInput.Text != input)
            {
                ColorInput.Text = input;
                return;
            }

            // Validate and update preview
            if (input.Length == 6)
            {
                var hexColor = $"#{input.ToUpper()}";
                if (IsValidHexColor(hexColor))
                {
                    UpdateColorPreview(hexColor);
                    ValidationFrame.IsVisible = false;
                    ApplyButton.IsEnabled = true;
                }
                else
                {
                    ShowValidationError("Invalid color code");
                    ApplyButton.IsEnabled = false;
                }
            }
            else if (input.Length == 3)
            {
                // Support 3-digit hex codes (e.g., F00 = FF0000)
                var expandedHex = $"#{input[0]}{input[0]}{input[1]}{input[1]}{input[2]}{input[2]}".ToUpper();
                UpdateColorPreview(expandedHex);
                ValidationFrame.IsVisible = false;
                ApplyButton.IsEnabled = true;
            }
            else
            {
                ValidationFrame.IsVisible = false;
                ApplyButton.IsEnabled = false;
            }
        }

        private bool IsValidHexColor(string hex)
        {
            return Regex.IsMatch(hex, @"^#[0-9A-Fa-f]{6}$");
        }

        private void UpdateColorPreview(string hexColor)
        {
            try
            {
                var color = Color.FromArgb(hexColor);
                ColorPreviewFrame.BackgroundColor = color;
                ColorCodeLabel.Text = hexColor.ToUpper();
                _selectedColor = hexColor.ToUpper();
                
                // Update shadow color to match
                var shadow = new Shadow
                {
                    Brush = new SolidColorBrush(color),
                    Opacity = 0.4f,
                    Radius = 20,
                    Offset = new Point(0, 4)
                };
                ColorPreviewFrame.Shadow = shadow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating color preview: {ex.Message}");
            }
        }

        private void ShowValidationError(string message)
        {
            ValidationMessage.Text = message;
            ValidationFrame.IsVisible = true;
        }

        private async void OnApplyClicked(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ColorInput.Text) && ColorInput.Text.Length >= 3)
            {
                // Ensure we have a 6-digit hex
                var input = ColorInput.Text.TrimStart('#');
                if (input.Length == 3)
                {
                    input = $"{input[0]}{input[0]}{input[1]}{input[1]}{input[2]}{input[2]}";
                }
                
                _selectedColor = $"#{input.ToUpper()}";
                await Navigation.PopModalAsync();
            }
            else
            {
                ShowValidationError("Please enter a valid color code (3 or 6 characters)");
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _selectedColor = null!; // Signal that user cancelled
            await Navigation.PopModalAsync();
        }
    }
}
