using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class AddEditTastingMenuPage : ContentPage
{
    private const int MaximumCourseCount = 100;

    private static readonly string[] VatOptions =
    {
        "Hot Food (20%)",
        "Cold Food (0%)",
        "Hot Beverage (20%)",
        "Cold Beverage (0%)",
        "Alcohol (20%)",
        "No VAT (0%)"
    };

    private readonly TastingMenuService _service = new();
    private readonly PrintGroupService _printGroupService = new();
    private readonly List<CourseRow> _courseRows = new();
    private readonly TastingMenu? _editingMenu;
    private string _packageOptionId = Guid.NewGuid().ToString();
    private string _menuColor = "#0EA5E9";
    private string? _savedFoodPrintGroupId;
    private string? _savedWinePrintGroupId;
    private bool _printerGroupsLoaded;

    public AddEditTastingMenuPage()
    {
        InitializeComponent();
        VatCategoryPicker.ItemsSource = VatOptions;
        VatCategoryPicker.SelectedIndex = 0;
        CourseCountEntry.Text = "4";

        LoadCourseRows(Array.Empty<TastingMenuCourse>(), 4);
        RefreshCourseRows();
    }

    public AddEditTastingMenuPage(TastingMenu menu) : this()
    {
        _editingMenu = menu;
        PageTitle.Text = "Edit Tasting Menu";
        SaveButton.Text = "Update";
        NameEntry.Text = menu.Name;
        ActiveSwitch.IsToggled = menu.Active;
        _menuColor = string.IsNullOrWhiteSpace(menu.Color) ? "#0EA5E9" : menu.Color;
        _savedFoodPrintGroupId = menu.FoodPrintGroupId;
        _savedWinePrintGroupId = menu.WinePrintGroupId;

        var option = menu.Options.OrderBy(candidate => candidate.SortOrder).FirstOrDefault();
        if (option != null)
        {
            _packageOptionId = string.IsNullOrWhiteSpace(option.Id) ? Guid.NewGuid().ToString() : option.Id;
            PriceEntry.Text = option.Price.ToString("0.00");
            CourseCountEntry.Text = Math.Max(1, option.CourseCount).ToString();
            IncludesWineSwitch.IsToggled = option.IncludesWine;
        }

        VatCategoryPicker.SelectedIndex = GetVatIndex(menu.VatCategory);
        var savedCourseCount = option?.CourseCount > 0
            ? option.CourseCount
            : Math.Max(1, menu.Courses.Count);
        CourseCountEntry.Text = savedCourseCount.ToString();
        LoadCourseRows(menu.Courses, savedCourseCount);
        RefreshCourseRows();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_printerGroupsLoaded)
        {
            return;
        }

        _printerGroupsLoaded = true;
        try
        {
            var groups = (await _printGroupService.GetAllPrintGroupsAsync())
                .Where(group => group.IsActive
                    || string.Equals(group.Id, _savedFoodPrintGroupId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(group.Id, _savedWinePrintGroupId, StringComparison.OrdinalIgnoreCase))
                .Where(group => !string.Equals(group.PrinterType, "label", StringComparison.OrdinalIgnoreCase))
                .OrderBy(group => group.DisplayOrder)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FoodPrintGroupPicker.ItemsSource = BuildPrintGroupChoices("Automatic Kitchen", groups);
            WinePrintGroupPicker.ItemsSource = BuildPrintGroupChoices("Automatic Bar", groups);
            SelectPrintGroup(FoodPrintGroupPicker, _savedFoodPrintGroupId);
            SelectPrintGroup(WinePrintGroupPicker, _savedWinePrintGroupId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to load tasting-menu print groups: {ex.Message}");
            FoodPrintGroupPicker.ItemsSource = BuildPrintGroupChoices("Automatic Kitchen", Array.Empty<PrintGroup>());
            WinePrintGroupPicker.ItemsSource = BuildPrintGroupChoices("Automatic Bar", Array.Empty<PrintGroup>());
            FoodPrintGroupPicker.SelectedIndex = 0;
            WinePrintGroupPicker.SelectedIndex = 0;
        }
    }

    private void LoadCourseRows(IEnumerable<TastingMenuCourse> existingCourses, int requestedCount)
    {
        _courseRows.Clear();
        CoursesContainer.Children.Clear();
        var existing = existingCourses
            .OrderBy(course => course.CourseNumber)
            .ToList();
        var rowCount = Math.Max(requestedCount, existing.Count);
        for (var number = 1; number <= rowCount; number++)
        {
            var course = existing.FirstOrDefault(candidate => candidate.CourseNumber == number)
                ?? existing.ElementAtOrDefault(number - 1)
                ?? new TastingMenuCourse { CourseNumber = number };
            var row = new CourseRow(number, course);
            _courseRows.Add(row);
            CoursesContainer.Children.Add(row.Root);
        }
    }

    private int SelectedCourseCount =>
        int.TryParse(CourseCountEntry.Text?.Trim(), out var count) && count > 0 && count <= MaximumCourseCount
            ? count
            : 0;

    private void OnCourseCountChanged(object sender, TextChangedEventArgs e)
    {
        var courseCount = SelectedCourseCount;
        if (courseCount > 0)
        {
            EnsureCourseRows(courseCount);
        }

        RefreshCourseRows();
    }

    private void OnWineToggled(object sender, ToggledEventArgs e) => RefreshCourseRows();

    private void RefreshCourseRows()
    {
        var courseCount = SelectedCourseCount;
        var includesWine = IncludesWineSwitch.IsToggled;
        WineRoutingField.IsVisible = includesWine;
        for (var index = 0; index < _courseRows.Count; index++)
        {
            _courseRows[index].Root.IsVisible = index < courseCount;
            _courseRows[index].SetWineEnabled(includesWine);
        }

        CoursesHelpLabel.Text = courseCount == 0
            ? $"Enter a number from 1 to {MaximumCourseCount} to create the course fields."
            : includesWine
                ? $"Enter {courseCount} course names and the matching wine for each course."
                : $"Enter the {courseCount} course names in the order they will be served.";
    }

    private void EnsureCourseRows(int courseCount)
    {
        while (_courseRows.Count < courseCount)
        {
            var number = _courseRows.Count + 1;
            var row = new CourseRow(number, new TastingMenuCourse { CourseNumber = number });
            _courseRows.Add(row);
            CoursesContainer.Children.Add(row.Root);
        }
    }

    private async void OnBackClicked(object sender, EventArgs e) =>
        await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation, source: sender as VisualElement);

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var name = NameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await AppAlertService.ShowAlertAsync("Missing Name", "Enter a tasting-menu name, for example Non-Veg 4 Course.");
                return;
            }

            if (!decimal.TryParse(PriceEntry.Text, out var price) || price <= 0m)
            {
                await AppAlertService.ShowAlertAsync("Invalid Price", "Enter a package price above zero.");
                return;
            }

            var courseCount = SelectedCourseCount;
            if (courseCount == 0)
            {
                await AppAlertService.ShowAlertAsync(
                    "Invalid Course Count",
                    $"Enter a number of courses from 1 to {MaximumCourseCount}.");
                return;
            }

            EnsureCourseRows(courseCount);
            var includesWine = IncludesWineSwitch.IsToggled;
            var vatCategory = GetVatCategory(VatCategoryPicker.SelectedIndex);
            var courses = _courseRows
                .Take(courseCount)
                .Select(row => row.ToCourse(vatCategory, includesWine))
                .ToList();

            if (courses.Any(course => string.IsNullOrWhiteSpace(course.Name)))
            {
                await AppAlertService.ShowAlertAsync("Missing Course", $"Enter a name for all {courseCount} courses.");
                return;
            }

            if (includesWine && courses.Any(course => string.IsNullOrWhiteSpace(course.WineName)))
            {
                await AppAlertService.ShowAlertAsync("Missing Wine", "Enter the wine name paired with every course.");
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Text = "Saving...";

            var menu = _editingMenu ?? new TastingMenu
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now
            };
            menu.Name = name;
            menu.Description = null;
            menu.Color = _menuColor;
            menu.VatCategory = vatCategory;
            menu.Active = ActiveSwitch.IsToggled;
            menu.ManualCourseCalling = true;
            menu.FoodPrintGroupId = _printerGroupsLoaded
                ? GetSelectedPrintGroupId(FoodPrintGroupPicker)
                : _savedFoodPrintGroupId;
            menu.WinePrintGroupId = includesWine
                ? _printerGroupsLoaded
                    ? GetSelectedPrintGroupId(WinePrintGroupPicker)
                    : _savedWinePrintGroupId
                : null;
            menu.Options = new List<TastingMenuOption>
            {
                new()
                {
                    Id = _packageOptionId,
                    Name = name,
                    Price = price,
                    CourseCount = courseCount,
                    IncludesWine = includesWine,
                    SortOrder = 0
                }
            };
            menu.Courses = courses;
            menu.UpdatedAt = DateTime.Now;

            var saved = _editingMenu == null
                ? await _service.CreateAsync(menu)
                : await _service.UpdateAsync(menu);
            if (!saved)
            {
                await AppAlertService.ShowAlertAsync("Save Failed", "Tasting menu was not saved.");
                return;
            }

            OrderPlacementPageSimple.InvalidateMenuCache();
            await AppAlertService.ShowAlertAsync("Success", "Tasting menu saved.");
            await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Text = _editingMenu == null ? "Save" : "Update";
        }
    }

    private static int GetVatIndex(string? vatCategory) => (vatCategory ?? string.Empty).Trim() switch
    {
        "ColdFood" => 1,
        "HotBeverage" => 2,
        "ColdBeverage" => 3,
        "Alcohol" => 4,
        "NoVAT" => 5,
        _ => 0
    };

    private static string GetVatCategory(int selectedIndex) => selectedIndex switch
    {
        1 => "ColdFood",
        2 => "HotBeverage",
        3 => "ColdBeverage",
        4 => "Alcohol",
        5 => "NoVAT",
        _ => "HotFood"
    };

    private static List<PrintGroupChoice> BuildPrintGroupChoices(string automaticLabel, IEnumerable<PrintGroup> groups)
    {
        var choices = new List<PrintGroupChoice> { new(null, automaticLabel) };
        choices.AddRange(groups.Select(group => new PrintGroupChoice(
            group.Id,
            $"{group.Name} · {group.PrinterType}{(group.IsActive ? string.Empty : " · Inactive")}")));
        return choices;
    }

    private static void SelectPrintGroup(Picker picker, string? groupId)
    {
        var choices = picker.ItemsSource?.Cast<PrintGroupChoice>().ToList() ?? new List<PrintGroupChoice>();
        var index = string.IsNullOrWhiteSpace(groupId)
            ? 0
            : choices.FindIndex(choice => string.Equals(choice.Id, groupId, StringComparison.OrdinalIgnoreCase));
        picker.SelectedIndex = index < 0 ? 0 : index;
    }

    private static string? GetSelectedPrintGroupId(Picker picker) =>
        picker.SelectedItem is PrintGroupChoice choice ? choice.Id : null;

    private sealed record PrintGroupChoice(string? Id, string DisplayName);

    private sealed class CourseRow
    {
        private readonly string _id;
        private readonly int _number;
        private readonly Entry _courseName = new() { Placeholder = "Course name, e.g. Chicken" };
        private readonly Entry _wineName = new() { Placeholder = "Wine name, e.g. Chardonnay" };
        private readonly ColumnDefinition _wineColumn = new(GridLength.Star);
        private readonly VerticalStackLayout _wineField;

        public CourseRow(int number, TastingMenuCourse course)
        {
            _number = number;
            _id = string.IsNullOrWhiteSpace(course.Id) ? Guid.NewGuid().ToString() : course.Id;
            _courseName.Text = course.Name;
            _wineName.Text = course.WineName;
            _wineField = BuildField("Wine pairing", _wineName);

            Root = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 54 },
                    new ColumnDefinition { Width = GridLength.Star },
                    _wineColumn
                },
                ColumnSpacing = 14,
                Padding = new Thickness(12, 10),
                BackgroundColor = Colors.White
            };

            Root.Add(new Border
            {
                WidthRequest = 42,
                HeightRequest = 42,
                BackgroundColor = Color.FromArgb("#0EA5E9"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Content = new Label
                {
                    Text = number.ToString(),
                    TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 16,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }, 0);
            Root.Add(BuildField("Course name", _courseName), 1);
            Root.Add(_wineField, 2);
        }

        public Grid Root { get; }

        public void SetWineEnabled(bool enabled)
        {
            _wineField.IsVisible = enabled;
            _wineColumn.Width = enabled ? GridLength.Star : new GridLength(0);
        }

        public TastingMenuCourse ToCourse(string vatCategory, bool includesWine) => new()
        {
            Id = _id,
            Name = _courseName.Text?.Trim() ?? string.Empty,
            WineName = includesWine ? _wineName.Text?.Trim() : null,
            CourseNumber = _number,
            Required = true,
            VatCategory = vatCategory,
            Choices = new List<TastingMenuChoice>()
        };

        private static VerticalStackLayout BuildField(string label, Entry entry) => new()
        {
            Spacing = 5,
            Children =
            {
                new Label { Text = label, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#475569") },
                entry
            }
        };
    }
}
