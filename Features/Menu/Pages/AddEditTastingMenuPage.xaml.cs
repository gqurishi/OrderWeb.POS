using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class AddEditTastingMenuPage : ContentPage
{
    private readonly TastingMenuService _service = new();
    private readonly List<OptionRow> _optionRows = new();
    private readonly List<CourseRow> _courseRows = new();
    private readonly TastingMenu? _editingMenu;

    public AddEditTastingMenuPage()
    {
        InitializeComponent();
        AddOptionRow(new TastingMenuOption { Name = "4 Course Without Wine", CourseCount = 4, Price = 0, SortOrder = 0 });
        AddOptionRow(new TastingMenuOption { Name = "4 Course With Wine", CourseCount = 4, IncludesWine = true, Price = 0, SortOrder = 1 });
        AddOptionRow(new TastingMenuOption { Name = "5 Course Without Wine", CourseCount = 5, Price = 0, SortOrder = 2 });
        AddOptionRow(new TastingMenuOption { Name = "5 Course With Wine", CourseCount = 5, IncludesWine = true, Price = 0, SortOrder = 3 });
        AddCourseRow(new TastingMenuCourse { Name = "Starter", CourseNumber = 1 });
        AddCourseRow(new TastingMenuCourse { Name = "Main", CourseNumber = 2 });
        AddCourseRow(new TastingMenuCourse { Name = "Dessert", CourseNumber = 3 });
    }

    public AddEditTastingMenuPage(TastingMenu menu) : this()
    {
        _editingMenu = menu;
        PageTitle.Text = "Edit Tasting Menu";
        SaveButton.Text = "Update";

        NameEntry.Text = menu.Name;
        DescriptionEditor.Text = menu.Description;
        ColorEntry.Text = menu.Color;
        VatCategoryEntry.Text = menu.VatCategory;
        ActiveSwitch.IsToggled = menu.Active;
        ManualCallSwitch.IsToggled = menu.ManualCourseCalling;

        _optionRows.Clear();
        OptionsContainer.Children.Clear();
        foreach (var option in menu.Options.OrderBy(o => o.SortOrder))
        {
            AddOptionRow(option);
        }

        _courseRows.Clear();
        CoursesContainer.Children.Clear();
        foreach (var course in menu.Courses.OrderBy(c => c.CourseNumber))
        {
            AddCourseRow(course);
        }
    }

    private void OnAddOptionClicked(object sender, EventArgs e)
    {
        AddOptionRow(new TastingMenuOption { SortOrder = _optionRows.Count, CourseCount = 4 });
    }

    private void OnAddCourseClicked(object sender, EventArgs e)
    {
        AddCourseRow(new TastingMenuCourse { CourseNumber = _courseRows.Count + 1 });
    }

    private void AddOptionRow(TastingMenuOption option)
    {
        var row = new OptionRow(option, RemoveOptionRow);
        _optionRows.Add(row);
        OptionsContainer.Children.Add(row.Root);
    }

    private void AddCourseRow(TastingMenuCourse course)
    {
        var row = new CourseRow(course, RemoveCourseRow);
        _courseRows.Add(row);
        CoursesContainer.Children.Add(row.Root);
    }

    private void RemoveOptionRow(OptionRow row)
    {
        _optionRows.Remove(row);
        OptionsContainer.Children.Remove(row.Root);
    }

    private void RemoveCourseRow(CourseRow row)
    {
        _courseRows.Remove(row);
        CoursesContainer.Children.Remove(row.Root);
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var name = NameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await AppAlertService.ShowAlertAsync("Missing Name", "Enter a tasting menu name.");
                return;
            }

            var options = _optionRows
                .Select((row, index) => row.ToOption(index))
                .Where(option => !string.IsNullOrWhiteSpace(option.Name))
                .ToList();

            if (options.Count == 0)
            {
                await AppAlertService.ShowAlertAsync("Missing Options", "Add at least one price option.");
                return;
            }

            var courses = _courseRows
                .Select((row, index) => row.ToCourse(index + 1))
                .Where(course => !string.IsNullOrWhiteSpace(course.Name) && course.Choices.Count > 0)
                .ToList();

            if (courses.Count == 0)
            {
                await AppAlertService.ShowAlertAsync("Missing Courses", "Add at least one course with dish choices.");
                return;
            }

            var menu = _editingMenu ?? new TastingMenu { Id = Guid.NewGuid().ToString(), CreatedAt = DateTime.Now };
            menu.Name = name;
            menu.Description = string.IsNullOrWhiteSpace(DescriptionEditor.Text) ? null : DescriptionEditor.Text.Trim();
            menu.Color = string.IsNullOrWhiteSpace(ColorEntry.Text) ? "#0EA5E9" : ColorEntry.Text.Trim();
            menu.VatCategory = string.IsNullOrWhiteSpace(VatCategoryEntry.Text) ? "HotFood" : VatCategoryEntry.Text.Trim();
            menu.Active = ActiveSwitch.IsToggled;
            menu.ManualCourseCalling = ManualCallSwitch.IsToggled;
            menu.Options = options;
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
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", ex.Message);
        }
    }

    private sealed class OptionRow
    {
        public Grid Root { get; }
        private readonly string _id;
        private readonly Action<OptionRow> _onDelete;
        private readonly Entry _name = new() { Placeholder = "Option name" };
        private readonly Entry _price = new() { Placeholder = "Price", Keyboard = Keyboard.Numeric };
        private readonly Entry _courseCount = new() { Placeholder = "Courses", Keyboard = Keyboard.Numeric };
        private readonly Switch _wine = new();

        public OptionRow(TastingMenuOption option, Action<OptionRow> onDelete)
        {
            _id = string.IsNullOrWhiteSpace(option.Id) ? Guid.NewGuid().ToString() : option.Id;
            _onDelete = onDelete;
            _name.Text = option.Name;
            _price.Text = option.Price.ToString("0.00");
            _courseCount.Text = option.CourseCount <= 0 ? string.Empty : option.CourseCount.ToString();
            _wine.IsToggled = option.IncludesWine;

            Root = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 120 },
                    new ColumnDefinition { Width = 100 },
                    new ColumnDefinition { Width = 120 },
                    new ColumnDefinition { Width = 44 }
                },
                ColumnSpacing = 10,
                Padding = new Thickness(10),
                BackgroundColor = Colors.White
            };

            Root.Add(_name, 0, 0);
            Root.Add(_price, 1, 0);
            Root.Add(_courseCount, 2, 0);
            Root.Add(new HorizontalStackLayout
            {
                Spacing = 6,
                Children = { _wine, new Label { Text = "Wine", VerticalOptions = LayoutOptions.Center } }
            }, 3, 0);
            Root.Add(CreateDeleteButton(() => _onDelete(this)), 4, 0);
        }

        public TastingMenuOption ToOption(int index)
        {
            decimal.TryParse(_price.Text, out var price);
            int.TryParse(_courseCount.Text, out var courseCount);
            return new TastingMenuOption
            {
                Id = _id,
                Name = _name.Text?.Trim() ?? string.Empty,
                Price = Math.Max(0, price),
                CourseCount = Math.Max(0, courseCount),
                IncludesWine = _wine.IsToggled,
                SortOrder = index
            };
        }
    }

    private sealed class CourseRow
    {
        public Grid Root { get; }
        private readonly string _id;
        private readonly Action<CourseRow> _onDelete;
        private readonly Entry _name = new() { Placeholder = "Course name" };
        private readonly Entry _choices = new() { Placeholder = "Choices: Scallops, Beef Tartare, Tomato Salad" };
        private readonly Dictionary<string, string> _choiceIds = new(StringComparer.OrdinalIgnoreCase);

        public CourseRow(TastingMenuCourse course, Action<CourseRow> onDelete)
        {
            _id = string.IsNullOrWhiteSpace(course.Id) ? Guid.NewGuid().ToString() : course.Id;
            _onDelete = onDelete;
            _name.Text = course.Name;
            _choices.Text = string.Join(", ", course.Choices.OrderBy(c => c.SortOrder).Select(c => c.Name));
            foreach (var choice in course.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice.Name)))
            {
                _choiceIds[choice.Name.Trim()] = string.IsNullOrWhiteSpace(choice.Id) ? Guid.NewGuid().ToString() : choice.Id;
            }

            Root = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 180 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 44 }
                },
                ColumnSpacing = 10,
                Padding = new Thickness(10),
                BackgroundColor = Colors.White
            };

            Root.Add(_name, 0, 0);
            Root.Add(_choices, 1, 0);
            Root.Add(CreateDeleteButton(() => _onDelete(this)), 2, 0);
        }

        public TastingMenuCourse ToCourse(int courseNumber)
        {
            var choices = (_choices.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((name, index) => new TastingMenuChoice
                {
                    Id = _choiceIds.TryGetValue(name, out var existingId) ? existingId : Guid.NewGuid().ToString(),
                    Name = name,
                    SortOrder = index
                })
                .ToList();

            return new TastingMenuCourse
            {
                Id = _id,
                Name = _name.Text?.Trim() ?? string.Empty,
                CourseNumber = courseNumber,
                Required = true,
                Choices = choices
            };
        }
    }

    private static Button CreateDeleteButton(Action onDelete)
    {
        var button = new Button
        {
            Text = "X",
            BackgroundColor = Color.FromArgb("#EF4444"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            WidthRequest = 36,
            HeightRequest = 36,
            CornerRadius = 18,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        };

        button.Clicked += (_, _) => onDelete();
        return button;
    }
}
