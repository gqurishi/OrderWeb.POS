using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using POS_in_NET.Models;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public sealed class PayByItemsResult
    {
        public bool Success { get; set; }
        public decimal Amount { get; set; }
        public List<PayByItemsSelection> SelectedItems { get; set; } = new();
    }

    public sealed class PayByItemsSelection
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    public partial class PayByItemsDialog : ContentView
    {
        private readonly List<PayByItemsLine> _lines = new();
        private TaskCompletionSource<PayByItemsResult>? _taskCompletionSource;
        private Grid? _parentGrid;
        private decimal _orderSubtotal;
        private decimal _serviceCharge;
        private decimal _discount;
        private decimal _remainingBalance;
        private decimal _amountDue;

        public PayByItemsDialog()
        {
            InitializeComponent();
        }

        public void SetOrder(
            IEnumerable<TableOrderItem> items,
            decimal orderSubtotal,
            decimal serviceCharge,
            decimal discount,
            decimal remainingBalance)
        {
            _orderSubtotal = Math.Max(0, orderSubtotal);
            _serviceCharge = Math.Max(0, serviceCharge);
            _discount = Math.Max(0, discount);
            _remainingBalance = Math.Max(0, remainingBalance);

            _lines.Clear();
            ItemsContainer.Children.Clear();

            foreach (var item in items.Where(item => !item.IsVoided && item.Quantity > 0))
            {
                var line = new PayByItemsLine(item);
                _lines.Add(line);
                ItemsContainer.Children.Add(CreateLineView(line));
            }

            UpdateSummary();
        }

        public async Task<PayByItemsResult> ShowAsync()
        {
            using var idleGuard = POS_in_NET.Services.ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
            _taskCompletionSource = new TaskCompletionSource<PayByItemsResult>();

            if (Application.Current?.MainPage != null)
            {
                var pageContent = GetPageContent(Application.Current.MainPage);
                if (pageContent is Grid mainGrid)
                {
                    _parentGrid = mainGrid;

                    Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                    Grid.SetRow(this, 0);
                    Grid.SetColumn(this, 0);

                    mainGrid.Children.Add(this);
                }
            }

            return await _taskCompletionSource.Task;
        }

        private View CreateLineView(PayByItemsLine line)
        {
            var border = new Border
            {
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                Stroke = Color.FromArgb("#D8E1ED"),
                StrokeThickness = 1,
                Padding = new Thickness(14, 10)
            };
            border.StrokeShape = new RoundRectangle { CornerRadius = 12 };

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(104) },
                    new ColumnDefinition { Width = new GridLength(128) },
                    new ColumnDefinition { Width = new GridLength(96) }
                },
                ColumnSpacing = 16
            };

            var itemStack = new VerticalStackLayout { Spacing = 2 };
            itemStack.Children.Add(new Label
            {
                Text = line.Item.Name,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#111827")
            });
            itemStack.Children.Add(new Label
            {
                Text = line.Item.ModifiersDisplay,
                FontSize = 12,
                TextColor = Color.FromArgb("#64748B"),
                IsVisible = !string.IsNullOrWhiteSpace(line.Item.ModifiersDisplay),
                LineBreakMode = LineBreakMode.TailTruncation
            });
            grid.Add(itemStack, 0, 0);

            var quantityLabel = new Label
            {
                Text = $"x{line.Item.Quantity}",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#334155"),
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center
            };
            grid.Add(quantityLabel, 1, 0);

            var selectorGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(34) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(34) }
                },
                ColumnSpacing = 8
            };

            var minusButton = CreateQuantityButton("-");
            minusButton.Clicked += (_, _) =>
            {
                if (line.SelectedQuantity > 0)
                {
                    line.SelectedQuantity--;
                    UpdateLine(line);
                }
            };
            selectorGrid.Add(minusButton, 0, 0);

            line.SelectedQuantityLabel = new Label
            {
                Text = "0",
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#111827"),
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center
            };
            selectorGrid.Add(line.SelectedQuantityLabel, 1, 0);

            var plusButton = CreateQuantityButton("+");
            plusButton.Clicked += (_, _) =>
            {
                if (line.SelectedQuantity < line.Item.Quantity)
                {
                    line.SelectedQuantity++;
                    UpdateLine(line);
                }
            };
            selectorGrid.Add(plusButton, 2, 0);
            grid.Add(selectorGrid, 2, 0);

            line.AmountLabel = new Label
            {
                Text = "£0.00",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#059669"),
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End
            };
            grid.Add(line.AmountLabel, 3, 0);

            border.Content = grid;
            return border;
        }

        private static Button CreateQuantityButton(string text)
        {
            return new Button
            {
                Text = text,
                BackgroundColor = Color.FromArgb("#E0ECFF"),
                TextColor = Color.FromArgb("#1D4ED8"),
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                WidthRequest = 34,
                HeightRequest = 34,
                Padding = 0,
                CornerRadius = 8
            };
        }

        private void UpdateLine(PayByItemsLine line)
        {
            if (line.SelectedQuantityLabel != null)
            {
                line.SelectedQuantityLabel.Text = line.SelectedQuantity.ToString();
            }

            if (line.AmountLabel != null)
            {
                line.AmountLabel.Text = $"£{line.SelectedAmount:F2}";
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var selectedQuantity = _lines.Sum(line => line.SelectedQuantity);
            var selectedSubtotal = _lines.Sum(line => line.SelectedAmount);
            var shareRatio = _orderSubtotal > 0 ? selectedSubtotal / _orderSubtotal : 0;
            var serviceShare = Math.Round(_serviceCharge * shareRatio, 2);
            var discountShare = Math.Round(_discount * shareRatio, 2);
            var adjustments = serviceShare - discountShare;
            _amountDue = Math.Round(selectedSubtotal + adjustments, 2);

            if (_amountDue > _remainingBalance)
            {
                _amountDue = _remainingBalance;
            }

            SelectedItemsLabel.Text = selectedQuantity == 0
                ? "No items selected"
                : $"{selectedQuantity} item(s) selected";
            SelectedSubtotalLabel.Text = $"Selected items: £{selectedSubtotal:F2}";
            AdjustmentsLabel.Text = $"Service/discount share: £{adjustments:F2}";
            AmountDueLabel.Text = $"£{_amountDue:F2}";
            RemainingAfterLabel.Text = $"Remaining after: £{Math.Max(0, _remainingBalance - _amountDue):F2}";
            TakePaymentButton.IsEnabled = _amountDue > 0;
        }

        private void OnTakePaymentClicked(object? sender, EventArgs e)
        {
            if (_amountDue <= 0)
            {
                return;
            }

            _taskCompletionSource?.TrySetResult(new PayByItemsResult
            {
                Success = true,
                Amount = _amountDue,
                SelectedItems = _lines
                    .Where(line => line.SelectedQuantity > 0)
                    .Select(line => new PayByItemsSelection
                    {
                        ItemId = line.Item.Id,
                        ItemName = line.Item.Name,
                        Quantity = line.SelectedQuantity,
                        Amount = line.SelectedAmount
                    })
                    .ToList()
            });
            CloseDialog();
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new PayByItemsResult { Success = false });
            CloseDialog();
        }

        private static View? GetPageContent(Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage currentPage)
            {
                return currentPage.Content;
            }

            return page is ContentPage contentPage ? contentPage.Content : null;
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }

        private sealed class PayByItemsLine
        {
            public PayByItemsLine(TableOrderItem item)
            {
                Item = item;
            }

            public TableOrderItem Item { get; }
            public int SelectedQuantity { get; set; }
            public Label? SelectedQuantityLabel { get; set; }
            public Label? AmountLabel { get; set; }

            public decimal UnitAmount
            {
                get
                {
                    var lineTotal = Item.TotalPriceWithVat > 0 ? Item.TotalPriceWithVat : Item.TotalPrice;
                    return Item.Quantity <= 0 ? 0 : Math.Round(lineTotal / Item.Quantity, 2);
                }
            }

            public decimal SelectedAmount => Math.Round(UnitAmount * SelectedQuantity, 2);
        }
    }
}
