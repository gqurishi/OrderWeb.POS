using POS_in_NET.Helpers;
using POS_in_NET.Models;

namespace POS_in_NET.Views;

public partial class TastingCourseProgressDialog : ContentView
{
    private TaskCompletionSource<TableOrderItem?>? _completion;
    private Grid? _parentGrid;

    public TastingCourseProgressDialog()
    {
        InitializeComponent();
        TabletLayoutHelper.AttachDialog(this, DialogCard, 720, 720);
    }

    public Task<TableOrderItem?> ShowAsync(TableOrderItem package, IReadOnlyList<TableOrderItem> courses)
    {
        _completion = new TaskCompletionSource<TableOrderItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PackageLabel.Text = $"{package.DisplayName} x {package.Quantity}";
        CoursesList.ItemsSource = courses
            .OrderBy(GetCourseNumber)
            .Select(course => new TastingCourseProgressRow(course, GetCourseNumber(course)))
            .ToList();

        if (Application.Current?.MainPage is Shell shell && shell.CurrentPage is ContentPage currentPage)
        {
            AddToPage(currentPage);
        }
        else if (Application.Current?.MainPage is ContentPage page)
        {
            AddToPage(page);
        }

        return _completion.Task;
    }

    private void AddToPage(ContentPage page)
    {
        if (page.Content is not Grid grid)
        {
            var wrapper = new Grid();
            var existing = page.Content;
            page.Content = null;
            if (existing != null) wrapper.Children.Add(existing);
            page.Content = wrapper;
            grid = wrapper;
        }

        _parentGrid = grid;
        Grid.SetRowSpan(this, Math.Max(1, grid.RowDefinitions.Count));
        Grid.SetColumnSpan(this, Math.Max(1, grid.ColumnDefinitions.Count));
        grid.Children.Add(this);
    }

    private void OnFireClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TableOrderItem course } && !course.IsCourseFired)
        {
            Close(course);
        }
    }

    private void OnCloseClicked(object sender, EventArgs e) => Close(null);

    private void Close(TableOrderItem? result)
    {
        _parentGrid?.Children.Remove(this);
        _completion?.TrySetResult(result);
    }

    public static int GetCourseNumber(TableOrderItem course)
    {
        var value = course.VariantName?.Split('/', StringSplitOptions.TrimEntries)[0];
        return int.TryParse(value, out var number) ? number : int.MaxValue;
    }

    private sealed class TastingCourseProgressRow
    {
        public TastingCourseProgressRow(TableOrderItem course, int number)
        {
            Course = course;
            CourseNumber = number == int.MaxValue ? "-" : number.ToString();
        }

        public TableOrderItem Course { get; }
        public string CourseNumber { get; }
        public string CourseName => Course.Name;
        public string PairingDisplay => Course.Notes ?? string.Empty;
        public bool HasPairing => !string.IsNullOrWhiteSpace(PairingDisplay);
        public bool CanFire => !Course.IsCourseFired;
        public string ActionText => CanFire ? "Fire" : "Fired";
        public string ActionColor => CanFire ? "#F59E0B" : "#94A3B8";
        public string NumberBackground => CanFire ? "#0EA5E9" : "#10B981";
        public string StatusDisplay => Course.IsCourseFired
            ? $"FIRED {Course.FiredAt:HH:mm} by {Course.FiredBy ?? "Staff"}"
            : "WAITING";
        public string StatusColor => CanFire ? "#B45309" : "#047857";
    }
}
