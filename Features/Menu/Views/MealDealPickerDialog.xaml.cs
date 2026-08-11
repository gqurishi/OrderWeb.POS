using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using POS_in_NET.Helpers;
using System.Linq;

namespace POS_in_NET.Views;

public partial class MealDealPickerDialog : ContentView
{
    private readonly MealDeal _deal;
    private readonly MealDealService _mealDealService = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _choiceBorders = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<List<string>?>? _taskCompletionSource;
    private Grid? _parentGrid;

    public MealDealPickerDialog(MealDeal deal)
    {
        InitializeComponent();
        TabletLayoutHelper.AttachDialog(this, DialogCard, 720, 760);
        _deal = deal;
        TitleLabel.Text = deal.Name;
        SubtitleLabel.Text = string.IsNullOrWhiteSpace(deal.Description)
            ? $"Pick {deal.PickCount} · £{deal.Price:F2}"
            : deal.Description;
        BuildChoiceButtons();
        UpdateSelectionUi();
    }

    public Task<List<string>?> ShowAsync(Page hostPage)
    {
        _taskCompletionSource = new TaskCompletionSource<List<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (hostPage is ContentPage contentPage)
        {
            AddToPage(contentPage);
        }
        else
        {
            _taskCompletionSource.TrySetResult(null);
        }

        return _taskCompletionSource.Task;
    }

    private void AddToPage(ContentPage page)
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 5000;

        if (page.Content is Grid grid)
        {
            _parentGrid = grid;
            Grid.SetRow(this, 0);
            Grid.SetColumn(this, 0);
            if (grid.RowDefinitions.Count > 0)
            {
                Grid.SetRowSpan(this, grid.RowDefinitions.Count);
            }

            if (grid.ColumnDefinitions.Count > 0)
            {
                Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
            }

            grid.Children.Add(this);
            return;
        }

        var wrapper = new Grid();
        var existing = page.Content;
        page.Content = null;
        if (existing != null)
        {
            wrapper.Children.Add(existing);
        }

        wrapper.Children.Add(this);
        page.Content = wrapper;
        _parentGrid = wrapper;
    }

    private void BuildChoiceButtons()
    {
        ChoicesLayout.Children.Clear();
        _choiceBorders.Clear();

        foreach (var choice in _deal.Choices.OrderBy(c => c.SortOrder))
        {
            var border = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#CBD5E1"),
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(18, 14),
                Margin = new Thickness(0, 0, 10, 10),
                MinimumWidthRequest = 120
            };

            border.Content = new Label
            {
                Text = choice.Name,
                FontFamily = "OpenSansSemibold",
                FontSize = 15,
                TextColor = Color.FromArgb("#1E293B"),
                HorizontalTextAlignment = TextAlignment.Center
            };

            var tap = new TapGestureRecognizer();
            var choiceName = choice.Name;
            tap.Tapped += (_, _) => ToggleChoice(choiceName, border);
            border.GestureRecognizers.Add(tap);

            _choiceBorders[choice.Name] = border;
            ChoicesLayout.Children.Add(border);
        }
    }

    private void ToggleChoice(string name, Border border)
    {
        if (_selected.Contains(name))
        {
            _selected.Remove(name);
        }
        else
        {
            if (_selected.Count >= _deal.PickCount)
            {
                return;
            }

            _selected.Add(name);
        }

        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        SelectionStatusLabel.Text = $"{_selected.Count}/{_deal.PickCount} selected";
        ConfirmButton.IsEnabled = _selected.Count == _deal.PickCount;
        ConfirmButton.Opacity = ConfirmButton.IsEnabled ? 1.0 : 0.5;

        foreach (var pair in _choiceBorders)
        {
            var selected = _selected.Contains(pair.Key);
            pair.Value.BackgroundColor = selected ? Color.FromArgb("#FEF3C7") : Colors.White;
            pair.Value.Stroke = Color.FromArgb(selected ? "#F59E0B" : "#CBD5E1");
            pair.Value.StrokeThickness = selected ? 3 : 2;
        }
    }

    private void Close(List<string>? result)
    {
        _parentGrid?.Children.Remove(this);
        _parentGrid = null;

        _taskCompletionSource?.TrySetResult(result);
    }

    private void OnCancelClicked(object sender, EventArgs e) => Close(null);

    private async void OnConfirmClicked(object sender, EventArgs e)
    {
        var selections = _selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var (isValid, errors) = _mealDealService.ValidateChoiceSelections(_deal, selections);
        if (!isValid)
        {
            await AppAlertService.ShowAlertAsync("Invalid selection", string.Join("\n", errors));
            return;
        }

        Close(selections);
    }
}
