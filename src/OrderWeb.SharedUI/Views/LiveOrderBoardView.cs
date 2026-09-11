using Microsoft.Maui.Layouts;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-style Live Order board: filter strip + FlexLayout cards + empty label.
/// Presentation only — hosts own load, refresh, and navigation.
/// </summary>
public sealed class LiveOrderBoardView : ContentView
{
    private readonly LiveOrderFilterBar _filterBar = new();
    private readonly FlexLayout _grid = new()
    {
        Direction = FlexDirection.Row,
        Wrap = FlexWrap.Wrap,
        AlignItems = FlexAlignItems.Start,
        JustifyContent = FlexJustify.Start
    };
    private readonly Label _emptyLabel = new()
    {
        Text = LiveOrderSampleData.EmptyTextFor(LiveOrderFilter.All),
        FontSize = 16,
        TextColor = Color.FromArgb("#9CA3AF"),
        HorizontalOptions = LayoutOptions.Center,
        Margin = new Thickness(0, 40, 0, 0),
        IsVisible = true
    };

    private IReadOnlyList<LiveOrderCardPresentation> _cards = Array.Empty<LiveOrderCardPresentation>();
    private bool _suppressFilterEvent;
    private bool _applyingCards;
    private bool _sampleMode;

    public static readonly BindableProperty SelectedFilterProperty = BindableProperty.Create(
        nameof(SelectedFilter),
        typeof(LiveOrderFilter),
        typeof(LiveOrderBoardView),
        LiveOrderFilter.All,
        propertyChanged: (b, _, n) => ((LiveOrderBoardView)b).OnSelectedFilterBindableChanged((LiveOrderFilter)n));

    public static readonly BindableProperty CardsProperty = BindableProperty.Create(
        nameof(Cards),
        typeof(IReadOnlyList<LiveOrderCardPresentation>),
        typeof(LiveOrderBoardView),
        Array.Empty<LiveOrderCardPresentation>(),
        propertyChanged: (b, _, n) =>
        {
            var view = (LiveOrderBoardView)b;
            if (view._applyingCards)
            {
                return;
            }

            view.SetCards(n as IReadOnlyList<LiveOrderCardPresentation> ?? Array.Empty<LiveOrderCardPresentation>());
        });

    public static readonly BindableProperty EmptyTextProperty = BindableProperty.Create(
        nameof(EmptyText),
        typeof(string),
        typeof(LiveOrderBoardView),
        LiveOrderSampleData.EmptyTextFor(LiveOrderFilter.All),
        propertyChanged: (b, _, n) =>
        {
            var view = (LiveOrderBoardView)b;
            view._emptyLabel.Text = n as string ?? string.Empty;
            view._emptyLabel.IsVisible = view._cards.Count == 0;
        });

    public LiveOrderFilter SelectedFilter
    {
        get => (LiveOrderFilter)GetValue(SelectedFilterProperty);
        set => SetValue(SelectedFilterProperty, value);
    }

    public IReadOnlyList<LiveOrderCardPresentation> Cards
    {
        get => (IReadOnlyList<LiveOrderCardPresentation>)GetValue(CardsProperty);
        set => SetValue(CardsProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public event EventHandler<LiveOrderFilterChangedEventArgs>? FilterChanged;
    public event EventHandler<LiveOrderCardTappedEventArgs>? CardTapped;

    public LiveOrderBoardView()
    {
        _filterBar.FilterChanged += (_, e) =>
        {
            if (_suppressFilterEvent)
            {
                return;
            }

            if (SelectedFilter != e.Filter)
            {
                _suppressFilterEvent = true;
                try
                {
                    SelectedFilter = e.Filter;
                }
                finally
                {
                    _suppressFilterEvent = false;
                }
            }

            if (_sampleMode)
            {
                ApplyCards(LiveOrderSampleData.CardsFor(e.Filter), LiveOrderSampleData.EmptyTextFor(e.Filter));
            }

            FilterChanged?.Invoke(this, e);
        };

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(16),
            Spacing = 12,
            Children = { _grid, _emptyLabel }
        };

        var scroll = new ScrollView { Content = body };
        Grid.SetRow(scroll, 1);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children =
            {
                _filterBar,
                scroll
            }
        };
    }

    /// <summary>Replace board cards (safe FlexLayout clear). Host maps models → presentation.</summary>
    public void SetCards(IReadOnlyList<LiveOrderCardPresentation> cards, string? emptyText = null)
    {
        _sampleMode = false;
        ApplyCards(cards, emptyText);
    }

    /// <summary>Load Mother-look sample cards for the current filter (Phase 1 smoke). Tab changes refresh samples until <see cref="SetCards"/> is used by a real host.</summary>
    public void ShowSampleData()
    {
        _sampleMode = true;
        ApplyCards(LiveOrderSampleData.CardsFor(SelectedFilter), LiveOrderSampleData.EmptyTextFor(SelectedFilter));
    }

    private void ApplyCards(IReadOnlyList<LiveOrderCardPresentation> cards, string? emptyText)
    {
        _cards = cards ?? Array.Empty<LiveOrderCardPresentation>();

        if (emptyText is not null && EmptyText != emptyText)
        {
            EmptyText = emptyText;
        }
        else
        {
            _emptyLabel.Text = EmptyText;
        }

        var views = new List<View>(_cards.Count);
        foreach (var presentation in _cards)
        {
            var card = new LiveOrderCard(presentation);
            card.Tapped += (_, e) => CardTapped?.Invoke(this, e);
            views.Add(card);
        }

        SafeFlexLayout.RemoveAllChildren(_grid);
        foreach (var view in views)
        {
            _grid.Children.Add(view);
        }

        _emptyLabel.IsVisible = _cards.Count == 0;

        if (!_applyingCards && !ReferenceEquals(GetValue(CardsProperty), _cards))
        {
            _applyingCards = true;
            try
            {
                SetValue(CardsProperty, _cards);
            }
            finally
            {
                _applyingCards = false;
            }
        }
    }

    private void OnSelectedFilterBindableChanged(LiveOrderFilter filter)
    {
        _filterBar.SetSelectedFilterQuiet(filter);

        if (_suppressFilterEvent)
        {
            return;
        }

        if (_sampleMode)
        {
            ApplyCards(LiveOrderSampleData.CardsFor(filter), LiveOrderSampleData.EmptyTextFor(filter));
        }

        FilterChanged?.Invoke(this, new LiveOrderFilterChangedEventArgs(filter));
    }
}
