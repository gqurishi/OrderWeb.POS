using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class AddEditMealDealPage : ContentPage
{
    private readonly MealDealService _mealDealService = new();
    private readonly List<MealDealChoice> _choices = new();
    private MealDeal? _editingDeal;
    private bool _isEditMode;
    private bool _initialized;

    public AddEditMealDealPage()
    {
        InitializeComponent();
    }

    public AddEditMealDealPage(MealDeal deal) : this()
    {
        _isEditMode = true;
        _editingDeal = deal;
        PageTitle.Text = "Edit Meal Deal";
        PageSubtitle.Text = "Update deal price, choices, and pick rules";
        SaveButton.Text = "Update Deal";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (_isEditMode && _editingDeal != null)
        {
            DealNameEntry.Text = _editingDeal.Name;
            DescriptionEntry.Text = _editingDeal.Description;
            PriceEntry.Text = _editingDeal.Price.ToString("F2");
            PickCountEntry.Text = _editingDeal.PickCount.ToString();
            ActiveSwitch.IsToggled = _editingDeal.Active;

            _choices.Clear();
            foreach (var choice in _editingDeal.Choices.OrderBy(c => c.SortOrder))
            {
                _choices.Add(new MealDealChoice
                {
                    Id = choice.Id,
                    Name = choice.Name,
                    SortOrder = choice.SortOrder
                });
            }

            RebuildChoiceRows();
        }
        else if (!_isEditMode && _choices.Count == 0)
        {
            AddChoiceRow(string.Empty);
            AddChoiceRow(string.Empty);
            RebuildChoiceRows();
        }
    }

    private async void OnBackClicked(object sender, EventArgs e) =>
        await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation, source: sender as VisualElement);

    private void OnAddChoiceClicked(object sender, EventArgs e)
    {
        _choices.Add(new MealDealChoice { SortOrder = _choices.Count });
        RebuildChoiceRows();
    }

    private void RebuildChoiceRows()
    {
        ChoicesContainer.Children.Clear();

        for (var i = 0; i < _choices.Count; i++)
        {
            var choice = _choices[i];
            choice.SortOrder = i;

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(32) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 10
            };

            row.Add(new Label
            {
                Text = $"{i + 1}.",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#64748B"),
                VerticalOptions = LayoutOptions.Center
            }, 0);

            var entry = new Entry
            {
                Placeholder = "Choice name (e.g. Rice)",
                Text = choice.Name,
                FontSize = 15,
                MaxLength = 60
            };
            var index = i;
            entry.TextChanged += (_, e) => _choices[index].Name = e.NewTextValue?.Trim() ?? string.Empty;
            row.Add(entry, 1);

            var removeBtn = new Button
            {
                Text = "X",
                BackgroundColor = Color.FromArgb("#EF4444"),
                TextColor = Colors.White,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                WidthRequest = 52,
                HeightRequest = 44,
                CornerRadius = 8,
                Padding = 0
            };
            removeBtn.Clicked += (_, _) =>
            {
                if (_choices.Count <= 2)
                {
                    _ = AppAlertService.ShowAlertAsync("Minimum Choices", "A meal deal needs at least 2 choices.");
                    return;
                }

                _choices.RemoveAt(index);
                RebuildChoiceRows();
            };
            row.Add(removeBtn, 2);

            ChoicesContainer.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12, 8),
                Content = row
            });
        }

        ChoicesEmptyLabel.IsVisible = _choices.Count == 0;
    }

    private void AddChoiceRow(string name)
    {
        _choices.Add(new MealDealChoice
        {
            Name = name,
            SortOrder = _choices.Count
        });
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var name = DealNameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await AppAlertService.ShowAlertAsync("Validation", "Please enter a deal name.");
                return;
            }

            if (!decimal.TryParse(PriceEntry.Text, out var price) || price <= 0)
            {
                await AppAlertService.ShowAlertAsync("Validation", "Please enter a valid deal price.");
                return;
            }

            if (!int.TryParse(PickCountEntry.Text, out var pickCount) || pickCount < 1)
            {
                await AppAlertService.ShowAlertAsync("Validation", "Please enter how many items the customer must pick.");
                return;
            }

            var validChoiceRows = _choices
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();

            var normalizedChoices = validChoiceRows
                .Select((c, idx) => new MealDealChoice
                {
                    Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString() : c.Id,
                    Name = c.Name.Trim(),
                    SortOrder = idx
                })
                .ToList();

            if (normalizedChoices.Count < 2)
            {
                await AppAlertService.ShowAlertAsync("Validation", "Add at least 2 choice names.");
                return;
            }

            if (pickCount > normalizedChoices.Count)
            {
                await AppAlertService.ShowAlertAsync("Validation", $"Customer can pick at most {normalizedChoices.Count} (you have {normalizedChoices.Count} choices).");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Text = "Saving...";

            try
            {
                if (_isEditMode && _editingDeal != null)
                {
                    _editingDeal.Name = name;
                    _editingDeal.Description = string.IsNullOrWhiteSpace(DescriptionEntry.Text) ? null : DescriptionEntry.Text.Trim();
                    _editingDeal.Price = price;
                    _editingDeal.PickCount = pickCount;
                    _editingDeal.Choices = normalizedChoices;
                    _editingDeal.Active = ActiveSwitch.IsToggled;
                    _editingDeal.UpdatedAt = DateTime.Now;

                    if (!await _mealDealService.UpdateDealAsync(_editingDeal))
                    {
                        await AppAlertService.ShowAlertAsync("Error", "Could not update meal deal.");
                        return;
                    }

                    await AppAlertService.ShowAlertAsync("Success", "Meal deal updated.");
                }
                else
                {
                    var deal = new MealDeal
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Description = string.IsNullOrWhiteSpace(DescriptionEntry.Text) ? null : DescriptionEntry.Text.Trim(),
                        Price = price,
                        PickCount = pickCount,
                        Choices = normalizedChoices,
                        Active = ActiveSwitch.IsToggled,
                        Color = "#F59E0B",
                        DisplayOrder = 0,
                        VatCategory = "HotFood",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    if (!await _mealDealService.CreateDealAsync(deal))
                    {
                        await AppAlertService.ShowAlertAsync("Error", "Could not create meal deal.");
                        return;
                    }

                    await AppAlertService.ShowAlertAsync("Success", "Meal deal created.");
                }

                OrderPlacementPageSimple.InvalidateMenuCache();
                await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Text = _isEditMode ? "Update Deal" : "Save Deal";
            }
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", ex.Message);
        }
    }
}
