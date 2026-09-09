using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>Canonical shared POS order surface. Hosts own all mutations.</summary>
public sealed class OrderEntryView : ContentView
{
    private readonly Grid _categories;
    private readonly Grid _products;
    private readonly VerticalStackLayout _lines;
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _subtotal;
    private readonly Label _tax;
    private readonly Label _service;
    private readonly Label _total;
    private readonly PosLoadingOverlay _loading;
    private readonly PosToast _error;
    private OrderEntryViewModel? _viewModel;

    public OrderEntryView()
    {
        _title = Label(25, true, "OwTextStrong");
        _status = Label(13, false, "OwTextMuted");
        _categories = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        _products = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        _lines = new VerticalStackLayout { Spacing = 4 };
        _subtotal = MoneyLabel(); _tax = MoneyLabel(); _service = MoneyLabel(); _total = MoneyLabel(22, true);

        var menu = Card(new VerticalStackLayout { Spacing = 14, Children = { Label("Categories", 16, true, "OwTextStrong"), new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _categories }, new ScrollView { Content = _products } } });
        var basketRows = new VerticalStackLayout { Spacing = 8, Children = { Row("Subtotal", _subtotal), Row("Tax", _tax), Row("Service", _service), Row("Total", _total) } };
        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, ColumnSpacing = 8, RowSpacing = 8 };
        AddAction(actions, "Discount", OrderEntryAction.Discount, 0, 0, ButtonVariant.Secondary);
        AddAction(actions, "More", OrderEntryAction.More, 0, 1, ButtonVariant.Secondary);
        AddAction(actions, "Void", OrderEntryAction.Void, 1, 0, ButtonVariant.Danger);
        AddAction(actions, "Send Order", OrderEntryAction.Send, 1, 1, ButtonVariant.Primary);
        var lineScroll = new ScrollView { Content = _lines };
        var basketGrid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, RowSpacing = 12 };
        basketGrid.Add(_title); basketGrid.Add(_status, 0, 1); basketGrid.Add(lineScroll, 0, 2); basketGrid.Add(basketRows, 0, 3); basketGrid.Add(actions, 0, 4);
        var basket = Card(basketGrid);
        var content = new Grid { Padding = new Thickness(18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(420) }, ColumnSpacing = 18 };
        content.Use(Grid.BackgroundColorProperty, "OwBackground"); content.Add(menu); content.Add(basket, 1);
        _loading = new PosLoadingOverlay { IsVisible = false, IsLoading = false, ZIndex = 10, Message = "Cooking up your data…" };
        _error = new PosToast { IsVisible = false, Kind = StatusKind.Error, IsRetryVisible = true, ZIndex = 11 };
        _error.DismissRequested += (_, _) => _error.IsVisible = false;
        Content = new Grid { Children = { content, _loading, _error } };
        SizeChanged += (_, _) => ApplyResponsiveLayout(content, menu, basket);
    }

    public OrderEntryViewModel? ViewModel { get => _viewModel; set { if (_viewModel == value) return; if (_viewModel is not null) _viewModel.PropertyChanged -= OnChanged; _viewModel = value; if (value is not null) { value.PropertyChanged += OnChanged; value.ProductSelected += OnProduct; value.ActionRequested += OnAction; } Rebuild(); } }
    public event EventHandler<OrderProductModel>? ProductSelected;
    public event EventHandler<OrderEntryActionRequest>? ActionRequested;
    private void OnProduct(object? sender, OrderProductModel product) => ProductSelected?.Invoke(this, product);
    private void OnAction(object? sender, OrderEntryActionRequest request) => ActionRequested?.Invoke(this, request);
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();
    private void Rebuild()
    {
        if (_viewModel is null) return;
        _title.Text = _viewModel.Title; _status.Text = _viewModel.HasConflict ? "Conflict — refresh before continuing." : _viewModel.Status;
        _subtotal.Text = Money(_viewModel.Subtotal); _tax.Text = Money(_viewModel.Tax); _service.Text = Money(_viewModel.ServiceCharge); _total.Text = Money(_viewModel.Total);
        _loading.IsVisible = _viewModel.IsLoading;
        _loading.IsLoading = _viewModel.IsLoading;
        if (_viewModel.IsLoading)
        {
            _loading.Message = "Cooking up your data…";
        }
        _error.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.ErrorMessage); _error.Title = "Order needs attention"; _error.Message = _viewModel.ErrorMessage ?? string.Empty;
        _categories.Children.Clear(); _categories.ColumnDefinitions.Clear();
        var index = 0; foreach (var category in _viewModel.Categories) { _categories.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); var button = new CategoryButton { Text = category.Name, IsSelected = category.Id == _viewModel.SelectedCategoryId, IsEnabled = category.IsAvailable, Command = _viewModel.SelectCategoryCommand, CommandParameter = category }; _categories.Add(button, index++); }
        _products.Children.Clear(); _products.ColumnDefinitions.Clear(); _products.RowDefinitions.Clear(); for (var i = 0; i < 3; i++) _products.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var productIndex = 0; foreach (var product in _viewModel.VisibleProducts) { if (productIndex % 3 == 0) _products.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var button = new ProductButton { Text = $"{product.Name}\n{Money(product.Price)}", IsEnabled = product.IsAvailable, Command = _viewModel.SelectProductCommand, CommandParameter = product }; _products.Add(button, productIndex % 3, productIndex / 3); productIndex++; }
        _lines.Children.Clear(); foreach (var line in _viewModel.Lines) { var lineView = new OrderLineView { Quantity = (int)line.Quantity, ProductName = line.Name, Notes = line.Notes ?? string.Empty, Total = Money(line.Total) }; var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => ActionRequested?.Invoke(this, new OrderEntryActionRequest(OrderEntryAction.Quantity, line.Id)); lineView.GestureRecognizers.Add(tap); _lines.Children.Add(lineView); }
    }
    private void AddAction(Grid grid, string text, OrderEntryAction action, int row, int column, ButtonVariant variant) { var button = new SharedButton { Text = text, Variant = variant, Command = new Command(() => ActionRequested?.Invoke(this, new OrderEntryActionRequest(action))) }; grid.Add(button, column, row); }
    private static Border Card(View content) { var card = new Border { Padding = 16, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = content }; card.Use(Border.BackgroundColorProperty, "OwSurface"); card.Use(Border.StrokeProperty, "OwBorder"); return card; }
    private static Label Label(string text, double size, bool bold, string color) { var label = Label(size, bold, color); label.Text = text; return label; }
    private static Label Label(double size, bool bold, string color) { var label = new Microsoft.Maui.Controls.Label { FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None }; label.Use(Microsoft.Maui.Controls.Label.TextColorProperty, color); return label; }
    private static Label MoneyLabel(double size = 15, bool bold = false) { var value = new Microsoft.Maui.Controls.Label { FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, HorizontalTextAlignment = TextAlignment.End }; value.Use(Microsoft.Maui.Controls.Label.TextColorProperty, bold ? "OwTextStrong" : "OwTextPrimary"); return value; }
    private static Grid Row(string name, Label value) { var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } }; row.Add(Label(name, 14, false, "OwTextMuted")); row.Add(value, 1); return row; }
    private static string Money(decimal value) => value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    private static void ApplyResponsiveLayout(Grid content, View menu, View basket) { if (content.Width > 0 && content.Width < 1100) { content.ColumnDefinitions.Clear(); content.RowDefinitions.Clear(); content.RowDefinitions.Add(new RowDefinition(GridLength.Star)); content.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetColumn(menu, 0); Grid.SetColumn(basket, 0); Grid.SetRow(menu, 0); Grid.SetRow(basket, 1); } }
}
