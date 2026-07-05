using MyFirstMauiApp.Models.FoodMenu;
using Microsoft.Maui.Controls.Shapes;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddonSelectionDialog : ContentView
    {
        private readonly List<Addon> _addons = new();
        private readonly List<SelectedAddon> _selectedAddons = new();
        private readonly Dictionary<string, Border> _rowBorders = new();
        private TaskCompletionSource<List<SelectedAddon>?>? _taskCompletionSource;
        private Grid? _parentGrid;

        public AddonSelectionDialog()
        {
            InitializeComponent();
        }

        public async Task<List<SelectedAddon>?> ShowAsync(string itemName, IEnumerable<Addon> addons)
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();

            _taskCompletionSource = new TaskCompletionSource<List<SelectedAddon>?>();
            _addons.Clear();
            _addons.AddRange(addons.Where(addon => !string.IsNullOrWhiteSpace(addon.Name)));
            _selectedAddons.Clear();
            _rowBorders.Clear();

            TitleLabel.Text = $"Add-ons for {itemName}";
            SubtitleLabel.Text = _addons.Count == 1
                ? "Select an optional extra for this item."
                : "Select one or more optional extras for this item.";

            BuildRows();
            RefreshState();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                return new List<SelectedAddon>();
            }

            return await _taskCompletionSource.Task;
        }

        private void BuildRows()
        {
            AddonsContainer.Children.Clear();

            foreach (var addon in _addons)
            {
                var key = GetAddonKey(addon);
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

                var checkLabel = new Label
                {
                    Text = "",
                    WidthRequest = 28,
                    HeightRequest = 28,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    BackgroundColor = Color.FromArgb("#CBD5E1"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.Center
                };

                var nameLabel = new Label
                {
                    Text = addon.Name,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalOptions = LayoutOptions.Center
                };

                var priceLabel = new Label
                {
                    Text = $"+£{addon.Price:F2}",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#059669"),
                    VerticalOptions = LayoutOptions.Center
                };

                grid.Add(checkLabel, 0, 0);
                grid.Add(nameLabel, 1, 0);
                grid.Add(priceLabel, 2, 0);
                row.Content = grid;

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (_, _) => ToggleAddon(addon);
                row.GestureRecognizers.Add(tapGesture);

                _rowBorders[key] = row;
                AddonsContainer.Children.Add(row);
            }
        }

        private void ToggleAddon(Addon addon)
        {
            var existing = _selectedAddons.FirstOrDefault(selected => AreSameAddon(selected, addon));
            if (existing != null)
            {
                _selectedAddons.Remove(existing);
            }
            else
            {
                _selectedAddons.Add(new SelectedAddon
                {
                    Id = string.IsNullOrWhiteSpace(addon.Id) ? addon.Name : addon.Id,
                    Name = addon.Name,
                    Price = addon.Price
                });
            }

            RefreshState();
        }

        private void RefreshState()
        {
            foreach (var addon in _addons)
            {
                var key = GetAddonKey(addon);
                if (!_rowBorders.TryGetValue(key, out var row))
                {
                    continue;
                }

                var selected = _selectedAddons.Any(selectedAddon => AreSameAddon(selectedAddon, addon));
                row.BackgroundColor = selected ? Color.FromArgb("#EFF6FF") : Colors.White;
                row.Stroke = selected ? Color.FromArgb("#60A5FA") : Color.FromArgb("#E2E8F0");
                row.StrokeThickness = selected ? 2 : 1;

                if (row.Content is Grid grid && grid.Children.FirstOrDefault() is Label checkLabel)
                {
                    checkLabel.Text = selected ? "✓" : "";
                    checkLabel.BackgroundColor = selected ? Color.FromArgb("#2563EB") : Color.FromArgb("#CBD5E1");
                }
            }

            var selectedCount = _selectedAddons.Count;
            var selectedTotal = _selectedAddons.Sum(addon => addon.Price);
            DoneButton.Text = selectedCount == 0 ? "No Add-ons" : "Add Selected";
            SelectionSummaryBorder.IsVisible = selectedCount > 0;
            SelectionSummaryLabel.Text = $"{selectedCount} selected · +£{selectedTotal:F2}";
        }

        private void OnDoneClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(_selectedAddons.ToList());
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

        private static bool AreSameAddon(SelectedAddon selectedAddon, Addon addon)
        {
            var selectedId = (selectedAddon.Id ?? string.Empty).Trim();
            var addonId = (addon.Id ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(selectedId) && !string.IsNullOrWhiteSpace(addonId))
            {
                return string.Equals(selectedId, addonId, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(selectedAddon.Name?.Trim(), addon.Name?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetAddonKey(Addon addon)
        {
            return string.IsNullOrWhiteSpace(addon.Id)
                ? addon.Name.Trim()
                : addon.Id.Trim();
        }
    }
}
