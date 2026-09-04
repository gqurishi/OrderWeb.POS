using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class VariantSelectionDialog : ContentView
    {
        private readonly List<MenuItemVariant> _variants = new();
        private TaskCompletionSource<MenuItemVariant?>? _taskCompletionSource;
        private Grid? _parentGrid;

        public VariantSelectionDialog()
        {
            InitializeComponent();
        }

        public async Task<MenuItemVariant?> ShowAsync(string itemName, IEnumerable<MenuItemVariant> variants)
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();

            _taskCompletionSource = new TaskCompletionSource<MenuItemVariant?>();
            _variants.Clear();
            _variants.AddRange(variants.Where(variant => variant.Active && !string.IsNullOrWhiteSpace(variant.Name)));

            ItemNameLabel.Text = itemName;
            SubtitleLabel.Text = _variants.Count == 1
                ? "Select the available size before adding this item."
                : "Select one size or portion before adding this item.";

            BuildRows();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                return null;
            }

            return await _taskCompletionSource.Task;
        }

        private void BuildRows()
        {
            VariantsContainer.Children.Clear();

            foreach (var variant in _variants)
            {
                var row = new Border
                {
                    BackgroundColor = Colors.White,
                    Stroke = Color.FromArgb("#E2E8F0"),
                    StrokeThickness = 1,
                    Padding = new Thickness(14, 12),
                    StrokeShape = new RoundRectangle { CornerRadius = 12 }
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 12
                };

                var marker = new Border
                {
                    WidthRequest = 30,
                    HeightRequest = 30,
                    BackgroundColor = Color.FromArgb("#EFF6FF"),
                    Stroke = Color.FromArgb("#93C5FD"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 15 },
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "£",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#2563EB"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center
                    }
                };

                var textStack = new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center
                };

                textStack.Children.Add(new Label
                {
                    Text = variant.Name,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A"),
                    LineBreakMode = LineBreakMode.TailTruncation
                });

                if (!string.IsNullOrWhiteSpace(variant.Description))
                {
                    textStack.Children.Add(new Label
                    {
                        Text = variant.Description.Trim(),
                        FontSize = 13,
                        TextColor = Color.FromArgb("#64748B"),
                        LineBreakMode = LineBreakMode.TailTruncation
                    });
                }

                var priceLabel = new Label
                {
                    Text = $"£{variant.Price:F2}",
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#059669"),
                    VerticalOptions = LayoutOptions.Center
                };

                grid.Add(marker, 0, 0);
                grid.Add(textStack, 1, 0);
                grid.Add(priceLabel, 2, 0);
                row.Content = grid;

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (_, _) => SelectVariant(variant);
                row.GestureRecognizers.Add(tapGesture);

                VariantsContainer.Children.Add(row);
            }
        }

        private void SelectVariant(MenuItemVariant variant)
        {
            _taskCompletionSource?.TrySetResult(variant);
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(null);
            CloseDialog();
        }

        private void CloseDialog()
        {
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
