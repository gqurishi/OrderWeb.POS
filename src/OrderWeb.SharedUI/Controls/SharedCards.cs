using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

public class DashboardTile : ContentView
{
    private readonly Image _icon;
    private readonly Label _title;
    private readonly Label _subtitle;
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(DashboardTile), string.Empty, propertyChanged: (b, _, v) => ((DashboardTile)b)._title.Text = v?.ToString());
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(DashboardTile), string.Empty, propertyChanged: (b, _, v) => { var c = (DashboardTile)b; c._subtitle.Text = v?.ToString(); c._subtitle.IsVisible = !string.IsNullOrWhiteSpace(c._subtitle.Text); });
    public static readonly BindableProperty IconSourceProperty = BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(DashboardTile), propertyChanged: (b, _, v) => ((DashboardTile)b)._icon.Source = (ImageSource?)v);
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(DashboardTile));
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(DashboardTile));

    public DashboardTile()
    {
        _icon = new Image { WidthRequest = 112, HeightRequest = 112, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center };
        _title = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        _title.Use(Label.TextColorProperty, "OwTextPrimary");
        _subtitle = new Label { FontSize = 13, IsVisible = false, HorizontalTextAlignment = TextAlignment.Center };
        _subtitle.Use(Label.TextColorProperty, "OwTextMuted");
        var card = new Border { Padding = 18, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 20 }, Content = new VerticalStackLayout { Spacing = 10, Children = { _icon, _title, _subtitle } } };
        card.Use(Border.BackgroundColorProperty, "OwSurface"); card.Use(Border.StrokeProperty, "OwBorder");
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { Tapped?.Invoke(this, EventArgs.Empty); if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter); };
        card.GestureRecognizers.Add(tap);
        Content = card;
    }
    public event EventHandler? Tapped;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public ImageSource? IconSource { get => (ImageSource?)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
}

public class SidebarItemView : ContentView
{
    private readonly Grid _row;
    private readonly Image _icon;
    private readonly Label _label;
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(SidebarItemView), string.Empty, propertyChanged: (b, _, v) => ((SidebarItemView)b)._label.Text = v?.ToString());
    public static readonly BindableProperty IconSourceProperty = BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(SidebarItemView), propertyChanged: (b, _, v) => ((SidebarItemView)b)._icon.Source = (ImageSource?)v);
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(SidebarItemView), false, propertyChanged: (b, _, _) => ((SidebarItemView)b).ApplySelection());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SidebarItemView));
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(SidebarItemView));
    public SidebarItemView()
    {
        _icon = new Image { WidthRequest = 30, HeightRequest = 30, Aspect = Aspect.AspectFit };
        _label = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };
        _label.Use(Label.TextColorProperty, "OwTextStrong");
        _row = new Grid { Padding = new Thickness(16, 13), ColumnDefinitions = { new ColumnDefinition(32), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 18 };
        _row.Add(_icon); _row.Add(_label, 1);
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { Tapped?.Invoke(this, EventArgs.Empty); if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter); };
        _row.GestureRecognizers.Add(tap); Content = _row; ApplySelection();
    }
    public event EventHandler? Tapped;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ImageSource? IconSource { get => (ImageSource?)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    private void ApplySelection() { if (IsSelected) _row.Use(Grid.BackgroundColorProperty, "OwPrimarySoft"); else _row.BackgroundColor = Colors.Transparent; }
}

public class TableCard : Border
{
    private readonly Label _title;
    private readonly Label _details;
    private readonly StatusBadge _status;
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(TableCard), "Table", propertyChanged: (b, _, v) => ((TableCard)b)._title.Text = v?.ToString());
    public static readonly BindableProperty DetailsProperty = BindableProperty.Create(nameof(Details), typeof(string), typeof(TableCard), string.Empty, propertyChanged: (b, _, v) => ((TableCard)b)._details.Text = v?.ToString());
    public static readonly BindableProperty StatusProperty = BindableProperty.Create(nameof(Status), typeof(string), typeof(TableCard), "Available", propertyChanged: (b, _, v) => ((TableCard)b)._status.Text = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty StatusKindProperty = BindableProperty.Create(nameof(StatusKind), typeof(StatusKind), typeof(TableCard), StatusKind.Success, propertyChanged: (b, _, v) => ((TableCard)b)._status.Kind = (StatusKind)v);
    public TableCard()
    {
        Padding = 16; StrokeThickness = 1; StrokeShape = new RoundRectangle { CornerRadius = 16 };
        this.Use(BackgroundColorProperty, "OwSurface"); this.Use(StrokeProperty, "OwBorder");
        _title = new Label { Text = "Table", FontSize = 20, FontAttributes = FontAttributes.Bold }; _title.Use(Label.TextColorProperty, "OwTextPrimary");
        _details = new Label { FontSize = 13 }; _details.Use(Label.TextColorProperty, "OwTextMuted");
        _status = new StatusBadge { Text = "Available", Kind = StatusKind.Success, HorizontalOptions = LayoutOptions.Start };
        Content = new VerticalStackLayout { Spacing = 8, Children = { _title, _details, _status } };
    }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Details { get => (string)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public StatusKind StatusKind { get => (StatusKind)GetValue(StatusKindProperty); set => SetValue(StatusKindProperty, value); }
}

public class ProductButton : SharedButton
{
    public ProductButton() { Variant = ButtonVariant.Secondary; HeightRequest = 78; FontSize = 16; LineBreakMode = LineBreakMode.WordWrap; }
}

public class CategoryButton : SharedButton
{
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(CategoryButton), false, propertyChanged: (b, _, v) => ((CategoryButton)b).Variant = (bool)v ? ButtonVariant.Primary : ButtonVariant.Secondary);
    public CategoryButton() { Variant = ButtonVariant.Secondary; HeightRequest = 54; }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
}

public class OrderLineView : ContentView
{
    private readonly Label _quantity;
    private readonly Label _name;
    private readonly Label _notes;
    private readonly Label _total;
    public static readonly BindableProperty QuantityProperty = BindableProperty.Create(nameof(Quantity), typeof(int), typeof(OrderLineView), 1, propertyChanged: (b, _, v) => ((OrderLineView)b)._quantity.Text = $"{v}×");
    public static readonly BindableProperty ProductNameProperty = BindableProperty.Create(nameof(ProductName), typeof(string), typeof(OrderLineView), string.Empty, propertyChanged: (b, _, v) => ((OrderLineView)b)._name.Text = v?.ToString());
    public static readonly BindableProperty NotesProperty = BindableProperty.Create(nameof(Notes), typeof(string), typeof(OrderLineView), string.Empty, propertyChanged: (b, _, v) => { var c = (OrderLineView)b; c._notes.Text = v?.ToString(); c._notes.IsVisible = !string.IsNullOrWhiteSpace(c._notes.Text); });
    public static readonly BindableProperty TotalProperty = BindableProperty.Create(nameof(Total), typeof(string), typeof(OrderLineView), string.Empty, propertyChanged: (b, _, v) => ((OrderLineView)b)._total.Text = v?.ToString());
    public OrderLineView()
    {
        _quantity = Label(16, true); _quantity.WidthRequest = 42;
        _name = Label(16, true); _notes = Label(12, false); _notes.IsVisible = false; _total = Label(16, true); _total.HorizontalTextAlignment = TextAlignment.End;
        var text = new VerticalStackLayout { Spacing = 3, Children = { _name, _notes } };
        var grid = new Grid { Padding = new Thickness(12, 10), ColumnDefinitions = { new ColumnDefinition(42), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        grid.Add(_quantity); grid.Add(text, 1); grid.Add(_total, 2); Content = grid;
    }
    private static Label Label(double size, bool strong) { var l = new Label { FontSize = size, FontAttributes = strong ? FontAttributes.Bold : FontAttributes.None, VerticalTextAlignment = TextAlignment.Center }; l.Use(Microsoft.Maui.Controls.Label.TextColorProperty, strong ? "OwTextPrimary" : "OwTextMuted"); return l; }
    public int Quantity { get => (int)GetValue(QuantityProperty); set => SetValue(QuantityProperty, value); }
    public string ProductName { get => (string)GetValue(ProductNameProperty); set => SetValue(ProductNameProperty, value); }
    public string Notes { get => (string)GetValue(NotesProperty); set => SetValue(NotesProperty, value); }
    public string Total { get => (string)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
}
