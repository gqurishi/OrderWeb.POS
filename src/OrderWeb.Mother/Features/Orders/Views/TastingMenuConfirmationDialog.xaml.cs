using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Helpers;
using POS_in_NET.Services;

namespace POS_in_NET.Views;

public partial class TastingMenuConfirmationDialog : ContentView
{
    private TaskCompletionSource<bool>? _completion;
    private Grid? _parentGrid;

    public TastingMenuConfirmationDialog()
    {
        InitializeComponent();
        TabletLayoutHelper.AttachDialog(this, DialogCard, 680, 720);
    }

    public Task<bool> ShowAsync(TastingMenu menu, TastingMenuOption option)
    {
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MenuNameLabel.Text = menu.Name;
        PriceLabel.Text = $"£{option.Price:F2}";
        CourseCountLabel.Text = $"{option.CourseCount} courses";
        WineLabel.Text = option.IncludesWine ? "With Wine" : "Without Wine";
        VatLabel.Text = FormatVat(menu.VatCategory);

        CoursesList.ItemsSource = menu.Courses
            .OrderBy(course => course.CourseNumber)
            .Where(course => option.CourseCount <= 0 || course.CourseNumber <= option.CourseCount)
            .Select(course => new CoursePreview(course, option.IncludesWine))
            .ToList();

        if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
        {
            _completion.TrySetResult(false);
        }

        return _completion.Task;
    }

    private void OnAddClicked(object sender, EventArgs e) => Close(true);

    private void OnCancelClicked(object sender, EventArgs e) => Close(false);

    private void Close(bool result)
    {
        DialogOverlayHelper.DetachOverlay(this, _parentGrid);
        _parentGrid = null;
        _completion?.TrySetResult(result);
    }

    private static string FormatVat(string? value) => (value ?? string.Empty).Trim() switch
    {
        "ColdFood" => "Cold Food · 0% VAT",
        "HotBeverage" => "Hot Drink · 20% VAT",
        "ColdBeverage" => "Cold Drink · 0% VAT",
        "Alcohol" => "Alcohol · 20% VAT",
        "NoVAT" => "No VAT · 0%",
        _ => "Hot Food · 20% VAT"
    };

    private sealed class CoursePreview
    {
        public CoursePreview(TastingMenuCourse course, bool includesWine)
        {
            Number = course.CourseNumber;
            Name = course.Name;
            WineDisplay = includesWine && !string.IsNullOrWhiteSpace(course.WineName)
                ? $"Wine: {course.WineName}"
                : string.Empty;
        }

        public int Number { get; }
        public string Name { get; }
        public string WineDisplay { get; }
        public bool HasWine => !string.IsNullOrWhiteSpace(WineDisplay);
    }
}
