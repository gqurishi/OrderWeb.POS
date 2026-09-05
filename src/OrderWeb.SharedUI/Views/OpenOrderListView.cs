using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Orders;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-style open-order board (All / Collection / Delivery / Table, 220px cards).
/// Visual source: Mother LiveOrderPage (green #10B981, stale #DC2626).
/// </summary>
public sealed class OpenOrderListView : ContentView
{
    public static readonly BindableProperty ModelProperty =
        BindableProperty.Create(nameof(Model), typeof(OpenOrderListDto), typeof(OpenOrderListView), propertyChanged: OnModelChanged);

    public static readonly BindableProperty OpenOrderCommandProperty =
        BindableProperty.Create(nameof(OpenOrderCommand), typeof(ICommand), typeof(OpenOrderListView));

    private readonly StaleDataBannerView _staleBanner = new();
    private readonly HorizontalStackLayout _tabs = new()
    {
        Spacing = 12,
        HorizontalOptions = LayoutOptions.Center,
        Padding = new Thickness(16, 8)
    };
    private readonly FlexLayout _cardHost = new()
    {
        Direction = FlexDirection.Row,
        Wrap = FlexWrap.Wrap,
        JustifyContent = FlexJustify.Start,
        AlignItems = FlexAlignItems.Start
    };
    private readonly Label _emptyLabel = new()
    {
        Text = "No open local orders",
        FontSize = 16,
        TextColor = Color.FromArgb("#9CA3AF"),
        HorizontalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 40, 0, 0),
        IsVisible = false
    };
    private readonly Grid _loadingOverlay;
    private OpenOrderChannelKind _selectedChannel = OpenOrderChannelKind.All;

    public OpenOrderListView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Loading open orders…");

        var tabBar = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Content = _tabs
        };

        var body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(20, 12, 20, 24),
                Children = { _emptyLabel, _cardHost }
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb(CustomerOrderUiHelpers.PageBackground),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children = { _staleBanner, tabBar, body, _loadingOverlay }
        };
        Grid.SetRow(tabBar, 1);
        Grid.SetRow(body, 2);
        Grid.SetRowSpan(_loadingOverlay, 3);
    }

    public OpenOrderListDto? Model
    {
        get => (OpenOrderListDto?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public ICommand? OpenOrderCommand
    {
        get => (ICommand?)GetValue(OpenOrderCommandProperty);
        set => SetValue(OpenOrderCommandProperty, value);
    }

    public event EventHandler<OpenOrderChannelKind>? ChannelChanged;
    public event EventHandler<OpenOrderCardDto>? OrderSelected;

    public void Apply(OpenOrderListDto state) => Model = state;

    private static void OnModelChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((OpenOrderListView)bindable).Render();

    private void Render()
    {
        var model = Model;
        if (model is null)
        {
            _loadingOverlay.IsVisible = false;
            _cardHost.Children.Clear();
            _emptyLabel.IsVisible = true;
            return;
        }

        _selectedChannel = model.SelectedChannel;
        _staleBanner.Apply(model.SyncStatus, model.StatusBanner, model.StatusBannerTone);
        _loadingOverlay.IsVisible = model.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, model.LoadingMessage);
        RenderTabs(model);
        RenderCards(model);
    }

    private void RenderTabs(OpenOrderListDto model)
    {
        _tabs.Children.Clear();
        var accent = Color.FromArgb(CustomerOrderUiHelpers.SuccessGreen);
        var orders = model.Orders;

        void Add(string text, OpenOrderChannelKind channel, int count)
        {
            var selected = _selectedChannel == channel;
            var label = count > 0 ? $"{text} ({count})" : text;
            _tabs.Children.Add(CustomerOrderUiHelpers.CreateTab(label, selected, accent, (_, _) =>
            {
                _selectedChannel = channel;
                ChannelChanged?.Invoke(this, channel);
                Model = model with { SelectedChannel = channel };
            }));
        }

        Add("All", OpenOrderChannelKind.All, orders.Count);
        Add("Collection", OpenOrderChannelKind.Collection, orders.Count(o => o.Channel == OpenOrderChannelKind.Collection));
        Add("Delivery", OpenOrderChannelKind.Delivery, orders.Count(o => o.Channel == OpenOrderChannelKind.Delivery));
        Add("Table", OpenOrderChannelKind.Table, orders.Count(o => o.Channel == OpenOrderChannelKind.Table));
    }

    private void RenderCards(OpenOrderListDto model)
    {
        _cardHost.Children.Clear();
        var cards = _selectedChannel switch
        {
            OpenOrderChannelKind.Collection => model.Orders.Where(o => o.Channel == OpenOrderChannelKind.Collection),
            OpenOrderChannelKind.Delivery => model.Orders.Where(o => o.Channel == OpenOrderChannelKind.Delivery),
            OpenOrderChannelKind.Table => model.Orders.Where(o => o.Channel == OpenOrderChannelKind.Table),
            _ => model.Orders
        }.ToList();

        _emptyLabel.IsVisible = cards.Count == 0 && !model.IsLoading;
        foreach (var card in cards)
        {
            _cardHost.Children.Add(BuildCard(card));
        }
    }

    private View BuildCard(OpenOrderCardDto card)
    {
        var accentHex = string.IsNullOrWhiteSpace(card.AccentColor)
            ? card.Health == OpenOrderHealthKind.StaleDraft
                ? CustomerOrderUiHelpers.StaleRed
                : CustomerOrderUiHelpers.SuccessGreen
            : card.AccentColor;
        var accent = Color.FromArgb(accentHex);

        var stack = new VerticalStackLayout { Spacing = 4 };
        if (card.Badges.Count > 0)
        {
            var row = new HorizontalStackLayout { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var badge in card.Badges)
            {
                row.Children.Add(CustomerOrderUiHelpers.CreateBadge(badge, "#DBEAFE", "#1D4ED8"));
            }

            stack.Children.Add(row);
        }

        stack.Children.Add(new Label
        {
            Text = card.Channel == OpenOrderChannelKind.Table ? "Table" : card.ChannelLabel,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E293B"),
            LineBreakMode = LineBreakMode.TailTruncation
        });

        var secondary = card.Channel == OpenOrderChannelKind.Table ? card.TableLabel : card.OrderNumber;
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            stack.Children.Add(new Label
            {
                Text = secondary,
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8"),
                LineBreakMode = LineBreakMode.TailTruncation
            });
        }

        if (!string.IsNullOrWhiteSpace(card.CustomerName))
        {
            stack.Children.Add(new Label
            {
                Text = card.CustomerName.Trim(),
                FontSize = 15,
                TextColor = Color.FromArgb("#475569"),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        stack.Children.Add(new Label
        {
            Text = $"£{card.TotalAmount:F2}",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = accent,
            Margin = new Thickness(0, 6, 0, 0)
        });

        stack.Children.Add(new Label
        {
            Text = card.TimeDisplay,
            FontSize = 12,
            TextColor = Color.FromArgb("#94A3B8")
        });

        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = accent,
            StrokeThickness = 2,
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 14, 14),
            WidthRequest = 220,
            MinimumHeightRequest = 132,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.08f
            },
            Content = stack
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            OrderSelected?.Invoke(this, card);
            if (OpenOrderCommand?.CanExecute(card) == true)
            {
                OpenOrderCommand.Execute(card);
            }
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }
}
